"""Synthetic marker/page evidence, not production approval or a pixel-equivalence assertion."""
import json
import re
import sys
from pathlib import Path
from pypdf import PdfReader

folder = Path(sys.argv[1]).resolve()
browser = json.loads((folder / "browser-report.json").read_text(encoding="utf-8"))
results = {}
for name, sample in browser["Samples"].items():
    if sample["ProductionPdfPages"] is None:
        results[name] = {"rendererRejected": True, "reason": sample["RendererRejection"], "layoutIssue": sample["Editor"]["layoutIssue"]}
        continue
    reader = PdfReader(folder / f"{name}.pdf")
    pdf_markers = {}
    page_texts = []
    for index, page in enumerate(reader.pages, start=1):
        text = page.extract_text() or ""
        page_texts.append(text)
        for marker in re.findall(r"MARK_\d{3}|LONG_PARA_START|LONG_PARA_END|TABLE_END_MARKER", text):
            if marker in pdf_markers:
                raise ValueError(f"Duplicate PDF marker: {name}: {marker}")
            pdf_markers[marker] = index
    editor_markers = sample["Editor"]["markers"]
    if set(pdf_markers) != set(editor_markers):
        raise ValueError(f"Marker loss: {name}: editor={set(editor_markers)} pdf={set(pdf_markers)}")
    differences = [{"marker": marker, "editorPage": editor_markers[marker], "pdfPage": pdf_markers[marker]}
                   for marker in sorted(editor_markers) if editor_markers[marker] != pdf_markers[marker]]
    results[name] = {"markers": len(pdf_markers), "editorPages": sample["Editor"]["pages"], "pdfPages": len(reader.pages),
                     "pageCountsMatch": sample["Editor"]["pages"] == len(reader.pages), "differentMarkerPages": differences,
                     "markerPagesMatch": not differences if editor_markers else None, "hasPageMarkerEvidence": bool(editor_markers),
                     "editorTableHeaders": sample["Editor"].get("tables", []),
                     "tableHeaderFragmentCountsMatch": all(table["repetitionMatches"] for table in sample["Editor"].get("tables", [])),
                     "allLineBoundariesProven": False}
    if name in ["long-table", "long-table-multiline", "table-no-header", "table-mixed-header"]:
        heading = "MULTILINE_HEADER" if name == "long-table-multiline" else "Sıra"
        occurrences = [text.count(heading) for text in page_texts]
        headed = name in ["long-table", "long-table-multiline"]
        results[name]["pdfHeadingOccurrencesByPage"] = occurrences
        results[name]["headingRepetitionVerified"] = (all(count == 1 for count in occurrences) if headed
            else sum(occurrences) == 1 and all(not table["hasHeader"] and table["headerFragments"] == 0
                                            for table in sample["Editor"]["tables"]))
required = ["long-table", "long-table-multiline", "table-no-header", "table-mixed-header"]
header_acceptance = all(name in results and results[name]["headingRepetitionVerified"] and
                       results[name]["tableHeaderFragmentCountsMatch"] for name in required)
header_acceptance = header_acceptance and all(results[name]["pageCountsMatch"] and results[name]["markerPagesMatch"]
                                            for name in ["long-table", "long-table-multiline"])
report = {"samples": results, "tableHeaderFixAcceptancePassed": header_acceptance,
          "knownPageBoundaryDifferences": [name for name, result in results.items() if result.get("pageCountsMatch") is False or result.get("markerPagesMatch") is False],
          "productionApproved": False, "wordImportTested": False,
          "scope": "One pinned Chromium build and synthetic inputs only; inspect PNG/PDF visual results and table header repetition separately."}
(folder / "pdf-comparison.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))
if not header_acceptance:
    raise SystemExit("Table heading acceptance failed; inspect the comparison report. No production approval.")

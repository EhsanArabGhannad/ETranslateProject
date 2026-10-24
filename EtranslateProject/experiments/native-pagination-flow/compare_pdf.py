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
    for index, page in enumerate(reader.pages, start=1):
        for marker in re.findall(r"MARK_\d{3}|LONG_PARA_START|LONG_PARA_END|TABLE_END_MARKER", page.extract_text() or ""):
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
                     "knownVisualDifferences": ["Table heading repeats in the PDF but not in the editor after the first table page."] if name == "long-table" else [],
                     "allLineBoundariesProven": False}
report = {"samples": results, "productionApproved": False, "wordImportTested": False,
          "scope": "One pinned Chromium build and synthetic inputs only; inspect PNG/PDF visual results and table header repetition separately."}
(folder / "pdf-comparison.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))

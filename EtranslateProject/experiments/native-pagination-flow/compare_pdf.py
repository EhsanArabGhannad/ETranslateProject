"""Synthetic marker/page evidence, not production approval or a pixel-equivalence assertion."""
import json
import re
import sys
from pathlib import Path
from pypdf import PdfReader

folder = Path(sys.argv[1]).resolve()
browser = json.loads((folder / "browser-report.json").read_text(encoding="utf-8"))
results = {}
pdf_marker_sets = {}
def evidence(path):
    reader = PdfReader(path)
    texts = [page.extract_text() or "" for page in reader.pages]
    markers = {}
    for index, text in enumerate(texts, 1):
        for marker in re.findall(r"MARK_\d{3}|LONG_PARA_START|LONG_PARA_END|TABLE_END_MARKER", text):
            if marker in markers:
                raise ValueError(f"Duplicate marker in {path.name}: {marker}")
            markers[marker] = index
    return texts, markers
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
    pdf_marker_sets[name] = pdf_markers
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
probe = json.loads((folder / "unheaded-flow-probe.json").read_text(encoding="utf-8"))
flow_results = {}
for name in ["table-no-header", "table-mixed-header"]:
    sample = probe[name]
    baseline = sample["Variants"]["baseline"]
    measured = sample["PrintMetrics"]["plain"]["rows"]
    geometry_matches = all(abs(a["height"] - b["height"]) < .1 and abs(a["width"] - b["width"]) < .1
                           for a, b in zip(baseline["Metrics"]["rows"], measured, strict=True))
    geometry_matches = geometry_matches and all(abs(a["width"] - b["width"]) < .1 and a["font"] == b["font"]
        for a, b in zip(baseline["Metrics"]["firstCells"], sample["PrintMetrics"]["plain"]["firstCells"], strict=True))
    controls = {}
    for variant in ["print-control", "cell-avoid", "row-groups"]:
        reader = PdfReader(folder / f"{name}-{variant}.pdf")
        markers = {}
        for index, page in enumerate(reader.pages, start=1):
            for marker in re.findall(r"MARK_\d{3}|TABLE_END_MARKER", page.extract_text() or ""):
                if marker in markers:
                    raise ValueError(f"Duplicate control marker: {name}: {variant}: {marker}")
                markers[marker] = index
        if set(markers) != set(pdf_marker_sets[name]):
            raise ValueError(f"Marker loss in print control: {name}: {variant}")
        if len(reader.pages) != sample["PrintControls"][variant]["Pages"]:
            raise ValueError(f"Print control page count changed: {name}: {variant}")
        editor = baseline["Report"]
        controls[variant] = {"pages": len(reader.pages), "markers": len(markers),
            "matchesCurrentRenderer": markers == pdf_marker_sets[name] and len(reader.pages) == results[name]["pdfPages"],
            "matchesEditor": markers == editor["markers"] and len(reader.pages) == editor["pages"],
            "css": sample["PrintControls"][variant]["Css"]}
    # The old raw-HTML control intentionally has no row plan; it is now a v10 reference,
    # not the v11 product renderer. Compare it to the independently generated raw baseline.
    legacy_name = 'unheaded' if name == 'table-no-header' else 'mixed'
    legacy_texts, legacy_markers = evidence(folder / f"row-plan-{legacy_name}-baseline.pdf")
    control_texts, control_markers = evidence(folder / f"{name}-print-control.pdf")
    if not geometry_matches or control_markers != legacy_markers or len(control_texts) != len(legacy_texts):
        raise ValueError(f"The independent legacy control is not reproducible: {name}")
    flow_results[name] = {"unfragmentedGeometryMatches": geometry_matches,
        "printHtmlInColumns": sample["PrintMetrics"]["withoutColgroup"], "printControls": controls,
        "resolved": controls["print-control"]["matchesEditor"]}
toolbar = browser["DirectTableToolbar"]
if {entry["Sample"] for entry in toolbar} != {"long-table", "table-no-header", "table-mixed-header"} or not all(
        entry["ExactTargetAndUndoRedo"] and entry["FirstRowDeleteUndo"] and entry["ReadonlyDisabled"] and
        entry["RepeatsPerCommand"] == 2 and entry["Commands"] == 5 for entry in toolbar):
    raise ValueError("Direct toolbar evidence is missing or incomplete.")
planned_results = {}
for name, sample in browser["Samples"].items():
    if name == "tall-cell":
        if sample["PlannedPdfPages"] is not None or not sample["PlannedRejection"]:
            raise ValueError("The planned renderer did not reject an oversized row.")
        continue
    texts, markers = evidence(folder / f"{name}-planned.pdf")
    editor = sample["Editor"]
    if set(markers) != set(editor["markers"]):
        raise ValueError(f"Planned PDF marker loss: {name}")
    headings = []
    for table in editor["tables"]:
        token = table["headerVerificationToken"]
        if token and table["headerVerificationOccurrences"] == 1:
            actual = [text.count(token) for text in texts]
            expected = [int(index in table["tablePages"]) for index in range(1, len(texts) + 1)]
            headings.append({"token": token, "occurrencesByPage": actual, "matches": actual == expected})
    footer_matches = True
    if name in ["letterhead-last", "table-letterhead-last"]:
        footers = [text.count("Yalnızca son sayfa - SENTETİK ALTBİLGİ") for text in texts]
        headers = [text.count("SENTETİK TERCÜME BÜROSU") for text in texts]
        footer_matches = footers == [0] * (len(texts) - 1) + [1] and headers == [1] * len(texts)
    differences = [{"marker": marker, "editorPage": editor["markers"][marker], "pdfPage": markers[marker]}
                   for marker in sorted(markers) if editor["markers"][marker] != markers[marker]]
    planned_results[name] = {"editorPages": editor["pages"], "pdfPages": len(texts), "markers": len(markers),
        "pageCountsMatch": len(texts) == editor["pages"] == sample["PlannedPdfPages"], "differentMarkerPages": differences,
        "headingChecks": headings, "headingRepetitionMatches": all(heading["matches"] for heading in headings),
        "templateFooterPolicyMatches": footer_matches, "allLineBoundariesProven": False}
expected_planned = {"text", "letterhead-last", "landscape", "simple-table", "long-table", "long-table-multiline",
    "table-no-header", "table-mixed-header", "table-varied", "table-after-text", "table-after-text-header", "table-multiple",
    "table-landscape", "table-letterhead-last", "long-paragraph"}
planned_acceptance = set(planned_results) == expected_planned and all(result["pageCountsMatch"] and not result["differentMarkerPages"]
    and result["headingRepetitionMatches"] and result["templateFooterPolicyMatches"] for result in planned_results.values())
product_acceptance = all(result.get('pageCountsMatch') is True and not result.get('differentMarkerPages') for name,result in results.items() if name != 'tall-cell')
raw = json.loads((folder / "row-plan-probe.json").read_text(encoding="utf-8"))
raw_results = {}
for name in ["unheaded", "mixed", "headed"]:
    variant = raw[name]["row-plan"]
    texts, markers = evidence(folder / f"row-plan-{name}-row-plan.pdf")
    expected = {row["text"]: row["page"] for row in variant["Plan"]["rows"] if row["text"].startswith("MARK_")}
    expected["TABLE_END_MARKER"] = variant["Plan"]["pages"]
    matched = len(texts) == variant["Plan"]["pages"] == variant["PdfPages"] and markers == expected and len(markers) == 47
    raw_results[name] = {"nativePages": variant["Plan"]["pages"], "plannedPdfPages": len(texts),
        "baselinePdfPages": raw[name]["baseline"]["PdfPages"], "markers": len(markers), "matches": matched}
    planned_acceptance = planned_acceptance and matched
report = {"samples": results, "tableHeaderFixAcceptancePassed": header_acceptance,
          "tableFlowInvestigation": flow_results, "directTableToolbarVerified": True,
          "plannedPdfResults": planned_results, "rawRowPlanControls": raw_results, "candidatePdfAcceptancePassed": planned_acceptance,
          "candidateRendererVersion": "native-table-row-plan-v1 (EXPERIMENT ONLY)",
          "productionRendererParityPassed": product_acceptance,
          "knownPageBoundaryDifferences": [name for name, result in results.items() if result.get("pageCountsMatch") is False or result.get("markerPagesMatch") is False],
          "productionApproved": False, "wordImportTested": False,
          "scope": "One pinned Chromium build and synthetic inputs only; inspect PNG/PDF visual results and table header repetition separately."}
(folder / "pdf-comparison.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"candidatePdfAcceptancePassed": planned_acceptance, "plannedSamples": len(planned_results),
    "rawRowPlanControls": raw_results, "productionRendererParityPassed": product_acceptance,
    "currentRendererDifferences": report["knownPageBoundaryDifferences"],
    "productionApproved": False, "report": str(folder / "pdf-comparison.json")}, ensure_ascii=False, indent=2))
if not header_acceptance:
    raise SystemExit("Table heading acceptance failed; inspect the comparison report. No production approval.")
if not planned_acceptance:
    raise SystemExit("Candidate PDF parity failed; inspect plannedPdfResults. No production approval.")
if not product_acceptance:
    raise SystemExit("Product renderer parity failed; no deployment approval.")

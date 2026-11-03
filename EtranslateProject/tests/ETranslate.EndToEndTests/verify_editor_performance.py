"""Local performance acceptance and marker-page parity; never a production capacity estimate."""
import json
from pathlib import Path
import re
import sys

from pypdf import PdfReader
from pypdf.generic import ContentStream

output = Path(sys.argv[1])
names = ["long-text", "wide-table", "mixed-images", "fifty-pages"]
pattern = re.compile(r"PERF_(?:START|TAIL|TEXT_\d{3}|CELL_\d{2}_\d{2}_\d{2}|MIX_\d{2}_\d{2}|PAGE_\d{3})")


def texts(node):
    if node.get("type") == "text":
        yield node["text"]
    for child in node.get("content", []):
        yield from texts(child)


def image_draws(stream, resources, pdf, depth=0):
    assert depth < 32
    count = 0
    if stream is None:
        return count
    for operands, operator in ContentStream(stream, pdf).operations:
        if operator != b"Do":
            continue
        obj = resources["/XObject"][operands[0]].get_object()
        if obj.get("/Subtype") == "/Image":
            count += 1
        elif obj.get("/Subtype") == "/Form":
            count += image_draws(obj, obj.get("/Resources", resources), pdf, depth + 1)
    return count


def compare(directory, name, expected):
    view = json.loads((directory / f"{name}-markers.json").read_text(encoding="utf-8"))
    pdf = PdfReader(directory / f"{name}.pdf")
    actual = {}
    images = 0
    for number, page in enumerate(pdf.pages, 1):
        compact = re.sub(r"\s+", "", page.extract_text())
        assert "DRAFT" in compact and "UNSIGNED" in compact
        assert "PERFORMANCEQALETTERHEAD" in compact
        assert ("PERFORMANCEQALASTFOOTER" in compact) == (number == len(pdf.pages))
        for match in pattern.findall(compact):
            actual.setdefault(match, number)  # The first row's heading markers repeat on later pages.
        images += image_draws(page.get_contents(), page["/Resources"], pdf)
    assert set(view["markers"]) == expected, f"{name}: incomplete editor marker map"
    assert set(actual) == expected, f"{name}: incomplete PDF marker map: {sorted(expected - set(actual))[:8]}"
    assert actual == view["markers"], f"{name}: PDF and editor marker pages differ"
    assert len(pdf.pages) == view["pageCount"]
    assert images == (10 if name == "mixed-images" else 0), f"{name}: body-image draw count differs: {images}"
    return {"name": name, "pages": len(pdf.pages), "markers": len(expected), "imageDraws": images, "passed": True}


reports = []
pdfs = []
expected_by_name = {}
for name in names:
    source = json.loads((output / f"{name}-body.json").read_text(encoding="utf-8"))
    expected = set(pattern.findall(" ".join(texts(source))))
    expected_by_name[name] = expected
    report = json.loads((output / f"{name}-report.json").read_text(encoding="utf-8"))
    assert report["passed"] and not report["baseline"] and not report["errors"] and not report["unexpected"]
    assert report["caretMeasures"] == report["zoomMeasures"] == 0 and report["editMeasures"] > 0
    assert report["inputSamples"] == 19 and report["inputP95Ms"] < 750
    assert report["readyMs"] < 15000 and report["enableMs"] < 15000
    reports.append(report)
    pdfs.append(compare(output, name, expected))

product_report = None
if len(sys.argv) > 2 and sys.argv[2] != "-":
    product = Path(sys.argv[2])
    product_report = json.loads((product / "performance-report.json").read_text(encoding="utf-8"))
    cleanup = json.loads((product / "cleanup.json").read_text(encoding="utf-8"))
    assert product_report["runId"] == cleanup["runId"] == product.name
    assert len(product_report["cases"]) == 3 and all(case["passed"] for case in product_report["cases"])
    assert not product_report["errors"] and not product_report["external"] and not product_report["csp"]
    assert cleanup["cleanupSucceeded"] and cleanup["databasesCreated"] == 6 and not cleanup["existingDatabasesUsed"]
    for name in ["long-text", "mixed-images"]:
        pdfs.append({**compare(product, name, expected_by_name[name]), "realProduct": True})

before_after = []
if len(sys.argv) > 3:
    baseline = Path(sys.argv[3])
    for report in reports:
        name = report["name"]
        before = json.loads((baseline / f"{name}-report.json").read_text(encoding="utf-8"))
        assert before["baseline"] and before["passed"] and before["browserVersion"] == report["browserVersion"]
        assert before["characters"] == report["characters"] and before["pageCount"] == report["pageCount"]
        assert (baseline / f"{name}-body.json").read_bytes() == (output / f"{name}-body.json").read_bytes()
        before_after.append({"name": name, "beforeCaretMeasures": before["caretMeasures"], "afterCaretMeasures": report["caretMeasures"],
                             "beforeZoomMeasures": before["zoomMeasures"], "afterZoomMeasures": report["zoomMeasures"],
                             "beforeInputP95Ms": before["inputP95Ms"], "afterInputP95Ms": report["inputP95Ms"]})

result = {"passed": True, "scope": "one local Windows browser run, input capture to second animation-frame callback; no multiuser capacity claim",
          "browserReports": reports, "pdfs": pdfs, "beforeAfter": before_after,
          "productCases": product_report["cases"] if product_report else []}
(output / "comparison.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "pdfs": len(pdfs), "pages": sum(pdf["pages"] for pdf in pdfs), "markerChecks": sum(pdf["markers"] for pdf in pdfs),
                  "inputP95Ms": {r["name"]: round(r["inputP95Ms"], 1) for r in reports}, "beforeAfterCases": len(before_after)}))

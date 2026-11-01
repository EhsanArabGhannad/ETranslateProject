"""Compare the browser matrix and real SQL product PDFs using synthetic markers only."""
import json
from pathlib import Path
import re
import sys

from pypdf import PdfReader

matrix = Path(sys.argv[1])
names = ["chrome-desktop", "edge-desktop", "chrome-narrow-touch", "chrome-phone-touch",
         "edge-phone-touch", "chrome-landscape-touch", "chrome-tablet-touch", "edge-short-window"]
expected = {f"MARK_{i:03}" for i in range(1, 47)} | {"COMPAT_START", "COMPAT_LAST", "END_MARKER"}
reports = [json.loads((matrix / f"{name}.json").read_text(encoding="utf-8")) for name in names]
canonical = reports[0]
for report in reports:
    assert report["passed"] and not report["errors"] and not report["unexpected"]
    assert report["pageCount"] == canonical["pageCount"]
    assert set(report["markerPages"]) == expected
    assert report["markerPages"] == canonical["markerPages"], f"Browser marker pages differ: {report['name']}"

pdfs = []
if len(sys.argv) > 2:
    product = Path(sys.argv[2])
    run = json.loads((product / "compatibility-report.json").read_text(encoding="utf-8"))
    cleanup = json.loads((product / "cleanup.json").read_text(encoding="utf-8"))
    assert run["runId"] == cleanup["runId"] == product.name
    assert len(run["cases"]) == 3 and all(case["passed"] for case in run["cases"])
    assert not run["errors"] and not run["external"] and not run["csp"]
    assert cleanup["cleanupSucceeded"] and cleanup["databasesCreated"] == 6 and not cleanup["existingDatabasesUsed"]
    for name in ["chrome-narrow", "chrome-landscape", "edge-desktop"]:
        view = json.loads((product / f"{name}-markers.json").read_text(encoding="utf-8"))
        pdf = PdfReader(product / f"{name}.pdf")
        actual = {}
        texts = []
        for number, page in enumerate(pdf.pages, 1):
            text = page.extract_text()
            texts.append(text)
            assert "DRAFT" in text and "UNSIGNED" in text
            assert "SENTETİK ÜSTBİLGİ" in text
            assert ("SON SAYFA ALTBİLGİ" in text) == (number == len(pdf.pages))
            for marker in re.findall(r"COMPAT_START|COMPAT_LAST|MARK_\d{3}|END_MARKER", text):
                assert marker not in actual, f"Duplicate PDF marker: {name}, {marker}"
                actual[marker] = number
        assert len(pdf.pages) == view["pageCount"] == canonical["pageCount"]
        assert set(actual) == expected
        assert actual == view["markers"] == canonical["markerPages"], f"PDF/editor/browser pages differ: {name}"
        assert sum(len(page.images) for page in pdf.pages) == 1
        assert "PRODUCT_EDIT" in "\n".join(texts)
        pdfs.append({"name": name, "pages": len(pdf.pages), "markers": len(actual), "passed": True})

result = {"passed": True, "browserCases": len(reports), "markerPages": len(expected),
          "pagesPerDocument": canonical["pageCount"], "productPdfs": pdfs,
          "realDevicesUsed": False, "independentEngineFamilies": 1}
(matrix / "comparison.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result))

"""Compare private body-image pages, alignment, aspect and actual PDF image bounds."""
import json
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader

root = Path(sys.argv[1])
cases = []
for name in ("text-only", "with-table"):
    view = json.loads((root / f"{name}.json").read_text(encoding="utf-8"))
    pdf = PdfReader(root / f"{name}.pdf")
    markers = {}
    for number, page in enumerate(pdf.pages, 1):
        for marker in re.findall(r"BI_\d{3}", page.extract_text()):
            assert marker not in markers, (name, "duplicate", marker)
            markers[marker] = number
    assert set(markers) == {f"BI_{i:03}" for i in range(30)}
    assert markers == view["markers"] and len(pdf.pages) == view["pageCount"]
    with pdfplumber.open(root / f"{name}.pdf") as document:
        images = [(page_number, image) for page_number, page in enumerate(document.pages, 1) for image in page.images]
        assert len(images) == len(view["images"]) == 3
        geometry = []
        for index, ((number, printed), live) in enumerate(zip(images, view["images"])):
            assert number == live["page"], (name, index, "image page")
            assert abs(100 * live["width"] / view["contentWidth"] - (25, 50, 75)[index]) < .02
            assert abs(live["width"] / live["height"] - 2) < .002
            assert abs(printed["width"] / printed["height"] - 2) < .02  # Chromium pixel quantization.
            assert tuple(printed["srcsize"]) == (120, 60)
            for key in ("width", "height"):
                assert abs(printed[key] - live[key] * .75) < 1.1, (name, index, key, printed[key], live[key])
            # Default page: 20mm left, 25mm top. Chromium rounds print margins to CSS pixels.
            assert abs(printed["x0"] - (20 * 72 / 25.4 + live["x"] * .75)) < 1.6
            assert abs(printed["top"] - (25 * 72 / 25.4 + live["y"] * .75)) < 1.6
            geometry.append({"page": number, "widthPercent": (25, 50, 75)[index], "align": ("left", "center", "right")[index], "pdfBoundsPt": [printed["x0"], printed["top"], printed["width"], printed["height"]]})
    cases.append({"name": name, "pages": len(pdf.pages), "markers": len(markers), "images": geometry, "passed": True})
report = {"passed": True, "cases": cases, "scope": "synthetic private managed images; source pixels, native marker pages and PDF image bounds"}
(root / "image-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))

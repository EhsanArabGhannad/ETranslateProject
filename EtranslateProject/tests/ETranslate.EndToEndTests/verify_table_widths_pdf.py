"""Compare marker pages and actual PDF border geometry for percentage columns."""
import json
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader

directory = Path(sys.argv[1])
view = json.loads((directory / "width-markers.json").read_text(encoding="utf-8"))
path = directory / "column-widths.pdf"
pdf = PdfReader(path)
occurrences = {}
for number, page in enumerate(pdf.pages, 1):
    for marker in re.findall(r"CW_[AB]_\d{3}", page.extract_text()):
        occurrences.setdefault(marker, []).append(number)
expected = {f"CW_{table}_{i:03}" for table in "AB" for i in range(35)}
assert set(occurrences) == set(view["markers"]) == expected
assert len(pdf.pages) == view["pageCount"]
assert all(occurrences[key][0] == view["markers"][key] for key in expected)
for marker, pages in occurrences.items():
    if marker != "CW_A_000": assert len(pages) == 1, (marker, pages)
occupied = sorted({page for key, pages in occurrences.items() if key.startswith("CW_A_") and key != "CW_A_000" for page in pages})
assert len(occupied) > 1 and occurrences["CW_A_000"] == occupied
geometry = []
with pdfplumber.open(path) as document:
    for marker, widths in [("CW_A_005", [50, 30, 20]), ("CW_B_005", [35, 65])]:
        page = document.pages[occurrences[marker][0] - 1]
        word = next(word for word in page.extract_words() if word["text"].startswith(marker))
        y = (word["top"] + word["bottom"]) / 2
        xs = sorted(edge["x0"] for edge in page.edges if edge["orientation"] == "v" and edge["top"] <= y <= edge["bottom"])
        clusters = []
        for x in xs:
            if clusters and x - clusters[-1][-1] < 1.1: clusters[-1].append(x)
            else: clusters.append([x])
        boundaries = [sum(group) / len(group) for group in clusters]
        assert len(boundaries) == len(widths) + 1, (marker, boundaries)
        actual = [100 * (b - a) / (boundaries[-1] - boundaries[0]) for a, b in zip(boundaries, boundaries[1:])]
        assert all(abs(a - b) < .6 for a, b in zip(actual, widths)), (marker, actual, widths)
        geometry.append({"marker": marker, "physicalLeftToRightPercentages": actual, "expected": widths})
report = {"passed": True, "pages": len(pdf.pages), "markers": len(expected), "headingPages": occupied,
          "geometry": geometry, "scope": "synthetic saved v6 tables; actual PDF borders and editor marker pages"}
(directory / "width-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))

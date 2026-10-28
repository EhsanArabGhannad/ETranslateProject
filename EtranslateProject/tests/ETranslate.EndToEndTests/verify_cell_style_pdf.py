"""Verify cell paint, repeated heading paint, borders and hidden edges in the saved PDF."""
import json
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader

directory = Path(sys.argv[1])
view = json.loads((directory / "cell-markers.json").read_text(encoding="utf-8"))
path = directory / "cell-style.pdf"
pdf = PdfReader(path)
seen = {}
for number, page in enumerate(pdf.pages, 1):
    for marker in re.findall(r"CW_[AB]_\d{3}|CS_HIDE", page.extract_text()): seen.setdefault(marker, []).append(number)
expected = {f"CW_{t}_{r:03}" for t in "AB" for r in range(35)} | {"CS_HIDE"}
assert set(seen) == set(view["markers"]) == expected
assert len(pdf.pages) == view["pageCount"]
assert all(seen[key][0] == view["markers"][key] for key in expected)
for marker, pages in seen.items():
    if marker != "CW_A_000": assert len(pages) == 1, (marker, pages)
header_pages = sorted({p for key, pages in seen.items() if key.startswith("CW_A_") and key != "CW_A_000" for p in pages})
assert seen["CW_A_000"] == header_pages and len(header_pages) > 1
def color_matches(value, color):
    return isinstance(value, (tuple, list)) and len(value) == 3 and all(abs(a-b/255) < .012 for a,b in zip(value, color))
with pdfplumber.open(path) as document:
    def word_on(marker, page_number): return next(w for w in document.pages[page_number-1].extract_words() if w["text"].startswith(marker))
    for marker, color in [("CW_A_005", (191,219,254)), ("CW_B_005", (187,247,208))] + [("CW_A_000", (254,240,138))]:
        for number in seen[marker]:
            page = document.pages[number-1]; word = word_on(marker, number); x=(word["x0"]+word["x1"])/2; y=(word["top"]+word["bottom"])/2
            assert any(r["x0"] <= x <= r["x1"] and r["top"] <= y <= r["bottom"] and color_matches(r.get("non_stroking_color"), color) for r in page.rects), (marker, number, "background paint")
    borders = []
    for index, (color, nominal, pattern) in enumerate([((185,28,28),1.5,"dashed"), ((29,78,216),2,"dotted")]):
        metrics = view["borders"][index]
        assert metrics["nominalPt"] == nominal and metrics["style"] == pattern
        width = metrics["computedPt"]
        dimensions = []
        for page in document.pages:
            for shape in page.rects + page.curves:
                if color_matches(shape.get("non_stroking_color"),color):
                    dimensions.append(min(shape["width"],shape["height"]))
        assert any(abs(measured-width) < .2 for measured in dimensions), (color, width, dimensions[:10])
        borders.append({"rgb":color,"nominalPt":nominal,"computedEditorAndPdfPt":width,"style":pattern,"verified":True})
    number = seen["CS_HIDE"][0]; page=document.pages[number-1]; word=word_on("CS_HIDE",number); y=(word["top"]+word["bottom"])/2
    assert not [e for e in page.edges if e["orientation"]=="v" and e["top"]<=y<=e["bottom"]], "Zero-width cell must have no vertical borders"
report={"passed":True,"pages":len(pdf.pages),"markers":len(expected),"headerPages":header_pages,"fillsVerified":True,"borders":borders,"hiddenEdgesVerified":True}
(directory/"cell-style-verification.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
print(json.dumps(report))

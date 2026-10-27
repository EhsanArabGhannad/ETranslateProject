"""Inspect physical paragraph layout, not just the saved JSON or equal page counts."""
import json
import logging
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader

logging.getLogger("pdfminer.pdffont").setLevel(logging.ERROR)
directory=Path(sys.argv[1])
path=directory/"paragraph-layout.pdf"
view=json.loads((directory/"paragraph-markers.json").read_text(encoding="utf-8"))
reader=PdfReader(path)
markers={}
for number,page in enumerate(reader.pages,1):
    for marker in re.findall(r"RTL_MARK|LTR_MARK|CENTER_MARK|JUSTIFY_MARK",page.extract_text()):
        assert marker not in markers, ("duplicate marker",marker)
        markers[marker]=number
assert markers==view["markers"] and len(markers)==4
assert len(reader.pages)==view["pageCount"]

def colored(chars,rgb):
    return [char for char in chars if isinstance(char.get("non_stroking_color"),(list,tuple)) and len(char["non_stroking_color"])==3
            and all(abs(float(a)-b/255)<.003 for a,b in zip(char["non_stroking_color"],rgb))]

def spacing(chars,expected):
    values=sorted({round(char["matrix"][5],2) for char in chars})
    differences=[b-a for a,b in zip(values,values[1:])]
    assert sum(abs(step-expected)<.8 for step in differences)>=2, ("line spacing",expected,differences)

with pdfplumber.open(path) as pdf:
    assert len(pdf.pages)==1, "The bounded geometry fixture should remain on one A4 sheet"
    chars=pdf.pages[0].chars
    rtl=colored(chars,(29,78,216));ltr=colored(chars,(21,128,61));center=colored(chars,(126,34,206));justify=colored(chars,(185,28,28))
    assert rtl and ltr and center and justify
    mm=72/25.4
    # 20mm paper margins; an independent 10mm start indent on each writing direction.
    assert abs(max(char["x1"] for char in rtl)-180*mm)<1.2, "RTL start indent should reduce the right bound"
    assert abs(min(char["x0"] for char in ltr)-30*mm)<1.2, "LTR start indent should move the left bound"
    assert abs((min(char["x0"] for char in center)+max(char["x1"] for char in center))/2-105*mm)<1.2, "Heading should be centered"
    spacing([char for char in rtl if "NotoSansArabic" in char["fontname"]],22)
    spacing(ltr,16.5)
    spacing(justify,27.5)
    assert abs(min(char["x0"] for char in justify)-25*mm)<1.2
    assert abs(max(char["x1"] for char in justify)-190*mm)<3, "Justified lines should approach both content edges"
report={"passed":True,"pages":len(reader.pages),"markers":len(markers),"rtlStartIndentMm":10,"ltrStartIndentMm":10,
        "lineHeights":{"rtl":2,"ltr":1.5,"justify":2.5},"centeredHeading":True,"scope":"synthetic colored paragraphs; point tolerances allow Chromium rounding and glyph overhang"}
(directory/"paragraph-verification.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
print(json.dumps(report))

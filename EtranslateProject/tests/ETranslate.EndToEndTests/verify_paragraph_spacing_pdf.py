"""Check physical before/after spacing, logical first-line indents and all marker pages."""
import json
import logging
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader

logging.getLogger("pdfminer.pdffont").setLevel(logging.ERROR)

def colored(char,rgb):
    value=char.get("non_stroking_color")
    return isinstance(value,(list,tuple)) and len(value)==3 and all(abs(float(a)-b/255)<.003 for a,b in zip(value,rgb))

def lines(chars):
    # Arabic fallback and Latin glyphs can have slightly different baseline matrices on one line.
    groups=[]
    for char in sorted(chars,key=lambda c:-c["matrix"][5]):
        if not groups or groups[-1][0]["matrix"][5]-char["matrix"][5]>1.1:groups.append([])
        groups[-1].append(char)
    return groups

directory=Path(sys.argv[1]);path=directory/"paragraph-spacing.pdf"
view=json.loads((directory/"spacing-markers.json").read_text(encoding="utf-8"));reader=PdfReader(path);markers={}
for number,page in enumerate(reader.pages,1):
    for marker in re.findall(r"START_MARK|AFTER_MARK|LTR_FIRST|RTL_FIRST|TABLE_SPACING|FLOW_\d{3}",page.extract_text()):
        assert marker not in markers,("duplicate marker",marker);markers[marker]=number
assert len(markers)==45 and markers==view["markers"]
assert len(reader.pages)==view["pageCount"] and len(reader.pages)>1
with pdfplumber.open(path) as pdf:
    page=pdf.pages[0];mm=72/25.4
    green=[char for char in page.chars if colored(char,(21,128,61))]
    red=lines([char for char in page.chars if colored(char,(185,28,28))])
    teal=[char for char in page.chars if colored(char,(15,118,110))]
    blue=lines([char for char in page.chars if colored(char,(29,78,216))])
    purple=lines([char for char in page.chars if colored(char,(126,34,206))])
    assert green and teal and len(red)>=3 and len(blue)>=3 and len(purple)>=2
    start=green[0]["matrix"][5];red_first=red[0][0]["matrix"][5];red_last=red[-1][0]["matrix"][5];after=teal[0]["matrix"][5]
    assert abs(start-red_first-16.5-12)<.5,"Expected 12pt space before the Latin paragraph"
    assert abs(red_last-after-16.5-18)<.5,"Expected 18pt space after the Latin paragraph"
    first_left=min(char["x0"] for char in red[0]);later_left=min(char["x0"] for line in red[1:] for char in line)
    assert abs(first_left-later_left-12.5*mm)<.8,"LTR first line should move inward by 12.5mm"
    assert abs(later_left-30*mm)<1.2,"The rest of the LTR paragraph should retain its 10mm block indent"
    first_right=max(char["x1"] for char in blue[0]);later_right=max(char["x1"] for line in blue[1:] for char in line)
    assert abs(later_right-first_right-5*mm)<1.2,"RTL first line should move inward from the right by 5mm"
    assert abs(later_right-180*mm)<1.2,"The rest of the RTL paragraph should retain its 10mm block indent"
    assert abs(min(char["x0"] for char in purple[0])-min(char["x0"] for char in purple[1])-5*mm)<.8,"Cell first-line indent should remain independent"
    flow1=page.search("FLOW_001",regex=False)[0]["chars"][0]["matrix"][5]
    flow2=page.search("FLOW_002",regex=False)[0]["chars"][0]["matrix"][5]
    line_count=(flow1-flow2-12)/16.5
    assert 1<=round(line_count)<=6 and abs(line_count-round(line_count))<.04,"Adjacent 12pt-after/6pt-before margins should collapse to 12pt"
report={"passed":True,"pages":len(reader.pages),"markers":len(markers),"spaceBeforePt":12,"spaceAfterPt":18,
        "ltrFirstLineMm":12.5,"rtlFirstLineMm":5,"cellFirstLineMm":5,"collapsedAdjacentMargins":True}
(directory/"spacing-verification.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
print(json.dumps(report))

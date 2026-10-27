"""Check actual script size/baselines and colored strike vectors in synthetic PDFs."""
import json
import logging
from pathlib import Path
import re
from statistics import median
import sys
import pdfplumber
from pypdf import PdfReader

logging.getLogger("pdfminer.pdffont").setLevel(logging.ERROR)

def colored(item,rgb):
    value=item.get("non_stroking_color")
    return isinstance(value,(list,tuple)) and len(value)==3 and all(abs(float(a)-b/255)<.003 for a,b in zip(value,rgb))

def sized(chars,size):
    return [char for char in chars if abs(char["size"]-size)<.06]

def strike(page,chars,rgb):
    assert chars
    left=min(char["x0"] for char in chars);right=max(char["x1"] for char in chars)
    top=min(char["top"] for char in chars);bottom=max(char["bottom"] for char in chars)
    assert any(colored(edge,rgb) and abs(edge["top"]-edge["bottom"])<.05
               and edge["x0"]<=left+1 and edge["x1"]>=right-1 and top<edge["top"]<bottom
               for edge in page.edges), "Expected a same-color horizontal strike crossing the selected glyphs"

def assert_sql_script(path):
    found=0
    with pdfplumber.open(path) as pdf:
        for page in pdf.pages:
            red=[char for char in page.chars if colored(char,(185,28,28)) and "NotoSerif" in char["fontname"]]
            script=sized(red,13.5)
            if not script:continue
            baseline=median(char["matrix"][5] for char in sized(red,18))
            assert "".join(char["text"] for char in script)=="QA_EDIT"
            assert all(abs(char["matrix"][5]-baseline-6.75)<.5 for char in script)
            strike(page,script,(185,28,28));found+=len(script)
    assert found==7,"Expected the saved table suffix at 75% size above its 18pt baseline"
    return {"passed":True,"scriptSizePt":13.5,"baseSizePt":18,"superscriptCharacters":found,"coloredStrike":True}

def verify(directory):
    path=directory/"script-styles.pdf";view=json.loads((directory/"script-markers.json").read_text(encoding="utf-8"))
    reader=PdfReader(path);markers={}
    for number,page in enumerate(reader.pages,1):
        for marker in re.findall(r"SCRIPT_MARK|RTL_SCRIPT|CARET_MARK|TABLE_SCRIPT",page.extract_text()):
            assert marker not in markers;markers[marker]=number
    assert len(markers)==4 and markers==view["markers"] and len(reader.pages)==view["pageCount"]
    with pdfplumber.open(path) as pdf:
        assert len(pdf.pages)==1;page=pdf.pages[0]
        for rgb,family in [((185,28,28),"NotoSerif"),((29,78,216),"NotoSansArabic")]:
            chars=[char for char in page.chars if colored(char,rgb)]
            base=sized(chars,18);script=sized(chars,13.5);assert len(script)==2
            assert all(family in char["fontname"] for char in script)
            baseline=median(char["matrix"][5] for char in base)
            offsets=sorted(char["matrix"][5]-baseline for char in script)
            assert abs(offsets[0]+3.375)<.5 and abs(offsets[1]-6.75)<.5,offsets
        green=[char for char in page.chars if colored(char,(21,128,61))]
        assert "".join(char["text"] for char in green)=="OLD";strike(page,green,(21,128,61))
        table=sized([char for char in page.chars if colored(char,(126,34,206))],13.5)
        assert len(table)==1 and table[0]["text"]=="2";strike(page,table,(126,34,206))
    report={"passed":True,"pages":len(reader.pages),"markers":len(markers),"baseSizePt":18,"scriptSizePt":13.5,
            "latinAndPersianBaselines":True,"coloredStrike":True,"tableScriptStrike":True}
    (directory/"script-verification.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    return report

if __name__=="__main__":print(json.dumps(verify(Path(sys.argv[1]))))

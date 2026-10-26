"""Verify actual embedded font, point size, text color and highlight in generated PDF."""
import json
import logging
from pathlib import Path
import sys
import pdfplumber

logging.getLogger("pdfminer.pdffont").setLevel(logging.ERROR)

def assert_typography(path, require_arabic=False):
    def color(actual, expected):
        return isinstance(actual, (list,tuple)) and len(actual)==3 and all(abs(float(a)-b/255)<.003 for a,b in zip(actual,expected))
    with pdfplumber.open(path) as pdf:
        chars=[char for page in pdf.pages for char in page.chars]
        styled=[char for char in chars if "NotoSerif" in char["fontname"] and abs(char["size"]-18)<.06 and color(char.get("non_stroking_color"),(185,28,28))]
        assert len(styled)>=5, "Expected selected text using local Noto Serif, 18pt and the chosen red"
        highlights=[rect for page in pdf.pages for rect in page.rects if color(rect.get("non_stroking_color"),(254,240,138))]
        assert highlights, "Expected selected text's yellow background"
        if require_arabic:
            assert any("NotoSansArabic" in char["fontname"] and abs(char["size"]-12)<.06 and color(char.get("non_stroking_color"),(29,78,216)) for char in chars), "Expected the independent Persian selection in Arabic font, 12pt and blue"
        return {"font":"Noto Serif","sizePt":18,"color":"#b91c1c","highlight":"#fef08a","styledCharacters":len(styled),"rtlStyleVerified":require_arabic,"passed":True}

if __name__=="__main__":
    path=Path(sys.argv[1]);result=assert_typography(path,require_arabic=True)
    path.with_suffix(".verification.json").write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result))

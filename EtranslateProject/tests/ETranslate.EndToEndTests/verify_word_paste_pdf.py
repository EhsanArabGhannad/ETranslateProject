"""Compare synthetic Office clipboard -> saved app JSON -> actual PDF, not Word fidelity."""
import json
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader
from verify_typography_pdf import assert_typography

directory = Path(sys.argv[1])
view = json.loads((directory / 'word.json').read_text(encoding='utf-8'))
pdf = PdfReader(directory / 'word.pdf')
markers = {}
# Read positioned glyphs: pypdf inserts artificial spaces inside variable-serif words.
# Explicit marker alternatives also handle adjacent underlined and superscript runs.
pattern = r'WP_(?:STYLE|ITALIC|UNDERLINE|SUP|HEADING|LITERAL|BULLET|LIST_A|LIST_B|CELL_A|CELL_B|SPAN)|CLIP_BEFORE|CLIP_AFTER'
with pdfplumber.open(directory / 'word.pdf') as positioned:
    for number, page in enumerate(positioned.pages, 1):
        text = page.extract_text()
        for marker in re.findall(pattern, text):
            assert marker not in markers, ('duplicate', marker)
            markers[marker] = number
        assert 'DRAFT - UNSIGNED' in text
assert len(markers) == 14 and markers == view['markers'], (markers, view['markers'])
assert len(pdf.pages) == view['pageCount'] == 2
assert markers['WP_STYLE'] == 1 and markers['WP_HEADING'] == 2
paint = assert_typography(directory / 'word.pdf')
summary = {'passed': True, 'pages': len(pdf.pages), 'markers': len(markers), 'typography': paint,
           'scope': 'synthetic Office HTML, actual production paste/save/reload, native pages and PDF; not a real Word/OS corpus'}
(directory / 'verification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary))

"""Replay native Word captures: app/PDF parity, not a lossless Word rendering claim."""
import json
import logging
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader
logging.getLogger('pdfminer.pdffont').setLevel(logging.ERROR)

output = Path(sys.argv[1]); fixtures = Path(__file__).parent.parent / 'ETranslate.DomainTests/Fixtures/WordClipboard'
results = []
for name in ['typography', 'tables', 'unsupported']:
    sample = json.loads((fixtures / f'word15-{name}.json').read_text(encoding='utf-8'))
    view = json.loads((output / f'{name}.json').read_text(encoding='utf-8')); markers = {}; duplicates = {}
    with pdfplumber.open(output / f'{name}.pdf') as pdf:
        pattern = '|'.join(re.escape(m) for m in sorted(sample['markers'], key=len, reverse=True))
        for i, page in enumerate(pdf.pages, 1):
            # The diagonal draft watermark can interleave letters with a body word in
            # geometric extraction. Body markers are horizontal; exclude rotated glyphs.
            body = page.filter(lambda o: o['object_type'] != 'char' or (abs(o['matrix'][1]) < .01 and abs(o['matrix'][2]) < .01))
            for marker in re.findall(pattern, body.extract_text()):
                if marker in markers: duplicates.setdefault(marker, []).append(i)
                else: markers[marker] = i
            assert 'DRAFT - UNSIGNED' in page.extract_text()
        assert len(pdf.pages) == view['pageCount'], (name, 'page count')
        assert set(markers) == set(sample['markers']) == set(view['markers']), (name, 'marker set', markers)
        assert markers == view['markers'], (name, 'marker pages', markers, view['markers'])
        assert set(duplicates) <= {'RW_HEADER_A', 'RW_HEADER_B'}, (name, 'unexpected duplicate', duplicates)
        if name == 'tables': assert duplicates, 'Expected repeated native Word thead cells in PDF'
        if name == 'typography':
            assert markers['RW_PAGE_TWO'] > markers['RW_STYLE']
            chars = [c for p in pdf.pages for c in p.chars]
            assert any('NotoSerif' in c['fontname'] and abs(c['size'] - 18) < .06 for c in chars)
            assert any(tuple(r.get('non_stroking_color') or ()) == (1, 1, 0) for p in pdf.pages for r in p.rects)
        assert not any(page.images for page in PdfReader(output / f'{name}.pdf').pages)
        results.append({'name': name, 'pages': len(pdf.pages), 'markers': len(markers), 'passed': True})
summary = {'passed': True, 'cases': results, 'originalNativeCaptures': 3, 'wordGenerator': 'Microsoft Word 15',
           'scope': 'generated non-personal DOCX, original native trusted clipboard events; replayed import/save/native pages/PDF'}
(output / 'verification.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary))

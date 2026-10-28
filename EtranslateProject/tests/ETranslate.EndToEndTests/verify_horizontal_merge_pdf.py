"""Check horizontal spans, repeated headings, marker pages and actual merged-cell geometry."""
import json
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader

directory=Path(sys.argv[1]); view=json.loads((directory/'merge-markers.json').read_text(encoding='utf-8')); path=directory/'horizontal-merge.pdf'
pdf=PdfReader(path); occurrences={}
for number,page in enumerate(pdf.pages,1):
    for marker in re.findall(r'CW_[AB]_\d{3}|EQ_\d{3}',page.extract_text()): occurrences.setdefault(marker,[]).append(number)
expected={f'CW_{t}_{r:03}' for t in 'AB' for r in range(35)} | {f'EQ_{r:03}' for r in range(3)}
assert set(occurrences)==set(view['markers'])==expected
assert len(pdf.pages)==view['pageCount'] and all(occurrences[key][0]==view['markers'][key] for key in expected)
for marker,pages in occurrences.items():
    if marker not in ['CW_A_000','EQ_000']: assert len(pages)==1,(marker,pages)
heading_pages=sorted({page for key,pages in occurrences.items() if key.startswith('CW_A_') and key!='CW_A_000' for page in pages})
assert occurrences['CW_A_000']==heading_pages and len(heading_pages)>1
assert occurrences['EQ_000']==sorted({page for key,pages in occurrences.items() if key.startswith('EQ_') and key!='EQ_000' for page in pages})
geometry=[]
with pdfplumber.open(path) as document:
    for marker,widths in [('CW_A_010',[80,20]),('CW_A_020',[100]),('CW_B_017',[100]),('EQ_001',[100/3,200/3]),('EQ_002',[100/3,100/3,100/3])]:
        page=document.pages[occurrences[marker][0]-1];word=next(w for w in page.extract_words() if w['text'].startswith(marker));y=(word['top']+word['bottom'])/2
        xs=sorted(edge['x0'] for edge in page.edges if edge['orientation']=='v' and edge['top']<=y<=edge['bottom']);clusters=[]
        for x in xs:
            if clusters and x-clusters[-1][-1]<1.1:clusters[-1].append(x)
            else:clusters.append([x])
        boundaries=[sum(group)/len(group) for group in clusters];assert len(boundaries)==len(widths)+1,(marker,boundaries)
        actual=[100*(b-a)/(boundaries[-1]-boundaries[0]) for a,b in zip(boundaries,boundaries[1:])]
        assert all(abs(a-b)<.6 for a,b in zip(actual,widths)),(marker,actual,widths)
        geometry.append({'marker':marker,'physicalPercentages':actual,'expected':widths})
report={'passed':True,'pages':len(pdf.pages),'markers':len(expected),'headingPages':heading_pages,'geometry':geometry,'scope':'saved v8 horizontal spans; actual PDF borders and editor marker pages'}
(directory/'merge-verification.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(report))

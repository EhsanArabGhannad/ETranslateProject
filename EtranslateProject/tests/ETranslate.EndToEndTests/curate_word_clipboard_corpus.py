"""Retain only generated Word QA clipboard data, replacing machine-local file URLs."""
import hashlib
import json
from pathlib import Path
import re
import sys

source, target = map(Path, sys.argv[1:3]); target.mkdir(parents=True, exist_ok=True)
for name in ['typography', 'tables', 'unsupported']:
    capture = json.loads((source / (name + '.capture.json')).read_text(encoding='utf-8'))
    assert capture['trusted'] is True and 'RW_' in capture['text']
    html = capture['html']
    generator = re.search(r'<meta name=Generator content="([^"]+)"', html).group(1)
    curated, paths = re.subn(r'file:///[^\s"\'<>]+', lambda m: 'file:///word-qa/' + m.group().rsplit('/', 1)[-1], html, flags=re.I)
    assert '/Users/' not in curated
    markers = sorted(set(re.findall(r'RW_[A-Z]+(?:_[A-Z0-9]+)*', capture['text'])))
    if name == 'unsupported': markers.append('RW_IMAGE_ALT')
    result = {'name': name, 'provenance': {'source': 'Generated non-personal DOCX copied in native Word', 'generator': generator,
              'browser': capture['browser'], 'captureEventTrusted': True, 'clipboardTypes': capture['types'],
              'originalHtmlSha256': hashlib.sha256(html.encode()).hexdigest(), 'localFileUrlsReplaced': paths,
              'note': 'Replay uses synthetic clipboard events; trusted=true describes the original native capture only.'},
              'html': curated, 'text': capture['text'], 'markers': markers}
    (target / ('word15-' + name + '.json')).write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'curated': 3, 'personalDocumentsUsed': False}))

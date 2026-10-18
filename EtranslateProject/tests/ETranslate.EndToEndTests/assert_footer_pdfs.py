"""Semantic per-page checks for locally generated, synthetic renderer fixtures."""
import json
import re
import sys
from pathlib import Path
from pypdf import PdfReader

qa = Path(sys.argv[1])
cases = [
    ("template-presentation-qa.pdf", "AllPages", True, False),
    ("template-footer-last-qa.pdf", "LastPage", True, False),
    ("template-footer-single-qa.pdf", "LastPage", True, True),
    ("template-footer-only-qa.pdf", "LastPage", False, False),
    ("template-footer-empty-qa.pdf", "None", True, False),
]
checks = 0
counts = {}
for filename, policy, header, single in cases:
    reader = PdfReader(qa / filename)
    count = len(reader.pages)
    assert (count == 1 if single else count >= 2), (filename, count)
    checks += 1
    counts[filename] = count
    for index, page in enumerate(reader.pages, 1):
        text = page.extract_text() or ""
        expected = policy == "AllPages" or (policy == "LastPage" and index == count)
        assert text.count("Browser footer") == int(expected), (filename, index, "custom footer")
        assert ("Updated browser header" in text) == header, (filename, index, "header")
        assert re.search(r"DRAFT\s*-\s*UNSIGNED", text), (filename, index, "unsigned notice")
        assert re.search(rf"Page\s+{index}\s+of\s+{count}", text), (filename, index, "page count")
        checks += 4
assert counts[cases[0][0]] == counts[cases[1][0]], "Footer policy must not repaginate body text"
checks += 1
print(json.dumps({"checks": checks, "pages": counts}))

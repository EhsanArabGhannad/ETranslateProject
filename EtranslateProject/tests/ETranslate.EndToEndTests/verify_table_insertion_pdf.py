"""Compare UI-created tables, including heading repetition and an ordinary first row."""
import json
from pathlib import Path
import re
import sys
from pypdf import PdfReader

directory = Path(sys.argv[1])
view = json.loads((directory / "table-markers.json").read_text(encoding="utf-8"))
pdf = PdfReader(directory / "chosen-tables.pdf")
occurrences = {}
for number, page in enumerate(pdf.pages, 1):
    for marker in re.findall(r"CFG_[HN]_\d{3}", page.extract_text()):
        occurrences.setdefault(marker, []).append(number)
expected = {f"CFG_H_{i:03}" for i in range(36)} | {f"CFG_N_{i:03}" for i in range(32)}
assert set(view["markers"]) == expected and set(occurrences) == expected
assert len(pdf.pages) == view["pageCount"]
differences = {key: {"editor": view["markers"][key], "pdf": occurrences[key][0]}
               for key in sorted(expected) if view["markers"][key] != occurrences[key][0]}
assert not differences, differences
for marker, pages in occurrences.items():
    if marker != "CFG_H_000":
        assert len(pages) == 1, (marker, pages)
heading_pages = sorted({page for key, pages in occurrences.items() if key.startswith("CFG_H_") and key != "CFG_H_000" for page in pages})
assert len(heading_pages) > 1
assert occurrences["CFG_H_000"] == heading_pages, "Chosen heading must repeat on every table page"
report = {"passed": True, "pages": len(pdf.pages), "markers": len(expected), "differences": differences,
          "headingPages": heading_pages, "ordinaryFirstRowOccurrences": len(occurrences["CFG_N_000"]),
          "scope": "public table picker, production bundle and renderer; synthetic loopback only"}
(directory / "table-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))

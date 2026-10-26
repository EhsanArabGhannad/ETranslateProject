"""Compare actual product DOM Range pages with the new renderer's synthetic PDF."""
import json
from pathlib import Path
import re
import sys

from pypdf import PdfReader

output = Path(sys.argv[1])
view = json.loads((output / "product-pagination-markers.json").read_text(encoding="utf-8"))
pdf = PdfReader(output / "product-pagination-v11.pdf")
expected = {f"MARK_{i:03}" for i in range(1, 47)} | {"END_MARKER"}
actual = {}
for number, page in enumerate(pdf.pages, 1):
    for marker in re.findall(r"MARK_\d{3}|END_MARKER", page.extract_text()):
        if marker in actual:
            raise AssertionError(f"Duplicate PDF marker: {marker}")
        actual[marker] = number
assert set(view["markers"]) == expected, "Missing or unexpected editor markers"
assert set(actual) == expected, "Missing or unexpected PDF markers"
assert len(pdf.pages) == view["pageCount"], "Editor/PDF page counts differ"
differences = {key: {"editor": view["markers"][key], "pdf": actual[key]}
               for key in sorted(expected) if view["markers"][key] != actual[key]}
report = {"pages": len(pdf.pages), "markers": len(expected), "differences": differences,
          "passed": not differences, "scope": "synthetic product bundle and renderer; no database"}
(output / "product-pagination-comparison.json").write_text(
    json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report, ensure_ascii=True))
assert not differences, "Editor/PDF marker pages differ"

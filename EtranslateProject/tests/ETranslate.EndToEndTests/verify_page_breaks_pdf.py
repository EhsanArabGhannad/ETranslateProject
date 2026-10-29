"""Verify manual starts, repeated table headers, intentionally empty pages and footer policy."""
import json
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader

root = Path(sys.argv[1])
pattern = r"PB_(?:\d{3}|ROW_\d{3}|TABLE_BEFORE|AFTER_TABLE|TAIL_\d{3}|EDGE_\d{3}|LIMIT_\d{3})"
results = []
for name in ("plain", "table-image", "trailing-empty", "first-and-empty", "limit"):
    view = json.loads((root / f"{name}.json").read_text(encoding="utf-8"))
    reader = PdfReader(root / f"{name}.pdf")
    texts = [page.extract_text() for page in reader.pages]
    found = {}
    for number, text in enumerate(texts, 1):
        for marker in re.findall(pattern, text): found.setdefault(marker, []).append(number)
        assert "PB_HEADER" in text and "DRAFT - UNSIGNED" in text
        assert ("PB_LAST_FOOTER" in text) == (number == len(texts)), (name, number, "last footer")
    assert len(texts) == view["pageCount"]
    assert set(found) == set(view["markers"])
    assert all(pages[0] == view["markers"][marker] for marker, pages in found.items())
    assert all(len(pages) == 1 for marker, pages in found.items() if marker != "PB_ROW_000")
    if name in ("plain", "table-image"):
        assert view["markers"]["PB_002"] == 2 and view["markers"]["PB_007"] == 3
        with pdfplumber.open(root / f"{name}.pdf") as pdf:
            def top(marker):
                page = pdf.pages[found[marker][0] - 1]
                return next(word["top"] for word in page.extract_words() if word["text"] == marker)
            for marker in ["PB_002", "PB_007"] + (["PB_TABLE_BEFORE", "PB_AFTER_TABLE"] if name == "table-image" else []):
                assert abs(top(marker) - top("PB_000")) < 1.5, (name, marker, "not at content top")
        if name == "table-image":
            last_row = max(pages[0] for marker, pages in found.items() if marker.startswith("PB_ROW_"))
            assert found["PB_AFTER_TABLE"][0] == last_row + 1
            occupied = sorted({pages[0] for marker, pages in found.items() if marker.startswith("PB_ROW_") and marker != "PB_ROW_000"})
            assert found["PB_ROW_000"] == occupied
            assert sum(len(page.images) for page in reader.pages) == 1
    elif name == "trailing-empty":
        assert len(texts) == 2 and not re.findall(pattern, texts[-1]) and view["starts"] == [2]
    elif name == "first-and-empty":
        assert len(texts) == 3 and view["starts"] == [1, 2, 3] and not re.findall(pattern, texts[1])
    else:
        assert len(texts) == 50 and view["starts"] == list(range(2, 51))
        assert all(found[f"PB_LIMIT_{i:03}"] == [i + 1] for i in range(50))
    results.append({"name": name, "pages": len(texts), "markers": len(found), "manualStarts": view["starts"], "passed": True})
report = {"passed": True, "cases": results, "scope": "native page starts and actual PDF marker pages/top positions; last-page footer and empty-page policies"}
(root / "break-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))

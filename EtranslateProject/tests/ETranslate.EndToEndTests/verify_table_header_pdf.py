"""Verify the same existing table before enabling and after disabling its first-row heading."""
import json
from pathlib import Path
import re
import sys
from pypdf import PdfReader

directory = Path(sys.argv[1])
expected = {f"HRT_{i:03}" for i in range(42)}
reports = []
for mode in ("mixed", "heading", "ordinary"):
    view = json.loads((directory / f"{mode}-markers.json").read_text(encoding="utf-8"))
    pdf = PdfReader(directory / f"{mode}.pdf")
    occurrences = {}
    for number, page in enumerate(pdf.pages, 1):
        for marker in re.findall(r"HRT_\d{3}", page.extract_text()):
            occurrences.setdefault(marker, []).append(number)
    assert set(view["markers"]) == expected and set(occurrences) == expected
    assert len(pdf.pages) == view["pageCount"]
    differences = {key: {"editor": view["markers"][key], "pdf": occurrences[key][0]}
                   for key in sorted(expected) if view["markers"][key] != occurrences[key][0]}
    assert not differences, (mode, differences)
    for marker, pages in occurrences.items():
        if marker != "HRT_000":
            assert len(pages) == 1, (mode, marker, pages)
    body_pages = sorted({page for key, pages in occurrences.items() if key != "HRT_000" for page in pages})
    if mode == "heading":
        assert len(body_pages) > 1 and occurrences["HRT_000"] == body_pages
    else:
        assert len(occurrences["HRT_000"]) == 1, (mode, "First row must not repeat")
    reports.append({"mode": mode, "pages": len(pdf.pages), "markers": len(expected), "differences": differences,
                    "headingOccurrences": occurrences["HRT_000"], "passed": True})
report = {"passed": True, "cases": reports, "totalMarkers": len(expected) * len(reports),
          "scope": "existing table first-row action, synthetic product bundle and renderer; no database"}
(directory / "header-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))

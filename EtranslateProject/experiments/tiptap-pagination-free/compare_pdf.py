"""Compare synthetic ASCII marker page assignments, not just the number of pages."""
import json
import re
import sys
from pathlib import Path
from pypdf import PdfReader

folder = Path(sys.argv[1]).resolve()
browser = json.loads((folder / "browser-report.json").read_text(encoding="utf-8"))
reader = PdfReader(folder / "text-reference.pdf")
pdf_markers = {}
for index, page in enumerate(reader.pages, start=1):
    for marker in re.findall(r"MARK_\d{3}", page.extract_text() or ""):
        if marker in pdf_markers:
            raise ValueError(f"Duplicate synthetic marker in PDF: {marker}")
        pdf_markers[marker] = index
editor_markers = {k: v for k, v in browser["Text"]["markers"].items() if k.startswith("MARK_")}
if len(pdf_markers) != 48 or set(pdf_markers) != set(editor_markers):
    raise ValueError("Synthetic text markers lost in PDF or editor")
differences = [{"marker": key, "editorPage": editor_markers[key], "pdfPage": pdf_markers[key]}
               for key in sorted(pdf_markers) if editor_markers[key] != pdf_markers[key]]
result = {"syntheticMarkers": 48, "editorPages": browser["Text"]["pages"], "pdfPages": len(reader.pages),
          "differentMarkerPages": differences, "markerPageAssignmentsMatch": not differences,
          "allLineBoundariesProven": False, "productionApproved": False}
(folder / "pdf-comparison.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))


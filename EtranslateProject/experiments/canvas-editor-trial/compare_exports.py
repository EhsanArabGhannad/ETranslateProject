"""Synthetic export checks; do not mistake raster appearance for searchable text."""
import json
import re
import sys
from pathlib import Path
from pypdf import PdfReader

folder = Path(sys.argv[1]).resolve()
browser = json.loads((folder / "browser-report.json").read_text(encoding="utf-8"))
raster = PdfReader(folder / "image-path.pdf")
html = PdfReader(folder / "html-path.pdf")
raster_text = "".join(p.extract_text() or "" for p in raster.pages)
html_pages = {}
for index, page in enumerate(html.pages, 1):
    for marker in re.findall(r"MARK_\d{3}", page.extract_text() or ""):
        if marker in html_pages:
            raise ValueError(f"Duplicate synthetic marker: {marker}")
        html_pages[marker] = index
native = browser["NativeMarkerPages"]
if len(html_pages) != 48 or set(html_pages) != set(native):
    raise ValueError("HTML export lost synthetic text markers")
diff = [{"marker": k, "canvasPage": native[k], "htmlPdfPage": html_pages[k]}
        for k in sorted(native) if native[k] != html_pages[k]]
positions = browser["RtlCursorPositions"]
xs = [p["x"] for p in positions]
result = {"rasterPages": len(raster.pages), "rasterExtractableTextCharacters": len(raster_text),
          "rasterEmbeddedImages": sum(len(p.images) for p in raster.pages), "htmlPdfPages": len(html.pages),
          "canvasPages": browser["Text"]["pages"], "htmlMarkerCount": len(html_pages),
          "differentMarkerPages": diff, "markerPageAssignmentsMatch": not diff,
          "rtlLogicalCharacterXPositions": xs,
          "rtlLogicalCharactersMoveLeftToRight": len(xs) > 1 and all(b > a for a, b in zip(xs, xs[1:])),
          "fullBidiVerified": False, "productionApproved": False}
if raster_text.strip() or result["rasterEmbeddedImages"] < len(raster.pages):
    raise ValueError("Unexpected raster fixture export; inspect actual path")
(folder / "export-comparison.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))


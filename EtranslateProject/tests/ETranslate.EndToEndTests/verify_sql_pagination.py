"""Verify PDFs downloaded through the real BFF against browser DOM and stored metadata."""
import hashlib
import json
from pathlib import Path
import re
import sys
import pdfplumber
from pypdf import PdfReader
from verify_typography_pdf import assert_typography
from verify_inline_scripts_pdf import assert_sql_script

directory = Path(sys.argv[1])
report = json.loads((directory / "browser-report.json").read_text(encoding="utf-8"))
cleanup = json.loads((directory / "cleanup.json").read_text(encoding="utf-8"))
assert cleanup["cleanupSucceeded"] and cleanup["databasesCreated"] == 6
assert not cleanup["existingDatabasesUsed"]
expected = {f"MARK_{i:03}" for i in range(1,47)} | {"END_MARKER"}
results = []
assert len(report["samples"]) == 3
for sample in report["samples"]:
    name = sample["name"]
    view = json.loads((directory / f"{name}-markers.json").read_text(encoding="utf-8"))
    pdf = PdfReader(directory / f"{name}.pdf")
    texts = [page.extract_text() for page in pdf.pages]
    if sample["archiveVerified"]:
        assert sample["searchReplaceVerified"] and sample["columnWidthsVerified"] and sample["cellStyleVerified"] and sample["horizontalMergeVerified"] and sample["bodyImagesVerified"] and sample["manualPageBreakVerified"] and "QA_REPLACED" in "\n".join(texts)
        assert sum(len(page.images) for page in pdf.pages) == 2
        assert sample["wordPasteVerified"]
        with pdfplumber.open(directory / f"{name}.pdf") as positioned:
            assert sample["nativeWordPasteVerified"]
            horizontal = [page.filter(lambda o: o['object_type'] != 'char' or (abs(o['matrix'][1]) < .01 and abs(o['matrix'][2]) < .01)).extract_text() for page in positioned.pages]
            for marker in ["RW_TYPO_START", "RW_STYLE", "RW_PAGE_TWO", "RW_TYPO_END"]: assert marker in "\n".join(horizontal), marker
            assert "WP_SQL" in "\n".join(page.extract_text() for page in positioned.pages)
            pasted_chars = [char for page in positioned.pages for char in page.chars
                            if "NotoSerif" in char["fontname"] and abs(char["size"] - 14) < .06
                            and isinstance(char.get("non_stroking_color"), (list, tuple))
                            and len(char["non_stroking_color"]) == 3
                            and all(abs(float(a) - b / 255) < .003 for a, b in zip(char["non_stroking_color"], (21, 128, 61)))]
            assert len(pasted_chars) >= 6, "Expected pasted Noto Serif 14pt green text in persisted PDF"
    typography = None
    inline_script = None
    markers = {}
    for number, text in enumerate(texts,1):
        for marker in re.findall(r"MARK_\d{3}|END_MARKER",text):
            assert marker not in markers, (name, "duplicate", marker)
            markers[marker] = number
        assert "DRAFT - UNSIGNED" in text
    assert set(markers) == set(view["markers"]) == expected, (name,"marker set")
    assert markers == view["markers"], (name,"marker pages")
    if sample["archiveVerified"]: assert markers["END_MARKER"] > max(page for marker, page in markers.items() if marker.startswith("MARK_"))
    assert len(texts) == sample["pages"] == view["pageCount"], (name,"page count")
    if sample["lastFooter"]:
        assert all("QA_HEADER" in text for text in texts)
        assert all("QA_LAST_FOOTER" not in text for text in texts[:-1])
        assert "QA_LAST_FOOTER" in texts[-1]
        assert sample["typographyVerified"]
        typography = assert_typography(directory / f"{name}.pdf")
        assert sample["paragraphSettingsVerified"] and sample["paragraphSpacingVerified"] and sample["inlineScriptVerified"]
        inline_script = assert_sql_script(directory / f"{name}.pdf")
    elif sample["hasTemplate"]:
        assert all("QA_LEGACY_HEADER" in text and "QA_LEGACY_FOOTER" in text for text in texts)
    results.append({"name":name,"pages":len(texts),"markers":len(markers),"passed":True,
                    "sha256":hashlib.sha256((directory/f"{name}.pdf").read_bytes()).hexdigest(),"typography":typography,"inlineScript":inline_script})
summary = {"passed":True,"cases":results,"markers":sum(item["markers"] for item in results),
           "realServices":6,"sqlCleanupSucceeded":True,"archiveFixture":report["archiveFixture"]}
(directory / "pdf-comparison.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps(summary,ensure_ascii=True))

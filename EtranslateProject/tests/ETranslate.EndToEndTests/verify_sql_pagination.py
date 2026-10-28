"""Verify PDFs downloaded through the real BFF against browser DOM and stored metadata."""
import hashlib
import json
from pathlib import Path
import re
import sys
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
        assert sample["searchReplaceVerified"] and sample["columnWidthsVerified"] and sample["cellStyleVerified"] and sample["horizontalMergeVerified"] and sample["bodyImagesVerified"] and "QA_REPLACED" in "\n".join(texts)
        assert sum(len(page.images) for page in pdf.pages) == 2
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

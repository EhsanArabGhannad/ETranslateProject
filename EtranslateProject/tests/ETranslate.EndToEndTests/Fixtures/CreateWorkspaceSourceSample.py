"""Create a deterministic, non-sensitive two-page PDF fixture for the workspace viewer."""
from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A4
from reportlab.lib.colors import HexColor

project = Path(__file__).resolve().parents[3]
output = project / "output" / "pdf" / "workspace-source-sample.pdf"
output.parent.mkdir(parents=True, exist_ok=True)
pdf = canvas.Canvas(str(output), pagesize=A4, invariant=1, pageCompression=0)
pdf.setTitle("ETranslate - synthetic source document")
pdf.setAuthor("ETranslate development fixtures")
width, height = A4
for number, heading, lines in [
    (1, "Document details", ["Reference: SAMPLE-001", "Source language: English", "Target language: Turkish", "Purpose: local software testing"]),
    (2, "Translation notes", ["Check that the second page is visible.", "Try the PDF zoom and page-navigation controls.", "Formatting in the translation must survive saving.", "The original source file must remain unchanged."]),
]:
    pdf.setFillColor(HexColor("#167064"))
    pdf.rect(0, height - 125, width, 125, fill=1, stroke=0)
    pdf.setFillColor(HexColor("#ffffff"))
    pdf.setFont("Helvetica-Bold", 23)
    pdf.drawString(48, height - 65, "ETranslate source sample")
    pdf.setFont("Helvetica", 11)
    pdf.drawString(48, height - 95, "Synthetic fixture - not a customer document")
    pdf.setFillColor(HexColor("#193a39"))
    pdf.setFont("Helvetica-Bold", 18)
    pdf.drawString(48, height - 180, heading)
    pdf.setFont("Helvetica", 13)
    for index, line in enumerate(lines):
        pdf.drawString(48, height - 230 - index * 36, line)
    pdf.setFillColor(HexColor("#657b79"))
    pdf.setFont("Helvetica", 10)
    pdf.drawString(48, 85, "No signature, certification, or legal effect.")
    pdf.drawString(48, 65, "Use only in a development/test environment.")
    pdf.drawRightString(width - 48, 45, f"Page {number} / 2")
    pdf.showPage()
pdf.save()
print(output)

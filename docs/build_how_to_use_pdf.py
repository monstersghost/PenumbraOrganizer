"""Renders docs/HOW_TO_USE_DRAFT.md into a PDF for release packaging.

Not a general markdown-to-PDF tool: it only understands the subset of markdown this
one document uses (headings, bold, inline code, bullet/numbered lists, blockquotes,
fenced code blocks). List items are rendered as single hanging-indent Paragraphs
(not ListFlowable) so a page break can't split a bullet/number from its text.
"""
import re
import sys
from pathlib import Path

from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib.enums import TA_LEFT
from reportlab.lib import colors
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Preformatted, HRFlowable

SRC = Path(__file__).parent / "HOW_TO_USE.md"
OUT = Path(__file__).parent / "HOW_TO_USE.pdf"

styles = getSampleStyleSheet()
title_style = ParagraphStyle("TitleCustom", parent=styles["Title"], fontSize=22, spaceAfter=4, alignment=TA_LEFT)
subtitle_style = ParagraphStyle("Subtitle", parent=styles["Normal"], fontSize=10, textColor=colors.HexColor("#555555"), spaceAfter=16, fontName="Helvetica-Oblique")
h2_style = ParagraphStyle("H2Custom", parent=styles["Heading2"], fontSize=14, spaceBefore=16, spaceAfter=8, textColor=colors.HexColor("#1a1a1a"))
body_style = ParagraphStyle("BodyCustom", parent=styles["Normal"], fontSize=10.5, leading=15, spaceAfter=8)
bullet_style = ParagraphStyle("BulletCustom", parent=body_style, leftIndent=16, firstLineIndent=-16, spaceAfter=6)
quote_style = ParagraphStyle("Quote", parent=body_style, leftIndent=14, textColor=colors.HexColor("#2f5d3a"), spaceAfter=8)
code_style = ParagraphStyle("Code", parent=styles["Code"], fontSize=9.5, leading=13, backColor=colors.HexColor("#f2f2f2"))


# Base PDF fonts (Helvetica/Courier) have no emoji glyphs and render them as black boxes.
# The markdown source keeps emoji for GitHub/web viewing; strip them only for this PDF.
_EMOJI_RE = re.compile(
    "[\U0001F300-\U0001FAFF\U00002600-\U000027BF\U0001F1E6-\U0001F1FF]+\\s*"
)


def inline(text: str) -> str:
    text = _EMOJI_RE.sub("", text)
    text = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    text = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", text)
    text = re.sub(r"`([^`]+)`", r'<font face="Courier" size="9.5" backColor="#eeeeee">\1</font>', text)
    return text


def is_block_start(stripped: str) -> bool:
    return (
        stripped == ""
        or stripped.startswith("#")
        or stripped.startswith("```")
        or stripped.startswith(">")
        or bool(re.match(r"^\*\s+", stripped))
        or bool(re.match(r"^\d+\.\s+", stripped))
    )


def render(md_text: str):
    lines = md_text.split("\n")
    story = []
    i = 0
    n = len(lines)
    para_buf = []

    def flush_paragraph():
        if para_buf:
            story.append(Paragraph(inline(" ".join(para_buf).strip()), body_style))
            para_buf.clear()

    while i < n:
        stripped = lines[i].strip()

        if stripped.startswith("```"):
            flush_paragraph()
            i += 1
            code_lines = []
            while i < n and not lines[i].strip().startswith("```"):
                code_lines.append(lines[i])
                i += 1
            i += 1
            story.append(Preformatted("\n".join(code_lines), code_style))
            story.append(Spacer(1, 8))
            continue

        if stripped.startswith("# "):
            flush_paragraph()
            story.append(Paragraph(inline(stripped[2:]), title_style))
            i += 1
            continue

        if stripped.startswith("## "):
            flush_paragraph()
            story.append(Paragraph(inline(stripped[3:]), h2_style))
            story.append(HRFlowable(width="100%", thickness=0.6, color=colors.HexColor("#dddddd"), spaceAfter=6))
            i += 1
            continue

        if stripped.startswith("_"):
            flush_paragraph()
            block = [stripped.lstrip("_")]
            i += 1
            while i < n and not lines[i].strip().endswith("_"):
                block.append(lines[i].strip())
                i += 1
            if i < n:
                block.append(lines[i].strip().rstrip("_"))
                i += 1
            story.append(Paragraph(inline(" ".join(block).strip()), subtitle_style))
            continue

        if stripped.startswith(">"):
            flush_paragraph()
            quote_lines = []
            while i < n and lines[i].strip().startswith(">"):
                quote_lines.append(lines[i].strip().lstrip(">").strip())
                i += 1
            story.append(Paragraph(inline(" ".join(quote_lines)), quote_style))
            continue

        bullet_match = re.match(r"^\*\s+(.*)$", stripped)
        number_match = re.match(r"^(\d+)\.\s+(.*)$", stripped)
        if bullet_match or number_match:
            flush_paragraph()
            prefix = "•  " if bullet_match else f"{number_match.group(1)}.  "
            item_text = bullet_match.group(1) if bullet_match else number_match.group(2)
            i += 1
            while i < n and lines[i].strip() and not is_block_start(lines[i].strip()):
                item_text += " " + lines[i].strip()
                i += 1
            story.append(Paragraph(prefix + inline(item_text), bullet_style))
            continue

        if stripped == "":
            flush_paragraph()
            i += 1
            continue

        para_buf.append(stripped)
        i += 1

    flush_paragraph()
    return story


def main():
    md_text = SRC.read_text(encoding="utf-8")
    story = render(md_text)
    doc = SimpleDocTemplate(
        str(OUT),
        pagesize=letter,
        leftMargin=0.85 * inch,
        rightMargin=0.85 * inch,
        topMargin=0.75 * inch,
        bottomMargin=0.75 * inch,
        title="How to Use Penumbra Organizer",
        author="Penumbra Organizer Contributors",
    )
    doc.build(story)
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    sys.exit(main())

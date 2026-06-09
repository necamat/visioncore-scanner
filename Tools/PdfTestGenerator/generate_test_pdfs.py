#!/usr/bin/env python3
"""
generate_test_pdfs.py
Generates synthetic scanned-style quiz-sheet PDFs that reproduce the physical
form defined by the Excel/VBA template (ExcelScript/excel code.txt):

    * four L-shaped corner markers (MK_TL/TR/BL/BR)
    * one TeamId outline box  (DB_TEAM_ID)
    * one Score outline box   (DB_SCORE) with three inner digit boxes

Each PDF page is a single full-page bitmap (A4 @ 200 DPI = 1654 x 2338 px). The
box rectangles match appsettings.json "PdfRegions", so the console host crops
exactly the printed boxes.

NOTE: the automated test suite does NOT use this script; it builds equivalent
PDFs in-process (SyntheticPdfDatasetBuilder). This tool is for manual end-to-end
runs and visual inspection only.

Dependencies:
    pip install -r requirements.txt        # pillow + reportlab

Usage:
    python generate_test_pdfs.py [output_dir]
    # writes <output_dir>/R1/sheet.pdf, R2/sheet.pdf, R3/sheet.pdf
    # then:  dotnet run --project ../../VisionCore.Console -- <output_dir>
"""

import os
import sys
from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas

PAGE_W, PAGE_H = 1654, 2338            # A4 @ 200 DPI
FORM = (130, 90, 1394, 2160)           # x, y, w, h
MARKER_INSET, MARKER_ARM = 0.018, 0.016

# Box rectangles (x, y, w, h) — must match appsettings.json "PdfRegions".
TEAM_BOX = (1330, 200, 110, 120)
TEAM_D1 = (1338, 214, 46, 92)
TEAM_D2 = (1392, 214, 46, 92)
SCORE_BOX = (1090, 1980, 295, 120)
SCORE_D1 = (1108, 1999, 78, 82)
SCORE_D2 = (1198, 1999, 78, 82)
SCORE_D3 = (1288, 1999, 78, 82)

SAMPLES = [
    {"round": 1, "team_id": 16, "score": 380},
    {"round": 2, "team_id": 16, "score": 100},
    {"round": 3, "team_id": 16,  "score": 400},
    {"round": 1, "team_id": 20, "score": 380},
    {"round": 2, "team_id": 20, "score": 500},
    {"round": 3, "team_id": 20,  "score": 620},
    {"round": 1, "team_id": 35, "score": 666},
    {"round": 2, "team_id": 35, "score": 779},
    {"round": 3, "team_id": 35,  "score": 22},
]


def _font(pixel_height):
    # Use a sans-serif face to match the recognizer's digit templates.
    for name in ("arialbd.ttf", "arial.ttf", "LiberationSans-Bold.ttf",
                 "LiberationSans-Regular.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size=pixel_height)
        except OSError:
            continue
    return ImageFont.load_default()


def _box(draw, rect, width=3):
    x, y, w, h = rect
    draw.rectangle([x, y, x + w, y + h], outline=0, width=width)


def _digit(draw, ch, rect):
    x, y, w, h = rect
    font = _font(int(h * 0.58))
    left, top, right, bottom = draw.textbbox((0, 0), ch, font=font)
    tx = x + (w - (right - left)) / 2 - left
    ty = y + (h - (bottom - top)) / 2 - top
    draw.text((tx, ty), ch, fill=0, font=font)


def _markers(draw):
    fx, fy, fw, fh = FORM
    arm_w = round(fw * MARKER_ARM)
    arm_h = round(fh * MARKER_ARM)
    th = max(2, round(min(arm_w, arm_h) * 0.26))
    ix, iy = round(fw * MARKER_INSET), round(fh * MARKER_INSET)
    left, right = fx + ix, fx + fw - ix - arm_w
    top, bottom = fy + iy, fy + fh - iy - arm_h
    for x, y, is_top, is_left in (
        (left, top, True, True), (right, top, True, False),
        (left, bottom, False, True), (right, bottom, False, False),
    ):
        hy = y if is_top else y + arm_h - th
        vx = x if is_left else x + arm_w - th
        draw.rectangle([x, hy, x + arm_w, hy + th], fill=0)
        draw.rectangle([vx, y, vx + th, y + arm_h], fill=0)


def render_page(team_id, score):
    img = Image.new("L", (PAGE_W, PAGE_H), color=255)
    draw = ImageDraw.Draw(img)

    _markers(draw)
    _box(draw, TEAM_BOX)
    _box(draw, SCORE_BOX)
    _box(draw, SCORE_D1)
    _box(draw, SCORE_D2)
    _box(draw, SCORE_D3)

    team = f"{team_id:02d}"
    _digit(draw, team[0], TEAM_D1)
    _digit(draw, team[1], TEAM_D2)

    sc = f"{score:03d}"
    _digit(draw, sc[0], SCORE_D1)
    _digit(draw, sc[1], SCORE_D2)
    _digit(draw, sc[2], SCORE_D3)

    return img


def save_as_pdf(img, path):
    tmp_png = path + ".tmp.png"
    img.save(tmp_png, format="PNG")
    pdf_w, pdf_h = A4
    c = canvas.Canvas(path, pagesize=A4)
    c.drawImage(tmp_png, 0, 0, width=pdf_w, height=pdf_h, preserveAspectRatio=False)
    c.showPage()
    c.save()
    os.remove(tmp_png)
    print(f"  {path}  ({img.width}x{img.height}px -> A4 PDF)")


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "generated_input")
    print(f"Generating {len(SAMPLES)} test PDFs under: {out_dir}")
    for s in SAMPLES:
        round_dir = os.path.join(out_dir, f"R{s['round']}")
        os.makedirs(round_dir, exist_ok=True)
        save_as_pdf(render_page(s["team_id"], s["score"]), os.path.join(round_dir, f"{s['round']}_{s['team_id']}.pdf"))
    print("Done.")


if __name__ == "__main__":
    main()

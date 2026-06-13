#!/usr/bin/env python3
"""
generate_handwritten_pdfs.py
Generates synthetic quiz-sheet PDFs whose SCORE digits are real handwriting —
genuine MNIST samples pasted into the three score cells — while the team id
stays printed. This is the manual companion to the OnnxHandwrittenScoreEndToEnd
test: it lets you eyeball the sheets and run the console with the ONNX engine.

The form (markers + boxes + printed team id) is drawn by generate_test_pdfs.py;
this script only swaps the printed score for handwritten MNIST digits. The MNIST
PNGs are the same ones the test suite uses, committed under
VisionCore.Tests/TestData/Mnist/ (named <digit>_<sample>.png, sample 0-2).

Dependencies:
    pip install -r requirements.txt        # pillow + reportlab

Usage:
    python generate_handwritten_pdfs.py [output_dir]
    # writes <output_dir>/R1/hw_team<NN>_score<NNN>.pdf
    # then, with the ONNX score engine:
    #   cd ../../VisionCore.Console
    #   DigitRecognitionOptions__ScoreEngine=Onnx dotnet run -- <output_dir>
"""

import os
import sys
from PIL import Image, ImageDraw

import generate_test_pdfs as form

MNIST_DIR = os.path.normpath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)),
                 "..", "..", "VisionCore.Tests", "TestData", "Mnist"))

# (team_id, [(digit, sample) x3], expected_score). Sample is 0-2.
SHEETS = [
    (12, [(5, 0), (8, 0), (3, 0)], 583),
    (7,  [(1, 1), (9, 0), (6, 0)], 196),
    (34, [(7, 0), (0, 1), (5, 1)], 705),
    (23, [(0, 2), (4, 1), (2, 0)], 42),
]


def _paste_handwritten(img, rect, digit, sample):
    x, y, w, h = rect
    path = os.path.join(MNIST_DIR, f"{digit}_{sample}.png")
    glyph = Image.open(path).convert("L")
    glyph = Image.eval(glyph, lambda v: 255 - v)        # MNIST is light-on-dark -> dark-on-white
    target = int(min(w, h) * 0.62)                      # leave a margin from the printed box border
    glyph = glyph.resize((target, target))
    img.paste(glyph, (x + (w - target) // 2, y + (h - target) // 2))


def render_handwritten_page(team_id, score_cells):
    img = Image.new("L", (form.PAGE_W, form.PAGE_H), color=255)
    draw = ImageDraw.Draw(img)

    form._markers(draw)
    form._box(draw, form.TEAM_BOX)
    form._box(draw, form.SCORE_BOX)
    form._box(draw, form.SCORE_D1)
    form._box(draw, form.SCORE_D2)
    form._box(draw, form.SCORE_D3)

    team = f"{team_id:02d}"
    form._digit(draw, team[0], form.TEAM_D1)            # team id stays printed
    form._digit(draw, team[1], form.TEAM_D2)

    for rect, (digit, sample) in zip((form.SCORE_D1, form.SCORE_D2, form.SCORE_D3), score_cells):
        _paste_handwritten(img, rect, digit, sample)    # score is real handwriting

    return img


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "generated_handwritten")

    if not os.path.isdir(MNIST_DIR):
        sys.exit(f"MNIST samples not found at {MNIST_DIR}")

    print(f"Generating {len(SHEETS)} handwritten-score PDFs under: {out_dir}")
    for team_id, cells, score in SHEETS:
        round_dir = os.path.join(out_dir, "R1")
        os.makedirs(round_dir, exist_ok=True)
        name = f"hw_team{team_id:02d}_score{score:03d}.pdf"
        page = render_handwritten_page(team_id, cells)
        form.save_as_pdf(page, os.path.join(round_dir, name))
        samples = " ".join(f"{d}_{s}.png" for d, s in cells)
        print(f"      expect team {team_id:02d}, score {score:03d}   (MNIST: {samples})")
    print("Done. Run the console with DigitRecognitionOptions__ScoreEngine=Onnx to read them.")


if __name__ == "__main__":
    main()

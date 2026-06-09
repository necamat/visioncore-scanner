# PDF Test Generator

A small Python helper that produces synthetic *scanned-style* quiz-sheet PDFs
for manually exercising the VisionCore PDF pipeline end to end.

Each generated page reproduces the printed form:

- four L-shaped corner markers,
- one **team-id** outline box, and
- one **score** outline box with three inner digit cells,

rendered on a single full-page A4 bitmap at 200 DPI (1654 × 2338 px). The boxes
are drawn at the exact pixel rectangles configured in the app's
`appsettings.json` → `PdfRegions`, so the console host crops precisely the
printed boxes.

> **Note** — the automated test suite does **not** use this script. The tests
> build equivalent PDFs in-process (`SyntheticPdfDatasetBuilder`), so
> `dotnet test` stays self-contained. This tool is for manual runs and visual
> inspection.
>
> Because recognition is template-matching (see the README "Digit recognition"
> section), it is calibrated for the app's own SkiaSharp rendering. Pillow
> rasterizes glyphs slightly differently, so a few digits generated here may
> read with lower confidence (`NeedsReview`). For an authoritative end-to-end
> demo that matches the recognizer's rendering, generate sheets with the C#
> `SyntheticPdfDatasetBuilder` instead — this Python tool uses a sans-serif font
> to stay as close as possible.

## Requirements

- Python 3.9+
- Packages in [`requirements.txt`](requirements.txt): **Pillow** (rendering) and
  **reportlab** (PDF output).

```bash
pip install -r requirements.txt
```

## Usage

```bash
# from this folder (Tools/PdfTestGenerator)
python generate_test_pdfs.py [output_dir]
```

- `output_dir` is optional; it defaults to `./generated_input` (git-ignored).
- The script writes `R1/sheet.pdf`, `R2/sheet.pdf`, `R3/sheet.pdf` — one round
  per subfolder, matching the layout the console expects.

Default sample data:

| File          | Team Id | Score |
|---------------|---------|-------|
| `R1/sheet.pdf`| 12      | 75    |
| `R2/sheet.pdf`| 23      | 100   |
| `R3/sheet.pdf`| 7       | 234   |

## Run the app against the generated PDFs

```bash
python generate_test_pdfs.py ./generated_input
dotnet run --project ../../VisionCore.Console -- ./generated_input
```

The console writes `output/rezultati.xlsx` with the recognized team ids and
scores. To change the sample values or coordinates, edit the `SAMPLES` and the
box-rectangle constants near the top of `generate_test_pdfs.py` (keep them in
sync with `appsettings.json` → `PdfRegions`).

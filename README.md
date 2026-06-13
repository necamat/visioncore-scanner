# VisionCore

Automated quiz-sheet scanning and scoring pipeline built on **.NET 10**.

VisionCore reads scanned quiz answer sheets, extracts each team's ID and score,
and exports the results (plus standings) to an Excel workbook.

The current release processes **PDF** scans. The architecture is deliberately
open for closed: adding a new source format (e.g. JPG phone photos) means
plugging in a new set of pipeline steps behind the existing seams, without
touching the orchestration, recognition or export code.

> **Planned:** a JPG (phone-photo) source — marker detection + perspective
> correction feeding the same recognition/evaluation/export path — is on the
> roadmap and will be built as a separate feature behind these seams.

---

## What this demonstrates

A compact but production-shaped .NET system, built to show engineering judgment
rather than feature count:

- **Clean Architecture, enforced.** Four layers
  (`Domain ← Application ← Infrastructure ← Console`) with the dependency rule
  checked at build time by NetArchTest — a stray reference fails the suite.
- **Everything pluggable behind ports.** Region extraction, recognition, export,
  state persistence and review-import each sit behind an interface. The score
  recognizer ships with **two interchangeable engines** — deterministic template
  matching and an **ONNX MNIST CNN** — chosen by configuration; the swap touches
  no pipeline, evaluation or export code.
- **Honest confidence, not guesses.** Recognition gates on the *weakest* digit
  with a margin-aware certainty; anything uncertain surfaces as `NeedsReview`
  instead of being auto-accepted, feeding a human-review loop (correct in Excel →
  re-import → standings regenerate) that validates every row and never touches
  the workbook on a bad import.
- **Tested seriously — 220 tests.** Confusable digit pairs and seeded scan
  degradations driven end-to-end through the production pipeline, real
  handwritten MNIST samples for the ONNX path, plus options/DI/architecture
  guards. CI builds and tests on **Linux, Windows and macOS** behind a
  formatting gate.

New to the code? Skim [Architecture](#architecture) and
[Design patterns](#design-patterns) for the structure, then
[Digit recognition — scope and limits](#digit-recognition--scope-and-limits)
for the trade-offs called out honestly.

---

## Repository layout

A single repository holds the application and its supporting tooling, kept in
separate top-level areas:

```
VisionCore.slnx            .NET solution
VisionCore.Domain/         \
VisionCore.Application/     |  the application (C# / .NET 10)
VisionCore.Infrastructure/ |
VisionCore.Console/        /
VisionCore.Tests/          test suite
Tools/PdfTestGenerator/    standalone Python tool (manual test-PDF generation)
```

`Tools/` is intentionally a sibling of the solution, not a project inside it:
it's a small Python utility with its own dependencies and lifecycle, unrelated
to the .NET build. The app does not depend on it (see *Test data* below).

## Architecture

Clean Architecture, four layers, dependencies pointing inward:

```
VisionCore.Domain          Entities, value objects, typed result records
VisionCore.Application     Use cases, abstractions, pipeline + steps, mapping
VisionCore.Infrastructure  PdfPig, ClosedXML, SkiaSharp implementations
VisionCore.Console         Entry point, DI/host wiring, Serilog, appsettings
```

`Domain ← Application ← Infrastructure ← Console`. The rule is enforced at
build time by the architecture tests in `VisionCore.Tests/Architecture/`
(NetArchTest), so a stray reference fails the suite.

---

## Platform — cross-platform

The solution targets plain `net10.0` and runs on Windows, Linux and macOS. All
image work — page decoding/cropping in `PdfRegionExtractor` and the
template-matching recognizers — goes through a small `GrayImage` abstraction
backed by **SkiaSharp** (MIT-licensed, fully managed native binaries). There is
no dependency on `System.Drawing.Common` (GDI+, Windows-only).

This was a contained adapter swap rather than a rewrite, which is the point of
the layering: the imaging library lives **only in the Infrastructure layer,
behind the `IRegionExtractor` and `IDigitRecognizer` ports**, so replacing the
backend touched no use-case, pipeline, mapping or domain code. Only the geometry
structs (`Rectangle`, `Point`) are reused from `System.Drawing.Primitives`,
which is itself cross-platform.

---

## Design patterns

- **Factory (by file extension)** — `IPipelineFactory` / `PipelineFactory`
  selects the pipeline for a source file from its extension. Today `.pdf` is
  registered; new formats add one `switch` arm.
- **Pipeline + Strategy** — a run is a chain of `IImageProcessingStep` strategies
  over a shared `PipelineContext`. The generic `StepPipeline` runner orders steps
  by `PipelineStage`, executes them, and stops at the first failure. Steps are
  composed, not inherited — new behaviour is a new step, not a subclass.
- **Strategy (region extraction)** — `IRegionExtractor` isolates *how* regions are
  obtained. `PdfRegionExtractor` crops fixed coordinates; a future photo-based
  extractor would detect markers and warp perspective behind the same interface.
- **Result pattern** — `Result` for operation boundaries (folder run, export);
  the imaging domain uses typed-failure result records (`RegionExtractionResult`,
  `DigitRecognitionResult`, `FinalScanResult`) that carry enum failure codes.
- **Options pattern** — all tunables bind from `appsettings.json` and are
  validated at startup (`ValidateDataAnnotations().ValidateOnStart()`).

### Digit recognition — scope and limits

Recognition uses **template matching**: each cropped digit is compared against
glyph templates the recognizer renders itself. It is intentionally simple and
deterministic, and it is **sensitive to the rasterizer and font of the input** —
inherent to template matching. It is calibrated for the rendered, fixed-layout
input this project targets, not arbitrary scans.

That trade-off is deliberate for a portfolio project: the value here is the
architecture, not a production OCR. Crucially, recognition sits behind the
`IDigitRecognizer` port, so swapping in a real engine (Tesseract, an ONNX model)
is a localized change — no use-case, pipeline, mapping or export code is touched.
Low-confidence reads are surfaced as `NeedsReview` rather than guessed, which is
the correct behaviour for any real scanning system.

### ONNX engine for handwritten scores

The port swap is not just theory — the score digits (handwritten on a real
form, which font templates are structurally bad at) can be recognized by an
**MNIST-class CNN via ONNX Runtime** instead:

```json
"DigitRecognitionOptions": {
  "ScoreEngine": "Onnx",
  "OnnxModelPath": "./Models/mnist-12.onnx"
}
```

`OnnxScoreRecognizer` plugs in behind the same `IScoreRecognizer` port — the
printed team id keeps using template matching, and no pipeline, evaluation or
export code changes. Each digit cell goes through the shared glyph isolation
(border/edge-ink removal), is normalized the MNIST way (20x20 box centered on
a 28x28 field), and the reported confidence is the model's softmax probability
for the winning digit — a confident read sits near 1.0 and an ambiguous one
falls into the review band on its own, so the existing accept/review thresholds
keep working without code changes. (Those thresholds were calibrated for the
template engine; for production handwriting you would re-check them against a
real sample.) The committed model (`mnist-12.onnx`, 26 KB, from the
[ONNX Model Zoo](https://github.com/onnx/models), Apache-2.0) runs fully
offline; it is verified both at the unit level (against thirty genuine
handwritten MNIST samples committed under `TestData/Mnist`) and **end-to-end** —
real MNIST digits pasted into the form and driven through the production PDF
pipeline, including confusable digits, with the same guarantee the template
engine carries: an auto-accepted score is never silently wrong.

### PDF pipeline

```
.pdf ──PipelineFactory──▶ StepPipeline
        PdfRegionExtractionStep  (IRegionExtractor → crop fixed regions)
     ▶  DigitRecognitionStep     (IDigitRecognizer → team id + score)
     ▶  PdfConfidenceEvaluationStep (accept / review / reject)
                                  │
                                  ▼
        ScanQuizSheetsUseCase ▶ QuizResult ▶ IExcelExporter (ClosedXML)
```

Scanned A4 PDFs share a fixed layout at a given DPI, so extraction is
deterministic: open the page, read its embedded image, crop the configured
rectangles. Coordinates live in `appsettings.json` under `PdfRegions` and are
measured against the printed form (a team-id box and a score box with three
digit cells):

```json
"PdfRegions": {
  "Dpi": 200,
  "TeamId":  { "X": 1330, "Y": 200,  "Width": 110, "Height": 120 },
  "Score":   { "X": 1090, "Y": 1980, "Width": 295, "Height": 120 }
}
```

Adjust these after measuring a real scan — no recompile needed.

---

## How to run

**Prerequisites:** .NET 10 SDK (Windows, Linux or macOS).

```bash
# build
dotnet build VisionCore.slnx

# run — point at a folder of round subfolders (R1/, R2/, ...) containing PDFs
cd VisionCore.Console
dotnet run -- ./input
```

Output: `./output/QuizResult.xlsx` (a `Scans` sheet and a `Standings` sheet).
The process exit code is `0` on success, non-zero on failure. Logs are written
to the console and a daily rolling file under `./logs` (Serilog).

```bash
# test
dotnet test VisionCore.slnx
```

### Incremental runs & parallelism

Sheets are scanned concurrently across all rounds; the worker count comes from
`ProcessingOptions.MaxDegreeOfParallelism` (0 = one per core). Each round's
results are persisted to `.visioncore-state.json` in the input root together
with file fingerprints and a hash of the recognition configuration — on the
next run, rounds whose files (and configuration) are unchanged reuse their
results instead of being scanned again. Set
`ProcessingOptions.ReuseUnchangedRounds` to `false` to force a full re-scan.

### Calibrating the confidence thresholds

The evaluation gates on the **weakest digit** of a sheet. The accept threshold
must sit above the heuristic confidence tiers (validated at startup) and below
the weakest clean template read for your scan stack. To re-calibrate against
real scans, run a representative batch and inspect the per-digit values in the
log (`TeamIdDigitConfidenceTrace` / `ScoreDigitConfidenceTrace`), then set
`ConfidenceEvaluationOptions` accordingly.

### Review workflow

Scans the recognizer is not confident about are exported as `NeedsReview` and
excluded from the standings until a human confirms them. The loop is:

1. Open the exported workbook, check the flagged rows on the `Scans` sheet,
   correct **Team ID** / **Score** where needed and set **Status** to
   `Accepted` (or `Rejected`).
2. Re-import the reviewed workbook — the standings are regenerated in place:

```bash
dotnet run -- --finalize ./output/QuizResult.xlsx
# without a path it defaults to the configured output workbook
```

The import is validated before anything is written back. The header row must
match the layout VisionCore exports, so a foreign or hand-reordered workbook is
rejected rather than misread. Each data row is then checked — unknown status
values, accepted rows missing a team id or score, non-numeric, fractional or
out-of-range cells (the form fits a two-digit team id and a three-digit score),
and sheets listed twice — and the first problem fails the import with the
offending row's position, without touching the workbook, so a typo in the
review can never silently corrupt the standings. Deleting (or clearing) a row
drops that sheet from the import.

The **Confidence** column records what the recognizer reported at scan time and
is kept as a record; it is not recomputed when you correct a row, so a finalized
sheet still shows the confidence that originally sent it to review.

---

## Test data

The automated tests are self-contained — no committed binaries are required to
run them:

- **Unit / step tests** mock the abstractions.
- **End-to-end tests** build synthetic scanned PDFs in-process
  (`SyntheticPdfDatasetBuilder`) that reproduce the real form (corner markers +
  team/score boxes) at the configured `PdfRegions` coordinates, then drive the
  production factory + pipeline + Excel export.
- A small set of committed sample PDFs under
  `VisionCore.Tests/TestData/PdfSamples/` (`R1`=12/75, `R2`=23/100, `R3`=7/234)
  is exercised by `PdfSampleFilesEndToEndTests` to prove the chain works over
  real on-disk files.

To generate sample PDFs manually for a console run, use the Python tool in
`Tools/PdfTestGenerator/` (renders the same form at the `PdfRegions` coordinates):

```bash
python Tools/PdfTestGenerator/generate_test_pdfs.py ./generated_input
dotnet run --project VisionCore.Console -- ./generated_input
```

---

## Third-party licenses

All dependencies are permissive open-source (MIT / Apache-2.0 / BSD-3-Clause) —
no copyleft (GPL/LGPL) and no commercial license — so the project is free to
publish and use. Licenses below were read from the restored package metadata,
including transitive dependencies.

**Shipped with the application (runtime):**

| Package | License |
|---|---|
| SkiaSharp (+ NativeAssets Win32/Linux/macOS) | MIT |
| ClosedXML | MIT |
| └ ClosedXML.Parser, DocumentFormat.OpenXml, ExcelNumberFormat, RBush.Signed, System.IO.Packaging | MIT |
| └ SixLabors.Fonts `1.0.0` (transitive via ClosedXML) | Apache-2.0 |
| PdfPig | Apache-2.0 |
| Microsoft.ML.OnnxRuntime | MIT |
| └ `mnist-12.onnx` model (ONNX Model Zoo) | Apache-2.0 |
| Microsoft.Extensions.* (Hosting, DI, Configuration, Options, Logging) | MIT |
| Serilog (+ Console / File sinks, Hosting, Settings.Configuration) | Apache-2.0 |

**Build/test only (not distributed):**

| Package | License |
|---|---|
| xUnit (+ runner.visualstudio) | Apache-2.0 |
| Moq | BSD-3-Clause |
| FluentAssertions `7.x` | Apache-2.0 |
| NetArchTest.Rules | MIT |
| Microsoft.NET.Test.Sdk | MIT |

> **Version pins to keep:** **FluentAssertions** is held at **7.x** — version 8+
> moved to a paid commercial license. **SixLabors.Fonts** stays at **1.0.0**
> (Apache-2.0); 2.x adopts a split commercial license. Both are intentionally
> not upgraded.

This project's own code is released under the license in [`LICENSE`](LICENSE).

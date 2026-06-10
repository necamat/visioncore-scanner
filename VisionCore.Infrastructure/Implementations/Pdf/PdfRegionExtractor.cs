namespace VisionCore.Infrastructure.Implementations.Pdf;

using System.Drawing;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using VisionCore.Application.Abstractions;
using VisionCore.Application.Configuration;
using VisionCore.Domain.Imaging;
using VisionCore.Domain.Imaging.Cropping;
using VisionCore.Domain.Imaging.Layout;
using VisionCore.Infrastructure.Imaging;

/// <summary>
/// Extracts form regions from a scanned PDF by reading the embedded page image
/// and cropping it at fixed pixel coordinates loaded from PdfRegionOptions.
///
/// Design rationale: scanned A4 PDFs always produce the same page layout at a
/// given DPI, so marker detection and perspective correction are unnecessary.
/// Coordinates are kept in appsettings.json so they can be adjusted without
/// recompiling when the physical form layout changes.
///
/// Crops are returned as in-memory grayscale pixels (no temp files).
/// </summary>
public sealed class PdfRegionExtractor(IOptions<PdfRegionOptions> options) : IRegionExtractor
{
    private readonly PdfRegionOptions _options = options.Value;

    public Task<RegionExtractionResult> ExtractAsync(string sourcePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var result = ExtractCore(sourcePath);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                RegionExtractionResult.Failure($"PDF region extraction failed: {ex.Message}"));
        }
    }

    private RegionExtractionResult ExtractCore(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);

        if (document.NumberOfPages == 0)
        {
            return RegionExtractionResult.Failure("PDF contains no pages.");
        }

        var page = document.GetPage(1);
        var pageImages = page.GetImages().ToList();

        if (pageImages.Count == 0)
        {
            return RegionExtractionResult.Failure(
                "PDF page 1 contains no embedded images. " +
                "Only scanned PDFs (image-based) are supported.");
        }

        // Use the first decodable embedded image as the page bitmap.
        GrayImage? pageImage = null;
        foreach (var pdfImage in pageImages)
        {
            pageImage = TryLoadImage(pdfImage);
            if (pageImage is not null)
            {
                break;
            }
        }

        if (pageImage is null)
        {
            return RegionExtractionResult.Failure(
                "Could not decode any embedded image from PDF page 1.");
        }

        return CropRegions(pageImage);
    }

    private static GrayImage? TryLoadImage(UglyToad.PdfPig.Content.IPdfImage pdfImage)
    {
        // Attempt PNG extraction first (lossless).
        if (pdfImage.TryGetPng(out var pngBytes) && pngBytes is { Length: > 0 } &&
            TryDecode(pngBytes, out var fromPng))
        {
            return fromPng;
        }

        // Fallback: treat raw bytes as the encoded image (common for scanned PDFs).
        var raw = pdfImage.RawBytes.ToArray();
        return raw.Length > 0 && TryDecode(raw, out var fromRaw) ? fromRaw : null;
    }

    private static bool TryDecode(byte[] bytes, out GrayImage? image)
    {
        try
        {
            image = GrayImage.Load(bytes);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            image = null;
            return false;
        }
    }

    private RegionExtractionResult CropRegions(GrayImage page)
    {
        var regionMap = BuildRegionMap();
        var croppedRegions = new List<CroppedRegion>(regionMap.Count);

        foreach (var (region, bounds) in regionMap)
        {
            var rect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);

            // Clamp to page dimensions to avoid an out-of-bounds crop.
            rect = ClampToPage(rect, page.Width, page.Height);

            var cropped = page.Crop(rect);

            croppedRegions.Add(new CroppedRegion(
                region,
                cropped.Width,
                cropped.Height,
                cropped.ToGray8Bytes(),
                new PixelBounds(rect.X, rect.Y, rect.Width, rect.Height)));
        }

        return RegionExtractionResult.Success(new CroppedFormRegions(croppedRegions));
    }

    private Dictionary<FormRegion, PdfRegionBounds> BuildRegionMap() =>
        new()
        {
            [FormRegion.TeamId] = _options.TeamId,
            [FormRegion.TeamIdDigit1] = _options.TeamIdDigit1,
            [FormRegion.TeamIdDigit2] = _options.TeamIdDigit2,
            [FormRegion.Score] = _options.Score,
            [FormRegion.ScoreDigit1] = _options.ScoreDigit1,
            [FormRegion.ScoreDigit2] = _options.ScoreDigit2,
            [FormRegion.ScoreDigit3] = _options.ScoreDigit3,
        };

    private static Rectangle ClampToPage(Rectangle rect, int pageWidth, int pageHeight)
    {
        var x = Math.Max(0, Math.Min(rect.X, pageWidth - 1));
        var y = Math.Max(0, Math.Min(rect.Y, pageHeight - 1));
        var w = Math.Max(1, Math.Min(rect.Width, pageWidth - x));
        var h = Math.Max(1, Math.Min(rect.Height, pageHeight - y));
        return new Rectangle(x, y, w, h);
    }
}

namespace VisionCore.Domain.Imaging.Cropping;

using VisionCore.Domain.Imaging.Layout;

public sealed class CroppedFormRegions
{
    private readonly IReadOnlyDictionary<FormRegion, CroppedRegion> _regions;

    public CroppedFormRegions(IReadOnlyCollection<CroppedRegion> regions)
    {
        _regions = BuildRegionMap(regions);
        Regions = _regions.Values.ToList().AsReadOnly();
    }

    public IReadOnlyCollection<CroppedRegion> Regions { get; }

    public bool Contains(FormRegion region) => _regions.ContainsKey(region);

    public CroppedRegion GetRegion(FormRegion region)
    {
        return _regions[region];
    }

    private static IReadOnlyDictionary<FormRegion, CroppedRegion> BuildRegionMap(
        IReadOnlyCollection<CroppedRegion> regions)
    {
        if (regions.Count == 0)
        {
            throw new InvalidOperationException("At least one cropped region is required.");
        }

        var duplicates = regions
            .GroupBy(region => region.Region)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException("Duplicate cropped regions detected.");
        }

        return regions.ToDictionary(region => region.Region);
    }
}

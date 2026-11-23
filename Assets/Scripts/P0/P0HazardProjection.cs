using System;
using System.Collections.Generic;
using System.Linq;

public sealed class P0HazardProjectionResult
{
    public IReadOnlyList<P0HazardSource> items;
    public IReadOnlyDictionary<string, int> byLevel;
    public int total;
    public int unlocated;
}

public static class P0HazardProjection
{
    public static P0HazardProjectionResult ForCompany(
        IEnumerable<P0HazardSource> hazards,
        string companyId,
        string sysOrgCode)
    {
        var unique = new Dictionary<string, P0HazardSource>(StringComparer.Ordinal);
        foreach (P0HazardSource hazard in hazards ?? Enumerable.Empty<P0HazardSource>())
        {
            if (hazard == null || string.IsNullOrWhiteSpace(hazard.hazardId)) continue;
            if (!string.Equals(hazard.companyId, companyId, StringComparison.Ordinal)) continue;
            if (!string.Equals(hazard.sysOrgCode, sysOrgCode, StringComparison.Ordinal)) continue;
            if (!unique.ContainsKey(hazard.hazardId)) unique.Add(hazard.hazardId, hazard);
        }

        List<P0HazardSource> items = unique.Values.OrderBy(item => item.hazardId).ToList();
        Dictionary<string, int> byLevel = items
            .GroupBy(item => string.IsNullOrWhiteSpace(item.level) ? "未分级" : item.level)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return new P0HazardProjectionResult
        {
            items = items.AsReadOnly(),
            byLevel = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(byLevel),
            total = items.Count,
            unlocated = items.Count(item => !ValidCoordinates(item.longitude, item.latitude))
        };
    }

    public static bool ValidCoordinates(double longitude, double latitude)
    {
        return !double.IsNaN(longitude) && !double.IsNaN(latitude) &&
               !double.IsInfinity(longitude) && !double.IsInfinity(latitude) &&
               Math.Abs(longitude) <= 180 && Math.Abs(latitude) <= 90 &&
               longitude != 0 && latitude != 0;
    }
}

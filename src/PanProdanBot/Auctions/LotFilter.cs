using Microsoft.Extensions.Options;

namespace PanProdanBot.Auctions;

/// <summary>Region + price filter: a lot passes when its start price is below the limit for its region.</summary>
public sealed class LotFilter(IOptions<MonitorOptions> options)
{
    private readonly MonitorOptions _o = options.Value;

    /// <summary>Highest limit of all rules — anything at or above it can be rejected without knowing the region.</summary>
    public decimal MaxLimit => _o.RegionRules.Select(r => r.MaxPrice).Append(_o.DefaultMaxPrice).Max();

    public decimal LimitFor(string? region)
    {
        if (!string.IsNullOrWhiteSpace(region))
            foreach (var rule in _o.RegionRules)
                if (rule.Match.Any(m => region.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    return rule.MaxPrice;
        return _o.DefaultMaxPrice;
    }

    public bool Passes(string? region, decimal price) => price < LimitFor(region);

    /// <summary>
    /// Decide from list data only: true/false when certain, null when details are needed.
    /// </summary>
    public bool? PreCheck(LotSummary s)
    {
        if (s.Price is not { } price) return null;
        if (price >= MaxLimit) return false;
        if (s.Region is not null) return Passes(s.Region, price);
        return price < _o.DefaultMaxPrice ? true : null; // cheap everywhere → passes; otherwise region decides
    }
}

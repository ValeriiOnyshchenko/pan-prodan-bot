using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PanProdanBot.Auctions;

/// <summary>
/// Region + price filter (start price must be below the limit for the lot's region)
/// and the partial-lot filter (skip lots that are only a share of an object).
/// </summary>
public sealed partial class LotFilter(IOptions<MonitorOptions> options)
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

    public bool Passes(Lot lot) => lot.Price is { } p && Passes(lot.Region, p) && !(_o.SkipPartialLots && IsPartial(lot.Title));

    /// <summary>
    /// True when the title says the lot is only a share of an object: «1/2 частка», «1/3 частини», «1/2 ч.»,
    /// «7/25 часток», «14/100 ід. частини», or a fraction right before the object («1/2 квартири»).
    /// Fractions in addresses («буд. 5/7», «кв. 167/1») and phrases like «що становить 1/10 частину»
    /// (a separate premise described relative to the building) are not treated as partial.
    /// </summary>
    public static bool IsPartial(string? title) => title is not null && PartialRegex().IsMatch(title);

    /// <summary>
    /// Decide from list data only: false = certainly rejected, null = details are needed.
    /// </summary>
    public bool? PreCheck(LotSummary s)
    {
        if (_o.SkipPartialLots && IsPartial(s.Title)) return false;
        if (s.Price is not { } price) return null;
        if (price >= MaxLimit) return false;
        if (s.Region is not null && !Passes(s.Region, price)) return false;
        return null;
    }

    [GeneratedRegex("""
        (?<!(?:становить|складає|складають)\s*)   # "що становить 1/10 частину" → a separate premise, not a share
        (?<![\d/])\d+\s*/\s*\d+(?![\d/])\s*        # the fraction itself: 1/2, 49/50, 943/1000
        (?:ід\.\s*|ідеальн\w*\s+)?                 # optional "ід." / "ідеальна"
        (?:
            частк\w* | часток | частин\w* | ч\.      # частка, частки, частку, часток, частина, частини, частину, частин, ч.
          | (?=(?:квартир|будин|будівл|приміщ|нежитл|нежил|житлов|\w*кімнатн|кімнат|домоволод|земельн|гараж|споруд))
        )
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex PartialRegex();
}

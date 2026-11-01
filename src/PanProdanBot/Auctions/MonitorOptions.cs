namespace PanProdanBot.Auctions;

public sealed class MonitorOptions
{
    public const string Section = "Monitor";

    public bool Enabled { get; set; } = true;

    /// <summary>How often Пан Продан checks the auctions. Aligned to the clock: 60 → every hour at xx:00.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Sites are read this many minutes before the scheduled time, so the messages
    /// go out to everyone exactly at the start of the hour (xx:00).
    /// </summary>
    public int PrefetchMinutes { get; set; } = 5;

    /// <summary>Skip lots that are only a share of an object, e.g. «1/2 частка квартири».</summary>
    public bool SkipPartialLots { get; set; } = true;

    /// <summary>Pause between HTTP requests to the same site — be polite.</summary>
    public int RequestDelayMs { get; set; } = 1000;

    /// <summary>
    /// Only lots published within this many hours are sent; older ones are remembered as "stale" and skipped.
    /// Protects subscribers from receiving history after an outage or a stuck connection. 0 = no limit.
    /// </summary>
    public int MaxLotAgeHours { get; set; } = 24;

    /// <summary>Max photos per lot message (Telegram album limit is 10).</summary>
    public int MaxImages { get; set; } = 6;

    /// <summary>Safety cap: max lots broadcast per check. The rest wait for the next check.</summary>
    public int MaxLotsPerRun { get; set; } = 30;

    /// <summary>
    /// On the very first check of a source all existing lots are remembered silently (no flood).
    /// This many matching lots are still sent as a sample, so subscribers see the bot working.
    /// </summary>
    public int FirstRunSampleCount { get; set; } = 1;

    public string GalcontractListUrl { get; set; } =
        "https://sales.tsbgalcontract.org.ua/PositionList.aspx?&page=1&rows=1000&sidx=3&sord=0&filter_type=filter&fvAuctionDir_0=small_privatization&action=Y&status=all";

    public string SetamListUrl { get; set; } = "https://setam.net.ua/neruhomist";

    /// <summary>
    /// Real estate only: allowed classifier code prefixes of a lot's main item (Prozorro.Sale: 04 = нерухомість,
    /// 06 = земельні ділянки, 70 = продаж нерухомості). Galcontract's small privatization also sells animals,
    /// cars, shares etc. — those are skipped. Lots without a code (SETAM, already real-estate only) always pass.
    /// Defined in appsettings.json (lists must not have C# defaults — the binder appends).
    /// </summary>
    public List<string> RealEstateCodePrefixes { get; set; } = [];

    /// <summary>false = lots from regions without a rule are not sent at all.</summary>
    public bool IncludeOtherRegions { get; set; } = true;

    /// <summary>Price limit (UAH, exclusive) for regions without a specific rule (if <see cref="IncludeOtherRegions"/>).</summary>
    public decimal DefaultMaxPrice { get; set; } = 1_000_000;

    /// <summary>Region-specific limits. Defined in appsettings.json (lists must not have C# defaults — the binder appends).</summary>
    public List<RegionRule> RegionRules { get; set; } = [];
}

public sealed class RegionRule
{
    /// <summary>Ukrainian display name for messages, e.g. "Львівська область".</summary>
    public string Name { get; set; } = "";

    /// <summary>Case-insensitive substrings matched against the lot's region, e.g. "Львів".</summary>
    public List<string> Match { get; set; } = [];

    public decimal MaxPrice { get; set; }
}

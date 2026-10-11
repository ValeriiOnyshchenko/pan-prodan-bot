namespace PanProdanBot.Auctions;

/// <summary>A lot as seen on a list page — just enough to detect new lots and pre-filter cheaply.</summary>
/// <param name="Key">Globally unique key, e.g. <c>setam:601644</c>.</param>
/// <param name="Region">Region if the list page shows it, otherwise null (resolved from details).</param>
/// <param name="Price">Start price if the list page shows it.</param>
/// <param name="DetailsId">Source-specific id used to load details.</param>
public sealed record LotSummary(string Key, string DetailsId, string? Region, decimal? Price);

/// <summary>Full lot information used for filtering and for the Telegram message.</summary>
public sealed class Lot
{
    public required string Key { get; init; }
    public required string SourceName { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }
    public string? Region { get; init; }
    public decimal? Price { get; init; }
    /// <summary>true = price includes VAT, false = VAT is added on top, null = unknown / not applicable.</summary>
    public bool? VatIncluded { get; init; }
    public decimal? Guarantee { get; init; }
    public string? AuctionType { get; init; }
    public string? Address { get; init; }
    public string? Area { get; init; }
    public string? Category { get; init; }
    public string? ApplicationsDeadline { get; init; }
    /// <summary>Applications deadline as a timestamp — used to skip outdated lots.</summary>
    public DateTimeOffset? DeadlineUtc { get; init; }
    public string? AuctionDate { get; init; }
    public string? Warning { get; init; }
    public string? Description { get; init; }
    public string? MapUrl { get; init; }
    public IReadOnlyList<string> ImageUrls { get; init; } = [];
}

public interface ILotSource
{
    /// <summary>Stable id used in storage, e.g. "setam".</summary>
    string Id { get; }

    /// <summary>Human-readable Ukrainian name, e.g. "СЕТАМ".</summary>
    string Name { get; }

    /// <summary>All lots that currently accept applications.</summary>
    Task<IReadOnlyList<LotSummary>> ListActiveAsync(CancellationToken ct);

    Task<Lot> GetLotAsync(LotSummary summary, CancellationToken ct);
}

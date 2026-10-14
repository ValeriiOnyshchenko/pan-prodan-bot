using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;

namespace PanProdanBot.Auctions.Sources;

/// <summary>
/// Біржа «Галконтракт», мала приватизація.
/// The list page gives ids + price + status; details come from the public Prozorro.Sale API (clean JSON).
/// </summary>
public sealed partial class GalcontractSource(HttpClient http, IOptions<MonitorOptions> options, ILogger<GalcontractSource> log)
    : ILotSource
{
    private const string SiteUrl = "https://sales.tsbgalcontract.org.ua";
    private const string ApiUrl = "https://procedure.prozorro.sale/api";

    public string Id => "galcontract";
    public string Name => "Біржа «Галконтракт»";

    public async Task<IReadOnlyList<LotSummary>> ListActiveAsync(CancellationToken ct)
    {
        var html = await http.GetStringAsync(options.Value.GalcontractListUrl, ct);
        var doc = await new HtmlParser().ParseDocumentAsync(html, ct);
        var result = new List<LotSummary>();

        foreach (var card in doc.QuerySelectorAll("div[id^='pn_Record_']"))
        {
            var procedureId = card.Id!["pn_Record_".Length..];
            if (!HexId().IsMatch(procedureId)) continue; // skips the JS template card

            var status = Text.Clean(card.QuerySelector("span.fw-light")?.TextContent);
            if (!status.Contains("Прийняття заяв", StringComparison.OrdinalIgnoreCase)) continue;

            var auctionId = Text.Clean(card.QuerySelector("span.text-primary.small")?.TextContent);
            if (auctionId.Length == 0) continue;

            var price = Text.ParsePrice(card.QuerySelector("span.h6")?.TextContent);
            var title = Text.Clean(card.QuerySelector("a.h4")?.TextContent);
            result.Add(new LotSummary($"{Id}:{auctionId}", procedureId, Region: null, price, title));
        }

        log.LogInformation("Galcontract: {Count} lots accept applications", result.Count);
        return result;
    }

    public async Task<Lot> GetLotAsync(LotSummary summary, CancellationToken ct)
    {
        await using var stream = await http.GetStreamAsync($"{ApiUrl}/procedures/{summary.DetailsId}", ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var p = json.RootElement;

        var auctionId = Str(p, "auctionId") ?? summary.Key.Split(':', 2)[1];
        var items = p.TryGetProperty("items", out var it) ? it.EnumerateArray().ToList() : [];
        var first = items.FirstOrDefault();
        var address = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("address", out var a) ? a : default;

        var region = Uk(address, "region");
        var addressText = BuildAddress(region, Uk(address, "locality"), Uk(address, "streetAddress"));

        var title = Text.Clean(Uk(p, "title"));
        var description = Text.CleanMultiline(Uk(p, "description"));
        if (description.Length == 0 && first.ValueKind == JsonValueKind.Object) description = Text.CleanMultiline(Uk(first, "description"));
        if (description == title) description = "";

        bool? vatIncluded = null;
        if (p.TryGetProperty("value", out var value) && value.TryGetProperty("valueAddedTaxIncluded", out var vi))
        {
            var charged = p.TryGetProperty("valueAddedTaxCharged", out var vc) && vc.ValueKind == JsonValueKind.True;
            vatIncluded = vi.ValueKind == JsonValueKind.True ? true : charged ? false : null;
        }

        var images = p.TryGetProperty("documents", out var docs)
            ? docs.EnumerateArray()
                .Where(d => Str(d, "documentType") == "illustration")
                .Select(d => Str(d, "url")?.Split('/').Last())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .Take(options.Value.MaxImages)
                .Select(id => $"{ApiUrl}/thumbnails/public/{id}?fit=contain&cbg=white&w=1280&h=1280&output=jpeg")
                .ToList()
            : [];

        string? mapUrl = null;
        if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("location", out var loc)
            && Str(loc, "latitude") is { } lat && Str(loc, "longitude") is { } lon)
            mapUrl = $"https://www.google.com/maps?q={lat},{lon}";

        return new Lot
        {
            Key = summary.Key,
            SourceName = Name,
            Url = $"{SiteUrl}/auction/{auctionId}",
            Title = title.Length > 0 ? title : auctionId,
            Region = region,
            Price = Amount(p, "value") ?? summary.Price,
            VatIncluded = vatIncluded,
            Guarantee = Amount(p, "guarantee"),
            AuctionType = AuctionType(Str(p, "sellingMethod")),
            Address = addressText.Length > 0 ? addressText : null,
            Area = Area(items),
            Category = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("classification", out var cls) ? Text.Clean(Uk(cls, "description")) : null,
            ApplicationsDeadline = Text.KyivTime(Period(p, "tenderPeriod", "endDate")),
            DeadlineUtc = Text.ParseIso(Period(p, "tenderPeriod", "endDate")),
            AuctionDate = Text.KyivTime(Period(p, "auctionPeriod", "startDate")),
            Description = description,
            MapUrl = mapUrl,
            ImageUrls = images,
        };
    }

    /// <summary>"Вінницька область, ПОЛІТАНКИ, , Політанки, ..." → "Вінницька область, Політанки, ..." (no duplicates).</summary>
    private static string BuildAddress(params string?[] parts)
    {
        var result = new List<string>();
        foreach (var piece in parts.SelectMany(p => (p ?? "").Split(',')).Select(x => Text.Clean(x)).Where(x => x.Length > 0))
        {
            var dup = result.FindIndex(r => r.Contains(piece, StringComparison.OrdinalIgnoreCase) || piece.Contains(r, StringComparison.OrdinalIgnoreCase));
            if (dup < 0) result.Add(piece);
            else if (piece.Length > result[dup].Length || result[dup].ToUpperInvariant() == result[dup]) result[dup] = piece; // prefer the longer / non-CAPS form
        }
        return string.Join(", ", result);
    }

    private static string? AuctionType(string? sellingMethod) => sellingMethod switch
    {
        null => null,
        _ when sellingMethod.Contains("dutch") => "Голландський аукціон (ціна знижується)",
        _ when sellingMethod.Contains("english") => "Англійський аукціон (ціна зростає)",
        _ => null,
    };

    private static string? Area(List<JsonElement> items)
    {
        decimal total = 0;
        foreach (var i in items)
            if (Uk(i, "unit") is { } unit && unit.Contains("метр", StringComparison.OrdinalIgnoreCase)
                && i.TryGetProperty("quantity", out var q) && q.TryGetDecimal(out var v))
                total += v;
        return total > 0 ? total.ToString("0.##", new System.Globalization.NumberFormatInfo { NumberDecimalSeparator = "," }) + " м²" : null;
    }

    private static decimal? Amount(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetProperty("amount", out var a) && a.TryGetDecimal(out var d) ? d : null;

    private static string? Period(JsonElement e, string name, string field) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object ? Str(p, field) : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Reads a Prozorro localized field: either { "uk_UA": "..." } or { "name": { "uk_UA": "..." } }.</summary>
    private static string? Uk(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind != JsonValueKind.Object) return null;
        if (v.TryGetProperty("uk_UA", out var uk)) return uk.GetString();
        return v.TryGetProperty("name", out var n) && n.TryGetProperty("uk_UA", out var nuk) ? nuk.GetString() : null;
    }

    [GeneratedRegex("^[0-9a-f]{24}$")] private static partial Regex HexId();
}

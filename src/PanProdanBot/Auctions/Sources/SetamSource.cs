using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;

namespace PanProdanBot.Auctions.Sources;

/// <summary>
/// ДП «СЕТАМ», розділ «Нерухомість». The list shows only upcoming auctions (registration open),
/// 20 per page, with region and price — so most lots are filtered without opening details.
/// </summary>
public sealed partial class SetamSource(HttpClient http, IOptions<MonitorOptions> options, ILogger<SetamSource> log)
    : ILotSource
{
    private const string SiteUrl = "https://setam.net.ua";
    private const int MaxPages = 50;

    public string Id => "setam";
    public string Name => "СЕТАМ";

    public async Task<IReadOnlyList<LotSummary>> ListActiveAsync(CancellationToken ct)
    {
        var parser = new HtmlParser();
        var result = new Dictionary<string, LotSummary>();
        var baseUrl = options.Value.SetamListUrl.TrimEnd('/');
        int? total = null;

        for (var page = 1; page <= MaxPages; page++)
        {
            if (page > 1) await Task.Delay(options.Value.RequestDelayMs, ct);

            var html = await http.GetStringAsync(page == 1 ? baseUrl : $"{baseUrl}/page/{page}", ct);
            var doc = await parser.ParseDocumentAsync(html, ct);
            total ??= int.TryParse(TotalRegex().Match(doc.Body?.TextContent ?? "").Groups[1].Value, out var t) ? t : null;

            var cards = doc.QuerySelectorAll("#upcoming .auctions-item");
            foreach (var card in cards)
            {
                var id = AuctionIdRegex().Match(card.QuerySelector("a[href*='/auction/']")?.GetAttribute("href") ?? "").Groups[1].Value;
                if (id.Length == 0) continue;

                var status = Text.Clean(card.QuerySelector(".condition-item span")?.TextContent);
                if (status.Length > 0 && !status.Contains("Реєстрація", StringComparison.OrdinalIgnoreCase)) continue;

                var region = Text.Clean(card.QuerySelector(".region-item span")?.TextContent);
                var price = Text.ParsePrice(card.QuerySelector(".start-price-item [itemprop='price']")?.TextContent);
                result.TryAdd(id, new LotSummary($"{Id}:{id}", id, region.Length > 0 ? region : null, price));
            }

            if (cards.Length == 0 || (total is { } n && page * 20 >= n)) break;
        }

        log.LogInformation("SETAM: {Count} lots with open registration (site says {Total})", result.Count, total);
        // Lot ids grow over time → newest first (the site itself sorts by price).
        return result.Values.OrderByDescending(l => long.TryParse(l.DetailsId, out var n) ? n : 0).ToList();
    }

    public async Task<Lot> GetLotAsync(LotSummary summary, CancellationToken ct)
    {
        var url = $"{SiteUrl}/auction/{summary.DetailsId}";
        var html = await http.GetStringAsync(url, ct);
        var doc = await new HtmlParser().ParseDocumentAsync(html, ct);

        // "Label: value" rows in the info box.
        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in doc.QuerySelectorAll(".info-box > div"))
        {
            var label = Text.Clean(row.ChildNodes.Where(n => n.NodeType == NodeType.Text).Select(n => n.TextContent).FirstOrDefault(t => t.Contains(':'))).TrimEnd(':', ' ');
            var value = Text.Clean(row.QuerySelector("span")?.TextContent);
            if (label.Length > 0 && value.Length > 0) rows.TryAdd(label, value);
        }

        var priceRow = rows.FirstOrDefault(r => r.Key.StartsWith("Стартова ціна", StringComparison.OrdinalIgnoreCase));
        var info = doc.QuerySelector("#additional-nformation");
        var category = Regex.Match(Text.Clean(info?.TextContent), @"Категорія:\s*(.+?)\s*(Відомості|$)").Groups[1].Value;
        var encumbrance = Text.Clean(Regex.Match(info?.TextContent ?? "", @"Відомості про обтяження майна:(.*)", RegexOptions.Singleline).Groups[1].Value);

        var title = Text.Clean(doc.QuerySelector("meta[name='og:title']")?.GetAttribute("content"));
        if (title.Length == 0) title = Text.Clean(doc.QuerySelector("h1, h2")?.TextContent);

        return new Lot
        {
            Key = summary.Key,
            SourceName = Name,
            Url = url,
            Title = title.Length > 0 ? title : $"Лот {summary.DetailsId}",
            Region = rows.GetValueOrDefault("Область") ?? summary.Region,
            Price = Text.ParsePrice(priceRow.Value) ?? summary.Price,
            VatIncluded = priceRow.Key?.Contains("ПДВ", StringComparison.OrdinalIgnoreCase) == true ? true : null,
            Guarantee = Text.ParsePrice(rows.GetValueOrDefault("Гарантійний внесок")),
            AuctionType = "Електронні торги СЕТАМ",
            Address = rows.GetValueOrDefault("Місцезнаходження майна"),
            Category = category.Length > 0 ? category : null,
            ApplicationsDeadline = rows.GetValueOrDefault("Дата закінчення подання заявок"),
            DeadlineUtc = Text.ParseUaDate(rows.GetValueOrDefault("Дата закінчення подання заявок")),
            AuctionDate = rows.GetValueOrDefault("Дата проведення аукціону"),
            Warning = encumbrance.Length > 0 ? "Є обтяження (арешт тощо) — деталі на сторінці лоту" : null,
            Description = Description(doc),
            ImageUrls = Images(doc).Take(options.Value.MaxImages).ToList(),
        };
    }

    /// <summary>Non-empty characteristics + description paragraphs from the "Характеристика лоту" tab.</summary>
    private static string Description(IDocument doc)
    {
        var tab = doc.QuerySelector("#Feature-lot");
        if (tab is null) return "";

        var lines = new List<string>();
        foreach (var f in tab.QuerySelectorAll(".user-field"))
        {
            var label = Text.Clean(f.QuerySelector(".user-field-label")?.TextContent);
            var value = Text.Clean(f.QuerySelector(".user-field-value")?.TextContent);
            if (label.Length is > 0 and <= 60 && value.Length > 0 && label != "Провадження") lines.Add($"{label}: {value}");
        }
        lines.AddRange(tab.QuerySelectorAll("p").Where(p => p.QuerySelector("p") is null)
            .Select(p => Text.Clean(p.TextContent)).Where(t => t.Length > 0));

        return string.Join("\n", lines.Distinct());
    }

    private static IEnumerable<string> Images(IDocument doc) =>
        doc.QuerySelectorAll("img[src*='/images/users/']")
            .Select(i => i.GetAttribute("src")!)
            .Where(src => !src.Contains("thumbnail", StringComparison.OrdinalIgnoreCase))
            .Select(src => src.StartsWith("http") ? src : SiteUrl + src)
            .Distinct();

    [GeneratedRegex(@"із\s+(\d+)\s+аукціон")] private static partial Regex TotalRegex();
    [GeneratedRegex(@"/auction/(\d+)")] private static partial Regex AuctionIdRegex();
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace PanProdanBot.Auctions;

/// <summary>Small parsing/formatting helpers shared by sources and the message formatter.</summary>
public static partial class Text
{
    private static readonly NumberFormatInfo UaMoney = new()
    {
        NumberGroupSeparator = " ", // narrow no-break space
        NumberDecimalSeparator = ",",
    };

    private static readonly TimeZoneInfo Kyiv = FindKyiv();

    /// <summary>"15 580 800.00" / "1 400 000,50" → 15580800.00</summary>
    public static decimal? ParsePrice(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = NonNumber().Replace(s, "").Replace(',', '.');
        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>1400000 → "1 400 000 грн", 853.48 → "853,48 грн"</summary>
    public static string Money(decimal v) =>
        v.ToString(v == decimal.Truncate(v) ? "#,0" : "#,0.00", UaMoney) + " грн";

    /// <summary>ISO UTC timestamp → "20.10.2026 12:30" Kyiv time.</summary>
    public static string? KyivTime(string? iso)
    {
        if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)) return null;
        return TimeZoneInfo.ConvertTime(dto, Kyiv).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private static readonly string[] UaMonths =
        ["січня", "лютого", "березня", "квітня", "травня", "червня", "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"];

    /// <summary>ISO timestamp → DateTimeOffset.</summary>
    public static DateTimeOffset? ParseIso(string? iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto) ? dto : null;

    /// <summary>"29 Жовтня 2026 08:00" (Kyiv time) → DateTimeOffset.</summary>
    public static DateTimeOffset? ParseUaDate(string? s)
    {
        var m = UaDate().Match(s ?? "");
        if (!m.Success) return null;
        var month = Array.IndexOf(UaMonths, m.Groups[2].Value.ToLowerInvariant()) + 1;
        if (month == 0) return null;
        var local = new DateTime(int.Parse(m.Groups[3].Value), month, int.Parse(m.Groups[1].Value),
            m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 23, m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : 59, 0);
        return new DateTimeOffset(local, Kyiv.GetUtcOffset(local));
    }

    /// <summary>Collapse whitespace and trim.</summary>
    public static string Clean(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : Spaces().Replace(s.Replace(' ', ' '), " ").Trim();

    /// <summary>Like <see cref="Clean"/> but keeps paragraph breaks.</summary>
    public static string CleanMultiline(string? s) =>
        string.Join("\n", (s ?? "").Split('\n').Select(Clean).Where(l => l.Length > 0));

    public static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        var cut = s.LastIndexOf(' ', Math.Max(0, max - 1), Math.Min(max, 80));
        return s[..(cut > 0 ? cut : max - 1)].TrimEnd(',', ';', ' ') + "…";
    }

    private static TimeZoneInfo FindKyiv()
    {
        foreach (var id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* try next */ }
        return TimeZoneInfo.CreateCustomTimeZone("Kyiv", TimeSpan.FromHours(2), "Kyiv", "Kyiv");
    }

    [GeneratedRegex(@"(\d{1,2})\s+(\p{L}+)\s+(\d{4})(?:\s+(\d{1,2}):(\d{2}))?")] private static partial Regex UaDate();
    [GeneratedRegex(@"[^\d.,]")] private static partial Regex NonNumber();
    [GeneratedRegex(@"[ \t\r\f\v]+")] private static partial Regex Spaces();
}

using System.Text;
using System.Text.RegularExpressions;

namespace PanProdanBot.Auctions;

/// <summary>Builds Пан Продан's Ukrainian lot message (Telegram HTML).</summary>
public static partial class LotFormatter
{
    /// <summary>Telegram limit for a photo/album caption (visible characters).</summary>
    public const int CaptionLimit = 1024;

    /// <summary>Telegram limit for a text message.</summary>
    public const int TextLimit = 4096;

    private const int MaxDescription = 1200; // keep text-only messages readable; full text is one tap away

    /// <param name="limit">Max visible length: <see cref="CaptionLimit"/> when sent under photos, <see cref="TextLimit"/> otherwise.</param>
    public static string Format(Lot lot, int limit = CaptionLimit)
    {
        var compact = limit <= CaptionLimit;

        var head = new StringBuilder();
        head.Append("🏠 <b>").Append(H(Text.Truncate(lot.Title, compact ? 220 : 350))).Append("</b>\n\n");

        if (lot.Price is { } price)
        {
            head.Append("💰 Стартова ціна: <b>").Append(Text.Money(price)).Append("</b>");
            head.Append(lot.VatIncluded switch { true => " (з ПДВ)", false => " (+ ПДВ)", _ => "" }).Append('\n');
        }
        Line(head, "🛡", "Гарантійний внесок", lot.Guarantee is { } g ? Text.Money(g) : null);
        Line(head, "🔨", null, lot.AuctionType);
        Line(head, "📍", null, lot.Address is { } a ? Text.Truncate(a, compact ? 160 : 300) : lot.Region);
        Line(head, "📐", "Площа", lot.Area);
        Line(head, "🏷", null, lot.Category is { } c ? Text.Truncate(c, compact ? 70 : 120) : null);
        Line(head, "⏳", "Заявки до", lot.ApplicationsDeadline);
        Line(head, "📅", "Аукціон", lot.AuctionDate);
        Line(head, "⚠️", null, lot.Warning);

        var tail = new StringBuilder("\n");
        if (lot.MapUrl is not null) tail.Append("🗺 <a href=\"").Append(H(lot.MapUrl)).Append("\">Карта</a> · ");
        tail.Append("🔗 <a href=\"").Append(H(lot.Url)).Append("\">Відкрити лот</a> · ").Append(H(lot.SourceName));

        var body = "";
        if (!string.IsNullOrWhiteSpace(lot.Description))
        {
            // Room left for the description, counted in visible characters (tags and links don't count).
            var room = Math.Min(MaxDescription, limit - Visible(head.ToString()) - Visible(tail.ToString()) - 8);
            if (room >= 80) body = "\n📝 " + H(Text.Truncate(lot.Description, room)) + "\n";
        }

        return head + body + tail;
    }

    /// <summary>Length as Telegram counts it: without HTML tags, entities decoded.</summary>
    public static int Visible(string html) =>
        Tags().Replace(html, "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&").Length;

    private static void Line(StringBuilder sb, string icon, string? label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append(icon).Append(' ');
        if (label is not null) sb.Append(label).Append(": ");
        sb.Append(H(value)).Append('\n');
    }

    /// <summary>Escape only what Telegram HTML requires.</summary>
    private static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
}

using System.Text;

namespace PanProdanBot.Auctions;

/// <summary>Builds Пан Продан's Ukrainian message (Telegram HTML, ≤ 4096 chars).</summary>
public static class LotFormatter
{
    private const int MaxLength = 4000;
    private const int MaxDescription = 1200; // keep messages readable; full text is one tap away

    public static string Format(Lot lot)
    {
        var head = new StringBuilder();
        head.Append("🏠 <b>").Append(H(Text.Truncate(lot.Title, 350))).Append("</b>\n\n");

        if (lot.Price is { } price)
        {
            head.Append("💰 Стартова ціна: <b>").Append(Text.Money(price)).Append("</b>");
            head.Append(lot.VatIncluded switch { true => " (з ПДВ)", false => " (+ ПДВ)", _ => "" }).Append('\n');
        }
        Line(head, "🛡", "Гарантійний внесок", lot.Guarantee is { } g ? Text.Money(g) : null);
        Line(head, "🔨", null, lot.AuctionType);
        Line(head, "📍", null, lot.Address ?? lot.Region);
        Line(head, "📐", "Площа", lot.Area);
        Line(head, "🏷", null, lot.Category is { } c ? Text.Truncate(c, 120) : null);
        Line(head, "⏳", "Заявки до", lot.ApplicationsDeadline);
        Line(head, "📅", "Аукціон", lot.AuctionDate);
        Line(head, "⚠️", null, lot.Warning);

        var tail = new StringBuilder("\n");
        if (lot.MapUrl is not null) tail.Append("🗺 <a href=\"").Append(H(lot.MapUrl)).Append("\">Показати на карті</a>\n");
        tail.Append("🔗 <a href=\"").Append(H(lot.Url)).Append("\">Сторінка лоту</a> · ").Append(H(lot.SourceName));

        var body = "";
        if (!string.IsNullOrWhiteSpace(lot.Description))
        {
            var room = Math.Min(MaxDescription, MaxLength - head.Length - tail.Length - 10);
            if (room > 100) body = "\n📝 " + H(Text.Truncate(lot.Description, room)) + "\n";
        }

        return head + body + tail;
    }

    private static void Line(StringBuilder sb, string icon, string? label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append(icon).Append(' ');
        if (label is not null) sb.Append(label).Append(": ");
        sb.Append(H(value)).Append('\n');
    }

    /// <summary>Escape only what Telegram HTML requires.</summary>
    private static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

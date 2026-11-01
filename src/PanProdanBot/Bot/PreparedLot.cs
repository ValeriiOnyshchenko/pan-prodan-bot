using PanProdanBot.Auctions;
using Telegram.Bot.Types.ReplyMarkups;

namespace PanProdanBot.Bot;

/// <summary>Lot ready to send: texts, button and photos (bytes for the first upload, then Telegram file ids).</summary>
public sealed class PreparedLot
{
    public required Lot Lot { get; init; }
    public required string Caption { get; init; }   // under photos, ≤ 1024 chars
    public required string Text { get; init; }      // text-only fallback, ≤ 4096 chars
    public required InlineKeyboardMarkup Button { get; init; }
    public List<byte[]> Photos { get; init; } = [];
    public string[]? FileIds { get; set; }

    /// <summary>When nothing could be downloaded, Telegram is asked to fetch these URLs itself.</summary>
    public IReadOnlyList<string> PhotoUrls => Photos.Count == 0 && FileIds is null ? Lot.ImageUrls : [];

    public int PhotoCount => FileIds?.Length ?? (Photos.Count > 0 ? Photos.Count : PhotoUrls.Count);
}

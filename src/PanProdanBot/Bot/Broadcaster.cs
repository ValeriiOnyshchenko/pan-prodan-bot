using Microsoft.Extensions.Options;
using PanProdanBot.Auctions;
using PanProdanBot.Storage;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PanProdanBot.Bot;

/// <summary>Sends messages to every subscriber, respecting Telegram's limits (~30 msg/sec overall).</summary>
public sealed class Broadcaster(
    ITelegramBotClient bot,
    SubscriberStore store,
    LatestLotStore latest,
    LotFilter filter,
    IHttpClientFactory httpFactory,
    ILogger<Broadcaster> log)
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(40);
    private const int MaxPhotoBytes = 10 * 1024 * 1024;

    public Task<int> BroadcastAsync(string text, CancellationToken ct = default) =>
        SendToAllAsync(chatId => bot.SendMessage(chatId, text, cancellationToken: ct), 1, ct);

    /// <summary>Sends the lot to all subscribers and remembers it as the latest lot of its source.</summary>
    public async Task<int> BroadcastLotAsync(Lot lot, CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(lot, fileIds: null, ct);
        var sent = await SendToAllAsync(chatId => SendLotAsync(chatId, prepared, ct), Math.Max(1, prepared.PhotoCount), ct);
        await latest.SaveAsync(lot, prepared.FileIds, ct);

        log.LogInformation("Lot {Key} sent to {Sent} subscribers ({Photos} photos)", lot.Key, sent, prepared.PhotoCount);
        return sent;
    }

    /// <summary>
    /// For a new subscriber: the latest lot of each source that passed the filters,
    /// skipping lots whose application deadline has already passed.
    /// </summary>
    public async Task SendLatestLotsAsync(long chatId, CancellationToken ct = default)
    {
        var lots = (await latest.GetAllAsync(ct))
            .Where(x => x.Lot.DeadlineUtc is not { } d || d > DateTimeOffset.UtcNow)
            .Where(x => filter.Passes(x.Lot)) // filters may have changed since the lot was sent
            .ToList();
        if (lots.Count == 0) return;

        await bot.SendMessage(chatId, BotTexts.LatestIntro, cancellationToken: ct);
        foreach (var (lot, fileIds) in lots)
        {
            var prepared = await PrepareAsync(lot, fileIds, ct);
            await SendLotAsync(chatId, prepared, ct);
            if (fileIds is null && prepared.FileIds is { } ids) await latest.SaveFileIdsAsync(lot, ids, ct);
            await Task.Delay(TimeSpan.FromSeconds(1), ct); // per-chat limit: ~1 msg/sec
        }
        log.LogInformation("Sent {Count} latest lots to new subscriber {ChatId}", lots.Count, chatId);
    }

    /// <summary>Lot ready to send: text, button and photos (bytes for the first upload, then Telegram file ids).</summary>
    private sealed class PreparedLot
    {
        public required Lot Lot { get; init; }
        public required string Caption { get; init; }   // under photos, ≤ 1024 chars
        public required string Text { get; init; }      // text-only fallback, ≤ 4096 chars
        public required InlineKeyboardMarkup Button { get; init; }
        public List<byte[]> Photos { get; init; } = [];
        public string[]? FileIds { get; set; }
        public int PhotoCount => FileIds?.Length ?? Photos.Count;
    }

    private async Task<PreparedLot> PrepareAsync(Lot lot, string[]? fileIds, CancellationToken ct) => new()
    {
        Lot = lot,
        Caption = LotFormatter.Format(lot, LotFormatter.CaptionLimit),
        Text = LotFormatter.Format(lot, LotFormatter.TextLimit),
        Button = new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("Відкрити лот ↗", lot.Url)),
        FileIds = fileIds is { Length: > 0 } ? fileIds : null,
        Photos = fileIds is { Length: > 0 } ? [] : await DownloadPhotosAsync(lot.ImageUrls, ct),
    };

    /// <summary>
    /// One lot = one message: photos with the details as caption.
    /// A single photo also gets the «Відкрити лот ↗» button (Telegram doesn't allow buttons on albums;
    /// there the link is in the caption). Without photos — a text message with the button.
    /// </summary>
    private async Task SendLotAsync(long chatId, PreparedLot p, CancellationToken ct)
    {
        if (p.PhotoCount > 0)
        {
            try
            {
                var files = p.FileIds is { } ids
                    ? ids.Select(id => (InputFile)InputFile.FromFileId(id)).ToArray()
                    : p.Photos.Select((bytes, i) => (InputFile)InputFile.FromStream(new MemoryStream(bytes), $"photo{i + 1}.jpg")).ToArray();

                if (files.Length == 1)
                {
                    var m = await bot.SendPhoto(chatId, files[0], p.Caption, ParseMode.Html, replyMarkup: p.Button, cancellationToken: ct);
                    p.FileIds ??= [m.Photo!.Last().FileId];
                }
                else
                {
                    var media = files.Select((f, i) => i == 0
                        ? new InputMediaPhoto(f) { Caption = p.Caption, ParseMode = ParseMode.Html }
                        : new InputMediaPhoto(f)).ToArray();
                    var msgs = await bot.SendMediaGroup(chatId, media, cancellationToken: ct);
                    p.FileIds ??= msgs.Select(x => x.Photo!.Last().FileId).ToArray(); // later sends reuse the uploaded photos
                }
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400)
            {
                // Bad photo or caption — fall back to a text message so the lot is still delivered.
                log.LogWarning("Photos for {Key} rejected by Telegram, sending text only: {Error}", p.Lot.Key, ex.Message);
            }
        }

        await bot.SendMessage(chatId, p.Text, ParseMode.Html,
            replyMarkup: p.Button,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: ct);
    }

    private async Task<int> SendToAllAsync(Func<long, Task> send, int messagesPerChat, CancellationToken ct)
    {
        var chats = await store.GetAllAsync(ct);
        var sent = 0;

        foreach (var chatId in chats)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await send(chatId);
                    sent++;
                    break;
                }
                catch (ApiRequestException ex) when (ex.ErrorCode == 403)
                {
                    await store.RemoveAsync(chatId, ct); // blocked the bot or deleted the account
                    log.LogInformation("Removed {ChatId}: {Reason}", chatId, ex.Message);
                    break;
                }
                catch (ApiRequestException ex) when (ex.ErrorCode == 429 && attempt < 3)
                {
                    var wait = ex.Parameters?.RetryAfter ?? 5;
                    log.LogWarning("Rate limited, waiting {Seconds}s", wait);
                    await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Failed to send to {ChatId}", chatId);
                    break;
                }
            }

            await Task.Delay(Delay * messagesPerChat, ct);
        }

        return sent;
    }

    private async Task<List<byte[]>> DownloadPhotosAsync(IReadOnlyList<string> urls, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("web");
        var result = new List<byte[]>();
        foreach (var url in urls)
        {
            try
            {
                using var resp = await http.GetAsync(url, ct);
                var type = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (!resp.IsSuccessStatusCode || !(type is "image/jpeg" or "image/png")) continue;
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length is > 0 and < MaxPhotoBytes) result.Add(bytes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Photo skipped {Url}: {Error}", url, ex.Message);
            }
        }
        return result;
    }
}

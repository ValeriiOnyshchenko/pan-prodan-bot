using Microsoft.Extensions.Options;
using PanProdanBot.Auctions;
using PanProdanBot.Storage;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace PanProdanBot.Bot;

/// <summary>Long-polls Telegram for updates and optionally announces itself to subscribers on startup.</summary>
public sealed class BotService(
    ITelegramBotClient bot,
    UpdateHandler handler,
    Broadcaster broadcaster,
    IOptions<BotOptions> options,
    IOptions<MonitorOptions> monitor,
    LotFilter filter,
    StateStore state,
    ILogger<BotService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var me = await bot.GetMe(ct);
        log.LogInformation("Started as @{Username}", me.Username);

        // No command menu — users only see Telegram's native Start button.
        await bot.DeleteMyCommands(cancellationToken: ct);
        await SyncProfileAsync(ct);

        if (options.Value.AnnounceOnStartup)
            await broadcaster.BroadcastAsync(BotTexts.Startup, ct);

        await AnnounceFilterChangeAsync(ct);
        await SendAnnouncementsAsync(ct);

        await bot.ReceiveAsync(handler, new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.MyChatMember],
        }, ct);
    }

    /// <summary>
    /// After a deploy that changed the filters, tell every subscriber once.
    /// The current filter signature is stored, so restarts and unrelated deploys stay silent.
    /// </summary>
    private async Task AnnounceFilterChangeAsync(CancellationToken ct)
    {
        const string key = "filters_signature";
        try
        {
            var current = filter.Signature();
            var previous = await state.GetAsync(key, ct);
            if (previous == current) return;

            // previous == null: first version with this feature — the filters did change in this release, so announce too.
            log.LogInformation("Filters changed ({Previous} → {Current}), notifying subscribers", previous ?? "unknown", current);
            await broadcaster.BroadcastAsync(BotTexts.FiltersChanged(monitor.Value), ct);
            await state.SetAsync(key, current, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Could not announce the filter change");
        }
    }

    /// <summary>One-time announcements from <see cref="BotTexts.Announcements"/>, each sent once ever.</summary>
    private async Task SendAnnouncementsAsync(CancellationToken ct)
    {
        foreach (var (id, text) in BotTexts.Announcements)
        {
            var key = $"announcement:{id}";
            try
            {
                if (await state.GetAsync(key, ct) is not null) continue;
                await state.SetAsync(key, "sending", ct); // mark first: a crash mid-broadcast must not resend it to everyone
                var sent = await broadcaster.BroadcastAsync(text, ct);
                await state.SetAsync(key, $"sent to {sent}", ct);
                log.LogInformation("Announcement {Id} sent to {Sent} subscribers", id, sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Announcement {Id} failed", id);
            }
        }
    }

    /// <summary>Pushes name/descriptions from <see cref="BotTexts"/> to Telegram, only when they changed (these calls are rate-limited).</summary>
    private async Task SyncProfileAsync(CancellationToken ct)
    {
        try
        {
            if ((await bot.GetMyName(cancellationToken: ct)).Name != BotTexts.Name)
                await bot.SetMyName(BotTexts.Name, cancellationToken: ct);

            if ((await bot.GetMyShortDescription(cancellationToken: ct)).ShortDescription != BotTexts.ShortDescription)
                await bot.SetMyShortDescription(BotTexts.ShortDescription, cancellationToken: ct);

            var description = BotTexts.Description(monitor.Value);
            if ((await bot.GetMyDescription(cancellationToken: ct)).Description != description)
                await bot.SetMyDescription(description, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Could not sync bot profile texts");
        }
    }
}

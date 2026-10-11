using Microsoft.Extensions.Options;
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

        await bot.ReceiveAsync(handler, new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.MyChatMember],
        }, ct);
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

            if ((await bot.GetMyDescription(cancellationToken: ct)).Description != BotTexts.Description)
                await bot.SetMyDescription(BotTexts.Description, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Could not sync bot profile texts");
        }
    }
}

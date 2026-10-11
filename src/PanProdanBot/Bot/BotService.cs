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

        if (options.Value.AnnounceOnStartup)
            await broadcaster.BroadcastAsync(options.Value.StartupMessage, ct);

        await bot.ReceiveAsync(handler, new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.MyChatMember],
        }, ct);
    }
}

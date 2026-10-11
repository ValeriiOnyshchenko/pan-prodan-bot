using PanProdanBot.Storage;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace PanProdanBot.Bot;

/// <summary>Sends a message to every subscriber, respecting Telegram's ~30 msg/sec limit.</summary>
public sealed class Broadcaster(ITelegramBotClient bot, SubscriberStore store, ILogger<Broadcaster> log)
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(40); // ~25 msg/sec, under the limit

    public async Task<int> BroadcastAsync(string text, CancellationToken ct = default)
    {
        var chats = await store.GetAllAsync(ct);
        var sent = 0;

        foreach (var chatId in chats)
        {
            try
            {
                await bot.SendMessage(chatId, text, cancellationToken: ct);
                sent++;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403)
            {
                // User blocked the bot or deleted the account — stop sending to them.
                await store.RemoveAsync(chatId, ct);
                log.LogInformation("Removed {ChatId}: {Reason}", chatId, ex.Message);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                var wait = ex.Parameters?.RetryAfter ?? 5;
                log.LogWarning("Rate limited, waiting {Seconds}s", wait);
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                await bot.SendMessage(chatId, text, cancellationToken: ct);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Failed to send to {ChatId}", chatId);
            }

            await Task.Delay(Delay, ct);
        }

        log.LogInformation("Broadcast delivered to {Sent}/{Total} subscribers", sent, chats.Count);
        return sent;
    }
}

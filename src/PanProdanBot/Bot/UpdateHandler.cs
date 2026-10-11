using Microsoft.Extensions.Options;
using PanProdanBot.Data;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PanProdanBot.Bot;

/// <summary>Handles incoming updates: /start subscribes, /stop or blocking the bot unsubscribes.</summary>
public sealed class UpdateHandler(SubscriberStore store, IOptions<BotOptions> options, ILogger<UpdateHandler> log)
    : IUpdateHandler
{
    private readonly BotOptions _opt = options.Value;

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        switch (update)
        {
            case { Message: { Chat.Type: ChatType.Private, Text: { } text } msg }:
                await OnTextAsync(bot, msg, text.Trim(), ct);
                break;

            // Sent by Telegram when the user presses "Stop and block bot" (Kicked) or "Restart bot" (Member).
            case { MyChatMember: { Chat.Type: ChatType.Private } change }:
                if (change.NewChatMember.Status == ChatMemberStatus.Kicked)
                {
                    await store.RemoveAsync(change.Chat.Id, ct);
                    log.LogInformation("User {ChatId} blocked the bot — unsubscribed", change.Chat.Id);
                }
                break;
        }
    }

    private async Task OnTextAsync(ITelegramBotClient bot, Message msg, string text, CancellationToken ct)
    {
        var command = text.Split(' ', '@')[0].ToLowerInvariant();
        var chatId = msg.Chat.Id;

        switch (command)
        {
            case "/start":
                await store.AddAsync(chatId, msg.From?.Username, msg.From?.FirstName, ct);
                log.LogInformation("Subscribed {ChatId} (@{Username})", chatId, msg.From?.Username);
                await bot.SendMessage(chatId, _opt.WelcomeMessage, cancellationToken: ct);
                break;

            case "/stop":
                await store.RemoveAsync(chatId, ct);
                log.LogInformation("Unsubscribed {ChatId}", chatId);
                await bot.SendMessage(chatId, _opt.GoodbyeMessage, cancellationToken: ct);
                break;
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, HandleErrorSource source, CancellationToken ct)
    {
        log.LogError(ex, "Telegram error ({Source})", source);
        // Back off a little so a network outage doesn't spin the polling loop.
        return Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
}

namespace PanProdanBot;

public sealed class BotOptions
{
    public const string Section = "Bot";

    /// <summary>Telegram bot token from @BotFather. Never commit it — use user-secrets locally and a GitHub secret in CI.</summary>
    public string Token { get; set; } = "";

    /// <summary>Path to the SQLite file that stores subscribers.</summary>
    public string DatabasePath { get; set; } = "db/bot.db";

    /// <summary>When true, the bot sends <see cref="StartupMessage"/> to every subscriber after each start/deploy.</summary>
    public bool AnnounceOnStartup { get; set; } = true;

    public string StartupMessage { get; set; } = "Hello world! 👋 PanProdanBot is up and running.";

    public string WelcomeMessage { get; set; } =
        "Hello world! 👋 You are subscribed. I'll message you whenever I have something to say.\n\nSend /stop to unsubscribe.";

    public string GoodbyeMessage { get; set; } = "You are unsubscribed. Press /start any time to come back.";
}

namespace PanProdanBot;

public sealed class BotOptions
{
    public const string Section = "Bot";

    /// <summary>Telegram bot token from @BotFather. Never commit it — use user-secrets locally and a GitHub secret in CI.</summary>
    public string Token { get; set; } = "";

    /// <summary>Path to the SQLite file that stores subscribers.</summary>
    public string DatabasePath { get; set; } = "db/bot.db";

    /// <summary>When true, the bot sends <see cref="BotTexts.Startup"/> to every subscriber after each start/deploy.</summary>
    public bool AnnounceOnStartup { get; set; } = true;
}

using PanProdanBot.Auctions;

namespace PanProdanBot;

/// <summary>
/// Every text the user can see. Пан Продан speaks Ukrainian only.
/// Texts that mention filters are generated from <see cref="MonitorOptions"/>, so they never go out of date.
/// Profile texts (name/descriptions) are synced to Telegram on startup.
/// </summary>
public static class BotTexts
{
    // ---- Telegram profile (limits: name 64, short description 120, description 512 chars) ----

    public const string Name = "Пан Продан";

    /// <summary>Shown on the bot's profile page and in share previews.</summary>
    public const string ShortDescription =
        "Нові лоти нерухомості з аукціонів «Галконтракт» і СЕТАМ — з фото, цінами та посиланнями.";

    /// <summary>Shown in an empty chat before the user presses Start.</summary>
    public static string Description(MonitorOptions o) => Text.Truncate(
        "Вітаю! Я — Пан Продан 🎩\n\n" +
        $"{Frequency(o)} переглядаю нові лоти:\n" +
        Sources + "\n\n" +
        "Надсилаю лише вигідні:\n" +
        Filters(o) + "\n\n" +
        "Натисніть «Почати», щоб підписатися. Відписатися — /stop.", 512);

    // ---- Chat messages ----

    public static string Welcome(MonitorOptions o) =>
        "Вітаю! 🎩 Я — Пан Продан, і ви підписалися на мою розсилку.\n\n" +
        $"{Frequency(o)} я переглядаю нові лоти на аукціонах:\n" +
        Sources + "\n\n" +
        "Надсилаю лише ті, що проходять мої фільтри за стартовою ціною:\n" +
        Filters(o) + "\n\n" +
        "У кожному повідомленні — фото, ціна, адреса, дати аукціону та посилання на лот. " +
        "Щойно з'явиться щось нове — одразу напишу.\n\n" +
        "Щоб відписатися, надішліть /stop.";

    /// <summary>Sent after the welcome, before the latest lot of each auction.</summary>
    public const string LatestIntro =
        "Ось останні пропозиції з кожного аукціону, що пройшли мої фільтри 👇";

    public const string Goodbye =
        "Ви відписалися від розсилки. Якщо передумаєте — натисніть /start, і я знову надсилатиму пропозиції. До зустрічі! 👋";

    public const string Unknown =
        "Я поки що не відповідаю на повідомлення 🙂 Просто чекайте — нові лоти надійдуть самі.\n\nВідписатися: /stop";

    public const string Startup =
        "Пан Продан знову на зв'язку! 🔨 Продовжую стежити за аукціонами.";

    // ---- Building blocks ----

    private const string Sources =
        "• Біржа «Галконтракт» — мала приватизація\n" +
        "• СЕТАМ — нерухомість";

    private static string Filters(MonitorOptions o) =>
        string.Join("\n", o.RegionRules.Select(r => $"• {r.Name} — до {Text.Money(r.MaxPrice)}")
            .Append($"• {(o.RegionRules.Count > 0 ? "Інші області" : "Усі області")} — до {Text.Money(o.DefaultMaxPrice)}"));

    private static string Frequency(MonitorOptions o) => o.IntervalMinutes switch
    {
        60 => "Щогодини",
        var m when m % 60 == 0 => $"Кожні {m / 60} год.",
        var m => $"Кожні {m} хв.",
    };
}

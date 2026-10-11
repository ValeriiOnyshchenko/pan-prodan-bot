namespace PanProdanBot;

/// <summary>
/// Every text the user can see. Пан Продан speaks Ukrainian only.
/// Profile texts (name/descriptions) are synced to Telegram on startup.
/// </summary>
public static class BotTexts
{
    // ---- Telegram profile (limits: name 64, short description 120, description 512 chars) ----

    public const string Name = "Пан Продан";

    /// <summary>Shown on the bot's profile page and in share previews.</summary>
    public const string ShortDescription =
        "Стежу за аукціонами та надсилаю вигідні лоти. Натисніть «Почати», щоб підписатися.";

    /// <summary>Shown in an empty chat before the user presses Start.</summary>
    public const string Description =
        "Вітаю! Я — Пан Продан 🎩\n\n" +
        "Щодня переглядаю аукціони — нерухомість, авто та інше майно — і надсилаю вам найцікавіші пропозиції.\n\n" +
        "Натисніть «Почати», і я повідомлю, щойно знайду щось вартісне. Відписатися можна будь-коли командою /stop.";

    // ---- Chat messages ----

    public const string Welcome =
        "Вітаю! 🎩 Я — Пан Продан.\n\n" +
        "Ви підписалися на розсилку. Щойно на аукціонах з'явиться щось цікаве — я одразу напишу.\n\n" +
        "Щоб відписатися, надішліть /stop.";

    public const string Goodbye =
        "Ви відписалися від розсилки. Якщо передумаєте — натисніть /start, і я знову надсилатиму пропозиції. До зустрічі! 👋";

    public const string Unknown =
        "Я поки що не відповідаю на повідомлення 🙂 Просто чекайте — пропозиції надійдуть самі.\n\nВідписатися: /stop";

    public const string Startup =
        "Пан Продан знову на зв'язку! 🔨 Продовжую стежити за аукціонами.";
}

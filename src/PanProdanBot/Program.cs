using Microsoft.Extensions.Options;
using PanProdanBot;
using PanProdanBot.Auctions;
using PanProdanBot.Auctions.Sources;
using PanProdanBot.Bot;
using PanProdanBot.Storage;
using Telegram.Bot;

var dryRun = args.Contains("--dry-run");
var builder = Host.CreateApplicationBuilder(args);

// ---- Auctions ----
builder.Services.AddOptions<MonitorOptions>().Bind(builder.Configuration.GetSection(MonitorOptions.Section));
builder.Services.AddSingleton<LotFilter>();
builder.Services.AddSingleton(TimeProvider.System);

void Web(HttpClient c)
{
    c.Timeout = TimeSpan.FromSeconds(60);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PanProdanBot/1.0; +https://t.me/PanProdanBot)");
    c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk-UA,uk;q=0.9");
}
builder.Services.AddHttpClient("web", Web);
builder.Services.AddHttpClient<GalcontractSource>(Web);
builder.Services.AddHttpClient<SetamSource>(Web);
builder.Services.AddTransient<ILotSource>(sp => sp.GetRequiredService<GalcontractSource>());
builder.Services.AddTransient<ILotSource>(sp => sp.GetRequiredService<SetamSource>());

if (dryRun)
{
    // Preview what Пан Продан would send — no Telegram, no database.
    using var host = builder.Build();
    await DryRun.RunAsync(host.Services, args);
    return;
}

// ---- Telegram bot ----
builder.Services.AddOptions<BotOptions>()
    .Bind(builder.Configuration.GetSection(BotOptions.Section))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Token), "Bot:Token is not set (use user-secrets locally or the Bot__Token env var).")
    .ValidateOnStart();

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
    new TelegramBotClient(sp.GetRequiredService<IOptions<BotOptions>>().Value.Token));

builder.Services.AddSingleton<SubscriberStore>();
builder.Services.AddSingleton<SeenLotStore>();
builder.Services.AddSingleton<RecentLotStore>();
builder.Services.AddSingleton<Broadcaster>();
builder.Services.AddSingleton<UpdateHandler>();
builder.Services.AddHostedService<BotService>();
builder.Services.AddHostedService<AuctionMonitorService>();

builder.Build().Run();

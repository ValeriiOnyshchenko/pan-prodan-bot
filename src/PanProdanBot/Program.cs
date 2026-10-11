using Microsoft.Extensions.Options;
using PanProdanBot;
using PanProdanBot.Bot;
using PanProdanBot.Storage;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<BotOptions>()
    .Bind(builder.Configuration.GetSection(BotOptions.Section))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Token), "Bot:Token is not set (use user-secrets locally or the Bot__Token env var).")
    .ValidateOnStart();

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
    new TelegramBotClient(sp.GetRequiredService<IOptions<BotOptions>>().Value.Token));

builder.Services.AddSingleton<SubscriberStore>();
builder.Services.AddSingleton<Broadcaster>();
builder.Services.AddSingleton<UpdateHandler>();
builder.Services.AddHostedService<BotService>();

builder.Build().Run();

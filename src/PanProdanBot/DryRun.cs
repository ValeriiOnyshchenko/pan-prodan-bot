using Microsoft.Extensions.Options;
using PanProdanBot.Auctions;

namespace PanProdanBot;

/// <summary>
/// <c>dotnet run -- --dry-run [--show N]</c> — reads all sources, applies the filters and prints
/// the first N matching lots per source exactly as they would be sent (Telegram HTML).
/// </summary>
public static class DryRun
{
    public static async Task RunAsync(IServiceProvider sp, string[] args)
    {
        var show = args.SkipWhile(a => a != "--show").Skip(1).Select(a => int.TryParse(a, out var n) ? n : 3).FirstOrDefault(3);
        var filter = sp.GetRequiredService<LotFilter>();
        var o = sp.GetRequiredService<IOptions<MonitorOptions>>().Value;
        var ct = CancellationToken.None;

        Console.WriteLine("=== Welcome message ===\n" + BotTexts.Welcome(o));
        Console.WriteLine("\n=== Description ===\n" + BotTexts.Description(o) + "\n");

        foreach (var source in sp.GetServices<ILotSource>())
        {
            var active = await source.ListActiveAsync(ct);
            var shown = 0;
            var checkedCount = 0;
            Console.WriteLine($"##### {source.Name}: {active.Count} active lots");

            foreach (var s in active)
            {
                if (shown >= show) break;
                if (filter.PreCheck(s) == false) continue;
                checkedCount++;
                await Task.Delay(o.RequestDelayMs, ct);
                var lot = await source.GetLotAsync(s, ct);
                if (lot.Price is not { } price || !filter.Passes(lot.Region, price)) continue;

                shown++;
                Console.WriteLine($"----- {lot.Key} | region={lot.Region} | price={price} | limit={filter.LimitFor(lot.Region)} | photos={lot.ImageUrls.Count}");
                foreach (var img in lot.ImageUrls) Console.WriteLine("  img: " + img);
                Console.WriteLine(LotFormatter.Format(lot));
                Console.WriteLine();
            }
            Console.WriteLine($"(opened {checkedCount} lot pages)\n");
        }
    }
}

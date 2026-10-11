using Microsoft.Extensions.Options;
using PanProdanBot.Bot;
using PanProdanBot.Storage;

namespace PanProdanBot.Auctions;

/// <summary>
/// Every <see cref="MonitorOptions.IntervalMinutes"/>: read active lots from every source,
/// pick the ones not seen before, filter by region/price and broadcast the matches.
/// </summary>
public sealed class AuctionMonitorService(
    IEnumerable<ILotSource> sources,
    LotFilter filter,
    SeenLotStore seen,
    Broadcaster broadcaster,
    IOptions<MonitorOptions> options,
    ILogger<AuctionMonitorService> log) : BackgroundService
{
    private readonly MonitorOptions _o = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_o.Enabled)
        {
            log.LogInformation("Auction monitor is disabled");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(_o.StartupDelaySeconds), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_o.IntervalMinutes));
        do
        {
            await RunOnceAsync(ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var budget = _o.MaxLotsPerRun;
        foreach (var source in sources)
        {
            try
            {
                budget -= await CheckSourceAsync(source, budget, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Check of {Source} failed — will retry next time", source.Name);
            }
        }
    }

    /// <returns>Number of lots broadcast.</returns>
    private async Task<int> CheckSourceAsync(ILotSource source, int budget, CancellationToken ct)
    {
        var active = await source.ListActiveAsync(ct);
        if (active.Count == 0)
        {
            log.LogWarning("{Source}: no active lots found — site down or layout changed?", source.Name);
            return 0;
        }

        var known = await seen.GetKeysAsync(source.Id, ct);
        var fresh = active.Where(l => !known.Contains(l.Key)).ToList();

        if (known.Count == 0)
            return await FirstRunAsync(source, fresh, ct);

        log.LogInformation("{Source}: {Active} active, {New} new", source.Name, active.Count, fresh.Count);

        var broadcast = 0;
        foreach (var summary in fresh)
        {
            if (broadcast >= budget)
            {
                log.LogWarning("{Source}: per-run limit reached, the rest waits for the next check", source.Name);
                break;
            }

            var lot = await LoadMatchAsync(source, summary, ct);
            if (lot is null) continue;

            await broadcaster.BroadcastLotAsync(lot, ct);
            await seen.MarkAsync(lot.Key, source.Id, "broadcast", ct);
            broadcast++;
        }
        return broadcast;
    }

    /// <summary>
    /// First check of a source: remember everything that already exists (no flood),
    /// except a small sample that is broadcast so subscribers see the bot working.
    /// </summary>
    private async Task<int> FirstRunAsync(ILotSource source, List<LotSummary> fresh, CancellationToken ct)
    {
        var samples = new List<Lot>();
        foreach (var summary in fresh)
        {
            if (samples.Count >= _o.FirstRunSampleCount) break;
            if (await LoadMatchAsync(source, summary, ct, markRejected: false) is { } lot) samples.Add(lot);
        }

        var sampleKeys = samples.Select(l => l.Key).ToHashSet();
        await seen.MarkManyAsync(fresh.Select(l => l.Key).Where(k => !sampleKeys.Contains(k)), source.Id, "baseline", ct);
        log.LogInformation("{Source}: first run — remembered {Count} existing lots, sending {Samples} sample(s)",
            source.Name, fresh.Count - samples.Count, samples.Count);

        foreach (var lot in samples)
        {
            await broadcaster.BroadcastLotAsync(lot, ct);
            await seen.MarkAsync(lot.Key, source.Id, "broadcast", ct);
        }
        return samples.Count;
    }

    /// <summary>Returns the lot if it passes the filter; null if rejected or details could not be loaded.</summary>
    private async Task<Lot?> LoadMatchAsync(ILotSource source, LotSummary summary, CancellationToken ct, bool markRejected = true)
    {
        if (filter.PreCheck(summary) == false)
        {
            if (markRejected) await seen.MarkAsync(summary.Key, source.Id, "rejected", ct);
            return null;
        }

        Lot lot;
        try
        {
            await Task.Delay(_o.RequestDelayMs, ct);
            lot = await source.GetLotAsync(summary, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("{Key}: could not load details ({Error}) — will retry next time", summary.Key, ex.Message);
            return null;
        }

        if (lot.Price is not { } price || !filter.Passes(lot.Region, price))
        {
            if (markRejected) await seen.MarkAsync(summary.Key, source.Id, "rejected", ct);
            return null;
        }

        log.LogInformation("{Key}: match — {Region}, {Price}", lot.Key, lot.Region, Text.Money(price));
        return lot;
    }
}

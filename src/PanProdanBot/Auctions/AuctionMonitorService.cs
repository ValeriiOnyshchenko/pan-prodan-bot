using Microsoft.Extensions.Options;
using PanProdanBot.Bot;
using PanProdanBot.Storage;

namespace PanProdanBot.Auctions;

/// <summary>
/// Clock-aligned schedule (IntervalMinutes = 60 → every hour at xx:00):
/// at xx:55 read all sources and collect new lots that pass the filters,
/// at xx:00 send them to all subscribers at once.
/// </summary>
public sealed class AuctionMonitorService(
    IEnumerable<ILotSource> sources,
    LotFilter filter,
    SeenLotStore seen,
    SubscriberStore subscribers,
    Broadcaster broadcaster,
    IOptions<MonitorOptions> options,
    TimeProvider clock,
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

        while (!ct.IsCancellationRequested)
        {
            var sendAt = NextSlot(clock.GetUtcNow());
            var fetchAt = sendAt - TimeSpan.FromMinutes(Math.Clamp(_o.PrefetchMinutes, 0, _o.IntervalMinutes - 1));
            log.LogInformation("Next check at {FetchAt:HH:mm} UTC, delivery at {SendAt:HH:mm} UTC", fetchAt, sendAt);

            await DelayUntilAsync(fetchAt, ct);
            var batch = await CollectAsync(ct);

            await DelayUntilAsync(sendAt, ct);
            await SendAsync(batch, ct);
        }
    }

    /// <summary>Next clock boundary of the interval, e.g. the next full hour. Always strictly in the future.</summary>
    private DateTimeOffset NextSlot(DateTimeOffset now)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _o.IntervalMinutes));
        var next = new DateTimeOffset((now.UtcTicks / interval.Ticks + 1) * interval.Ticks, TimeSpan.Zero);
        // Not enough time to prefetch before the closest slot (e.g. started at 10:58) → take the following one.
        return next - now < TimeSpan.FromMinutes(Math.Min(_o.PrefetchMinutes, 2)) ? next + interval : next;
    }

    private async Task DelayUntilAsync(DateTimeOffset when, CancellationToken ct)
    {
        var wait = when - clock.GetUtcNow();
        if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, ct);
    }

    // ---------------- collect ----------------

    /// <summary>Reads all sources and returns new matching lots (rejected ones are remembered right away).</summary>
    public async Task<List<(ILotSource Source, Lot Lot)>> CollectAsync(CancellationToken ct)
    {
        var result = new List<(ILotSource, Lot)>();
        foreach (var source in sources)
        {
            try
            {
                var budget = _o.MaxLotsPerRun - result.Count;
                foreach (var lot in await CollectSourceAsync(source, budget, ct)) result.Add((source, lot));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Check of {Source} failed — will retry next time", source.Name);
            }
        }
        log.LogInformation("Collected {Count} new lots to send", result.Count);
        return result;
    }

    private async Task<List<Lot>> CollectSourceAsync(ILotSource source, int budget, CancellationToken ct)
    {
        var matches = new List<Lot>();
        var active = await source.ListActiveAsync(ct);
        if (active.Count == 0)
        {
            log.LogWarning("{Source}: no active lots found — site down or layout changed?", source.Name);
            return matches;
        }

        var known = await seen.GetKeysAsync(source.Id, ct);
        var fresh = active.Where(l => !known.Contains(l.Key)).ToList();
        var firstRun = known.Count == 0;
        if (firstRun) budget = Math.Min(budget, _o.FirstRunSampleCount);

        log.LogInformation("{Source}: {Active} active, {New} new{First}", source.Name, active.Count, fresh.Count,
            firstRun ? " — first run: existing lots are remembered silently" : "");

        foreach (var summary in fresh)
        {
            if (matches.Count >= budget)
            {
                if (!firstRun) log.LogWarning("{Source}: per-run limit reached, the rest waits for the next check", source.Name);
                break;
            }
            if (await LoadMatchAsync(source, summary, markRejected: !firstRun, ct) is { } lot) matches.Add(lot);
        }

        if (firstRun)
        {
            // Remember everything else BEFORE sending anything — a crash later can never cause a flood.
            var sample = matches.Select(l => l.Key).ToHashSet();
            await seen.MarkManyAsync(fresh.Select(l => l.Key).Where(k => !sample.Contains(k)), source.Id, "baseline", ct);
        }
        return matches;
    }

    /// <summary>Returns the lot if it passes the filters; null if rejected or details could not be loaded.</summary>
    private async Task<Lot?> LoadMatchAsync(ILotSource source, LotSummary summary, bool markRejected, CancellationToken ct)
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

        if (!filter.Passes(lot))
        {
            if (markRejected) await seen.MarkAsync(summary.Key, source.Id, "rejected", ct);
            return null;
        }

        log.LogInformation("{Key}: match — {Region}, {Price}", lot.Key, lot.Region, lot.Price is { } p ? Text.Money(p) : "?");
        return lot;
    }

    // ---------------- send ----------------

    private async Task SendAsync(List<(ILotSource Source, Lot Lot)> batch, CancellationToken ct)
    {
        await subscribers.PruneDeliveriesAsync(TimeSpan.FromDays(90), ct);

        foreach (var (source, lot) in batch)
        {
            try
            {
                await broadcaster.BroadcastLotAsync(lot, ct);
                await seen.MarkAsync(lot.Key, source.Id, "broadcast", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Broadcast of {Key} failed — will retry next time", lot.Key);
            }
        }
    }
}

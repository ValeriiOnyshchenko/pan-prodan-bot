using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;

namespace PanProdanBot.Auctions;

/// <summary>
/// Per-host request pacing shared by all HTTP clients. procedure.prozorro.sale sits behind Cloudflare, which
/// blocks quick bursts from the bot (403 "challenge" for several minutes) but accepts one request every few seconds.
/// </summary>
public sealed class HostThrottle(IOptions<MonitorOptions> options, TimeProvider clock, ILogger<HostThrottle> log)
{
    private sealed class HostState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public DateTimeOffset NextAllowed = DateTimeOffset.MinValue;
    }

    private readonly ConcurrentDictionary<string, HostState> _hosts = new(StringComparer.OrdinalIgnoreCase);

    private TimeSpan IntervalFor(string host) =>
        host.EndsWith("prozorro.sale", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromMilliseconds(options.Value.ProzorroRequestIntervalMs)
            : TimeSpan.Zero;

    /// <summary>Runs <paramref name="send"/> no sooner than the host's interval after the previous request.</summary>
    public async Task<HttpResponseMessage> RunAsync(Uri uri, Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        var interval = IntervalFor(uri.Host);
        if (interval == TimeSpan.Zero) return await send();

        var state = _hosts.GetOrAdd(uri.Host, _ => new HostState());
        await state.Gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var wait = state.NextAllowed - clock.GetUtcNow();
                if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, ct);

                var response = await send();
                state.NextAllowed = clock.GetUtcNow() + interval;

                var blocked = response.StatusCode == HttpStatusCode.Forbidden && response.Headers.Contains("cf-mitigated");
                if (!blocked || attempt >= 2) return response;

                var cooldown = TimeSpan.FromSeconds(options.Value.CloudflareCooldownSeconds);
                log.LogWarning("{Host}: Cloudflare challenge, pausing {Seconds}s and retrying {Path}", uri.Host, cooldown.TotalSeconds, uri.AbsolutePath);
                response.Dispose();
                state.NextAllowed = clock.GetUtcNow() + cooldown;
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }
}

/// <summary>Plugs <see cref="HostThrottle"/> into every request of the "web" HTTP client.</summary>
public sealed class ThrottlingHandler(HostThrottle throttle) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        throttle.RunAsync(request.RequestUri!, () => base.SendAsync(Clone(request), ct), ct);

    /// <summary>A request message can be sent only once — clone it for a possible retry (GET, no body).</summary>
    private static HttpRequestMessage Clone(HttpRequestMessage r)
    {
        var copy = new HttpRequestMessage(r.Method, r.RequestUri) { Version = r.Version, VersionPolicy = r.VersionPolicy };
        foreach (var h in r.Headers) copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return copy;
    }
}

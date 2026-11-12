using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Read-only JSON GETs with an in-memory cache and a minimum gap between requests,
/// so each service's rate limit is respected.
/// </summary>
public abstract class PacedJsonClient : IDisposable
{
    private const int MaxCacheEntries = 5000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, (DateTime Expires, string? Json)> _cache = new(StringComparer.Ordinal);
    private readonly TimeSpan _minInterval;
    private DateTime _lastRequest = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="PacedJsonClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="minInterval">Minimum time between requests.</param>
    protected PacedJsonClient(IHttpClientFactory httpClientFactory, ILogger logger, TimeSpan minInterval)
    {
        _httpClientFactory = httpClientFactory;
        Logger = logger;
        _minInterval = minInterval;
    }

    /// <summary>Gets the logger.</summary>
    protected ILogger Logger { get; }

    /// <summary>Gets the service name used in log lines.</summary>
    protected abstract string ServiceName { get; }

    /// <summary>
    /// Download an image or other file without any API credentials.
    /// </summary>
    /// <param name="url">URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HTTP response.</returns>
    public Task<HttpResponseMessage> GetRawAsync(string url, CancellationToken cancellationToken)
        => _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Release resources.
    /// </summary>
    /// <param name="disposing">Whether called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }
    }

    /// <summary>
    /// Add headers (authentication, user agent) to a request.
    /// </summary>
    /// <param name="request">Request.</param>
    protected virtual void Prepare(HttpRequestMessage request)
    {
    }

    /// <summary>
    /// GET a URL and deserialize the JSON response. Returns null on any error or 404.
    /// </summary>
    /// <typeparam name="T">Response type.</typeparam>
    /// <param name="url">Absolute URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed response or null.</returns>
    protected async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        var cacheHours = Math.Max(0, Plugin.Instance?.Configuration.CacheHours ?? 24);
        if (cacheHours > 0 && _cache.TryGetValue(url, out var cached) && cached.Expires > DateTime.UtcNow)
        {
            return Deserialize<T>(cached.Json);
        }

        string? json;
        try
        {
            json = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{Service}: request failed for {Url}", ServiceName, url);
            return null;
        }

        if (cacheHours > 0)
        {
            if (_cache.Count >= MaxCacheEntries)
            {
                foreach (var key in _cache.Where(kv => kv.Value.Expires <= DateTime.UtcNow).Select(kv => kv.Key).ToList())
                {
                    _cache.TryRemove(key, out _);
                }

                if (_cache.Count >= MaxCacheEntries)
                {
                    _cache.Clear();
                }
            }

            _cache[url] = (DateTime.UtcNow.AddHours(cacheHours), json);
        }

        return Deserialize<T>(json);
    }

    private T? Deserialize<T>(string? json)
        where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "{Service}: could not read response", ServiceName);
            return null;
        }
    }

    private async Task<string?> SendAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await PaceAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            Prepare(request);

            using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * (attempt + 1));
                Logger.LogInformation("{Service}: rate limited, waiting {Seconds}s", ServiceName, wait.TotalSeconds);
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning(
                    "{Service}: HTTP {Status} for {Url}: {Message}",
                    ServiceName,
                    (int)response.StatusCode,
                    url,
                    body.Length <= 300 ? body : body[..300]);
                return null;
            }

            return body;
        }

        Logger.LogWarning("{Service}: giving up on {Url} after repeated rate limiting", ServiceName, url);
        return null;
    }

    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastRequest + _minInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}

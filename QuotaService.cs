using System.Text.Json;

namespace VeloUploader;

public record QuotaInfo(long UsedBytes, long? QuotaBytes)
{
    public bool HasQuota => QuotaBytes.HasValue;
    public long FreeBytes => HasQuota ? Math.Max(0, QuotaBytes!.Value - UsedBytes) : long.MaxValue;
    public bool WouldExceed(long fileSize) => HasQuota && (UsedBytes + fileSize > QuotaBytes!.Value);

    public string UsedFormatted => FormatBytes(UsedBytes);
    public string QuotaFormatted => HasQuota ? FormatBytes(QuotaBytes!.Value) : "Unlimited";
    public string FreeFormatted => HasQuota ? FormatBytes(FreeBytes) : "Unlimited";

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F1} MB";
        return $"{bytes / 1024.0:F1} KB";
    }
}

public static class QuotaService
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static QuotaInfo? _cached;
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Fetch storage quota from the server. Returns null on network error.
    /// Results are cached for 2 minutes.
    /// </summary>
    public static async Task<QuotaInfo?> GetAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(settings.ApiToken))
            return null;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached != null && DateTime.UtcNow < _cacheExpiry)
                return _cached;

            using var handler = TlsCertHelper.CreateHandler(settings);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiToken);

            var url = settings.ServerUrl.TrimEnd('/') + "/api/quota";
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn($"Quota check returned {(int)response.StatusCode}");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var usedBytes = root.GetProperty("usedBytes").GetInt64();
            long? quotaBytes = null;
            if (root.GetProperty("quotaBytes").ValueKind != JsonValueKind.Null)
                quotaBytes = root.GetProperty("quotaBytes").GetInt64();

            _cached = new QuotaInfo(usedBytes, quotaBytes);
            _cacheExpiry = DateTime.UtcNow.Add(CacheTtl);
            return _cached;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Logger.Warn($"Quota check failed: {ex.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Invalidate the cache so the next call fetches fresh data.</summary>
    public static void Invalidate()
    {
        _lock.Wait();
        _cached = null;
        _lock.Release();
    }
}

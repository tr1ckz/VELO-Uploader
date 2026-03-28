using System.Net.Http.Headers;
using System.Text.Json;

namespace VeloUploader;

public static class PolicySyncService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<bool> TrySyncAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (!settings.EnablePolicySync) return false;
        if (string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(settings.ApiToken)) return false;

        var url = settings.ServerUrl.TrimEnd('/') + "/api/policy";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return false;

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("uploaderPolicy", out var policy))
            {
                if (policy.TryGetProperty("checksumValidationRequired", out var checksumReq))
                    settings.RequireUploadChecksum = checksumReq.GetBoolean();

                if (policy.TryGetProperty("featureFlags", out var flags))
                {
                    if (flags.TryGetProperty("uploaderQueuePersistence", out var queueFlag))
                        settings.EnableQueuePersistence = queueFlag.GetBoolean();
                    if (flags.TryGetProperty("uploaderGameAwareCompression", out var gameAwareFlag))
                        settings.AdaptiveCompressionWhenGaming = gameAwareFlag.GetBoolean();
                    if (flags.TryGetProperty("uploaderPolicySync", out var policyFlag))
                        settings.EnablePolicySync = policyFlag.GetBoolean();
                }

                settings.Save();
                Logger.Info("Policy sync applied from server.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Policy sync failed: {ex.Message}");
        }

        return false;
    }
}

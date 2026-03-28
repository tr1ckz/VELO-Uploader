using System.Diagnostics;

namespace VeloUploader;

public static class GameActivityDetector
{
    private static readonly string[] KnownGameProcessHints =
    [
        "steam", "epicgameslauncher", "battle.net", "riotclientservices", "valorant", "cs2", "dota2", "fortnite", "cod", "overwatch", "apex", "pubg", "r5apex"
    ];

    public static bool IsLikelyGameRunning(string clipPath)
    {
        try
        {
            var nameHint = Path.GetFileNameWithoutExtension(clipPath).ToLowerInvariant();
            var dirHint = (Path.GetDirectoryName(clipPath) ?? string.Empty).ToLowerInvariant();

            foreach (var proc in Process.GetProcesses())
            {
                string p;
                try { p = proc.ProcessName.ToLowerInvariant(); }
                catch { continue; }

                if (KnownGameProcessHints.Any(h => p.Contains(h, StringComparison.OrdinalIgnoreCase)))
                    return true;

                if (!string.IsNullOrWhiteSpace(nameHint) && nameHint.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!string.IsNullOrWhiteSpace(dirHint) && dirHint.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Detection is best-effort only.
        }

        return false;
    }
}

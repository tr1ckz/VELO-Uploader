using System.Media;

namespace VeloUploader;

public static class SoundFeedback
{
    public static void PlaySuccess(bool enabled)
    {
        if (!enabled) return;
        try { SystemSounds.Asterisk.Play(); } catch { }
    }

    public static void PlayFailure(bool enabled)
    {
        if (!enabled) return;
        try { SystemSounds.Hand.Play(); } catch { }
    }
}
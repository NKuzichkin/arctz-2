using System;

namespace ArctZ.Services.Diagnostics;

/// <summary>
/// Recognises the greeting FluidNC prints on connect, which is the only place the
/// firmware version appears in the stream. Two shapes occur in practice: the Grbl
/// compatibility banner ("Grbl 3.7 [FluidNC v3.7.0 ...]") and an informational
/// message ("[MSG:INFO: FluidNC v3.7.0 ...]").
/// </summary>
public static class FirmwareBannerDetector
{
    public static bool IsBanner(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.StartsWith("Grbl ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return trimmed.StartsWith("[MSG:INFO:", StringComparison.OrdinalIgnoreCase)
            && trimmed.Contains("FluidNC", StringComparison.OrdinalIgnoreCase);
    }
}

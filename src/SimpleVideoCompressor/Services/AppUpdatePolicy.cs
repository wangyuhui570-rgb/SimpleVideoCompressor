using System.Text.RegularExpressions;

namespace SimpleVideoCompressor.Services;

internal static partial class AppUpdatePolicy
{
    public const string PatchManifestFileName = "patch-manifest.json";
    public const string PortablePrefix = "SimpleVideoCompressor-Portable-";
    public const string PatchPrefix = "SimpleVideoCompressor-Patch-";

    public static Version? TryParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string trimmed = raw.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        int plusIdx = trimmed.IndexOf('+');
        if (plusIdx >= 0)
        {
            trimmed = trimmed[..plusIdx];
        }

        int dashIdx = trimmed.IndexOf('-');
        if (dashIdx >= 0)
        {
            trimmed = trimmed[..dashIdx];
        }

        return Version.TryParse(trimmed, out Version? v) ? v : null;
    }

    public static string? ExtractField(string? notes, string label)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var match = Regex.Match(
            notes,
            $@"^\s*{Regex.Escape(label)}:\s*(\S+)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public static string? ExtractSha256(string? notes, string label = "SHA256")
    {
        string? raw = ExtractField(notes, label);
        if (string.IsNullOrWhiteSpace(raw) || raw.Length != 64)
        {
            return null;
        }

        foreach (char c in raw)
        {
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                return null;
            }
        }

        return raw.ToLowerInvariant();
    }
}

using System.Text.RegularExpressions;

namespace HornetStudio.Core.Logging;

public static partial class SensitiveDataSanitizer
{
    private static readonly Regex KeyValueRegex = new(
        @"\b(password|pwd|secret|token|apikey|api_key)\s*([:=])\s*([^;\s&""']+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsonPasswordRegex = new(
        @"""(password|encryptedPassword|sshPassword|passphrase)""\s*:\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sanitized = KeyValueRegex.Replace(text, "$1$2******");
        sanitized = JsonPasswordRegex.Replace(sanitized, "\"$1\":\"******\"");
        return sanitized;
    }
}

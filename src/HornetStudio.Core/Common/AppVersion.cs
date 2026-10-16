using System;
using System.Reflection;

namespace HornetStudio.Core.Common;

public static class AppVersion
{
    private static readonly Lazy<(string SemVer, string FullVersion, string CommitHash)> _versionData = new(LoadVersion);

    public static string SemVer => _versionData.Value.SemVer;
    public static string DisplayVersion => $"v{SemVer}";
    public static string FullVersion => _versionData.Value.FullVersion;
    public static string CommitHash => _versionData.Value.CommitHash;

    private static (string SemVer, string FullVersion, string CommitHash) LoadVersion()
    {
        var assembly = typeof(AppVersion).Assembly;

        // Try getting InformationalVersion (which includes git commit in .NET 8+)
        var infoAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var full = infoAttr?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(full))
        {
            var asmVer = assembly.GetName().Version;
            var fallback = asmVer != null ? $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}" : "1.0.0";
            return (fallback, fallback, string.Empty);
        }

        var parts = full.Split('+', 2, StringSplitOptions.TrimEntries);
        var semVer = parts[0];
        var commit = parts.Length > 1 ? parts[1] : string.Empty;

        return (semVer, full, commit);
    }
}

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace ArctZ.Services.Diagnostics;

/// <summary>
/// Identity of the running build. The values come from attributes stamped into the
/// core assembly at compile time by the GitVersionStamp target in ArctZ.csproj —
/// git itself is not consulted at runtime, since on a phone or in a browser there
/// is no repository to consult.
/// </summary>
public sealed record BuildInfo(string Version, DateTimeOffset? CommitDate)
{
    public const string AppName = "ArctZ";

    /// <summary>Shown instead of a version when the build carried no version information at all.</summary>
    public const string UnknownVersion = "неизвестно";

    /// <summary>Name of the AssemblyMetadata attribute holding the ISO-8601 commit date.</summary>
    public const string CommitDateAttributeKey = "CommitDate";

    private static readonly Lazy<BuildInfo> LazyCurrent = new(() => FromAssembly(typeof(BuildInfo).Assembly));

    public static BuildInfo Current => LazyCurrent.Value;

    public static BuildInfo FromAssembly(Assembly assembly) => Create(
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        assembly.GetName().Version?.ToString(),
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == CommitDateAttributeKey)?.Value);

    public static BuildInfo Create(string? informationalVersion, string? assemblyVersion, string? commitDateRaw) =>
        new(ResolveVersion(informationalVersion, assemblyVersion), ParseCommitDate(commitDateRaw));

    private static string ResolveVersion(string? informationalVersion, string? assemblyVersion)
    {
        // The SDK appends "+<SourceRevisionId>" to InformationalVersion; the git-describe
        // output we stamp already carries the hash, so the suffix is noise. Trimmed here
        // as well as suppressed in the csproj, so a build without that switch still reads well.
        var version = informationalVersion?.Split('+')[0].Trim();

        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        return string.IsNullOrWhiteSpace(assemblyVersion) ? UnknownVersion : assemblyVersion.Trim();
    }

    private static DateTimeOffset? ParseCommitDate(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}

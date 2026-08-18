using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace ArctZ.Services.Diagnostics;

/// <summary>
/// Everything the "О программе" report says about the machine the app is running on.
/// Read once per process — none of it changes while the app is up.
/// </summary>
public static class EnvironmentInfo
{
    /// <summary>Shown wherever a value could not be determined.</summary>
    public const string Unknown = "—";

    private static readonly Lazy<string> LazyPlatformName = new(DetectPlatformName);
    private static readonly Lazy<IReadOnlyList<string>> LazyPlatformLines = new(BuildPlatformLines);
    private static readonly Lazy<IReadOnlyList<string>> LazyRuntimeLines = new(BuildRuntimeLines);
    private static readonly Lazy<IReadOnlyList<string>> LazyLibraryLines = new(BuildLibraryLines);

    public static string PlatformName => LazyPlatformName.Value;

    public static IReadOnlyList<string> PlatformLines => LazyPlatformLines.Value;

    public static IReadOnlyList<string> RuntimeLines => LazyRuntimeLines.Value;

    public static IReadOnlyList<string> LibraryLines => LazyLibraryLines.Value;

    private static string DetectPlatformName()
    {
        // OperatingSystem.Is* rather than RuntimeInformation.IsOSPlatform, because it is the
        // only one that distinguishes Android from Linux and the browser from anything else.
        if (OperatingSystem.IsAndroid()) return "Android";
        if (OperatingSystem.IsIOS()) return "iOS";
        if (OperatingSystem.IsBrowser()) return "Браузер (WASM)";
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";
        return Unknown;
    }

    private static IReadOnlyList<string> BuildPlatformLines() => new[]
    {
        $"Платформа: {PlatformName}",
        $"ОС: {Describe(RuntimeInformation.OSDescription)}",
        $"Версия ОС: {Describe(Environment.OSVersion.VersionString)}",
        $"Архитектура ОС: {RuntimeInformation.OSArchitecture}",
        $"Архитектура процесса: {RuntimeInformation.ProcessArchitecture}",
    };

    private static IReadOnlyList<string> BuildRuntimeLines() => new[]
    {
        $"Среда: {Describe(RuntimeInformation.FrameworkDescription)}",
        $"Идентификатор среды: {Describe(RuntimeInformation.RuntimeIdentifier)}",
    };

    private static IReadOnlyList<string> BuildLibraryLines() => new[]
    {
        ("Avalonia", typeof(Application)),
        ("ReactiveUI", typeof(ReactiveObject)),
        ("CommunityToolkit.Mvvm", typeof(ObservableObject)),
        ("Material.Icons", typeof(MaterialIconKind)),
        ("Microsoft.Extensions.DependencyInjection", typeof(ServiceCollection)),
    }
        .Select(entry => $"{entry.Item1}: {VersionOf(entry.Item2)}")
        .ToArray();

    private static string VersionOf(Type typeFromAssembly)
    {
        var assembly = typeFromAssembly.Assembly;

        // InformationalVersion is the NuGet package version ("12.0.4"); AssemblyVersion is
        // usually rounded to a major-only value ("12.0.0.0") and would misreport the package.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0].Trim();

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? Unknown;
    }

    private static string Describe(string? value) => string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();
}

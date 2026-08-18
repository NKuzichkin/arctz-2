using System.Linq;
using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class EnvironmentInfoTests
{
    [Fact]
    public void PlatformName_NamesTheHostOperatingSystem()
    {
        Assert.False(string.IsNullOrWhiteSpace(EnvironmentInfo.PlatformName));
    }

    [Fact]
    public void PlatformLines_DescribeTheOperatingSystemAndItsArchitecture()
    {
        var lines = EnvironmentInfo.PlatformLines;

        Assert.Contains(lines, l => l.StartsWith("Платформа:"));
        Assert.Contains(lines, l => l.StartsWith("ОС:"));
        Assert.Contains(lines, l => l.StartsWith("Архитектура ОС:"));
        Assert.Contains(lines, l => l.StartsWith("Архитектура процесса:"));
    }

    [Fact]
    public void RuntimeLines_NameTheDotNetRuntime()
    {
        Assert.Contains(EnvironmentInfo.RuntimeLines, l => l.Contains(".NET"));
    }

    [Fact]
    public void LibraryLines_ListTheVersionsOfTheUiStack()
    {
        var lines = EnvironmentInfo.LibraryLines;

        Assert.Contains(lines, l => l.StartsWith("Avalonia:"));
        Assert.Contains(lines, l => l.StartsWith("ReactiveUI:"));
        Assert.Contains(lines, l => l.StartsWith("CommunityToolkit.Mvvm:"));
    }

    [Fact]
    public void LibraryLines_CarryAResolvedVersionForEveryEntry()
    {
        Assert.All(EnvironmentInfo.LibraryLines, line =>
        {
            var version = line.Split(':', 2)[1].Trim();
            Assert.False(string.IsNullOrWhiteSpace(version));
        });
    }
}

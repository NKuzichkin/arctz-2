using System;
using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class BuildInfoTests
{
    [Fact]
    public void Create_UsesGitDescribeOutputAsVersion()
    {
        var info = BuildInfo.Create("v0.9.0-3-g7bf6f5f", "1.0.0.0", "2026-08-17T21:14:05+03:00");

        Assert.Equal("v0.9.0-3-g7bf6f5f", info.Version);
    }

    [Fact]
    public void Create_StripsSdkAppendedBuildMetadata()
    {
        var info = BuildInfo.Create("7bf6f5f+c1b2a3d4e5", "1.0.0.0", null);

        Assert.Equal("7bf6f5f", info.Version);
    }

    [Fact]
    public void Create_KeepsDirtyMarkerFromGitDescribe()
    {
        var info = BuildInfo.Create("7bf6f5f-dirty", "1.0.0.0", null);

        Assert.Equal("7bf6f5f-dirty", info.Version);
    }

    [Fact]
    public void Create_ParsesCommitDate()
    {
        var info = BuildInfo.Create("7bf6f5f", "1.0.0.0", "2026-08-17T21:14:05+03:00");

        Assert.Equal(new DateTimeOffset(2026, 8, 17, 21, 14, 5, TimeSpan.FromHours(3)), info.CommitDate);
    }

    [Fact]
    public void Create_LeavesCommitDateUnsetWhenTheStampIsMissing()
    {
        var info = BuildInfo.Create("7bf6f5f", "1.0.0.0", null);

        Assert.Null(info.CommitDate);
    }

    [Fact]
    public void Create_LeavesCommitDateUnsetWhenTheStampIsUnparsable()
    {
        var info = BuildInfo.Create("7bf6f5f", "1.0.0.0", "не дата");

        Assert.Null(info.CommitDate);
    }

    [Fact]
    public void Create_FallsBackToAssemblyVersionWhenGitWasUnavailable()
    {
        var info = BuildInfo.Create(null, "1.2.3.0", null);

        Assert.Equal("1.2.3.0", info.Version);
    }

    [Fact]
    public void Create_FallsBackToAssemblyVersionWhenGitDescribeOutputIsBlank()
    {
        var info = BuildInfo.Create("   ", "1.2.3.0", null);

        Assert.Equal("1.2.3.0", info.Version);
    }

    [Fact]
    public void Create_ReportsUnknownWhenNoVersionIsAvailableAtAll()
    {
        var info = BuildInfo.Create(null, null, null);

        Assert.Equal("неизвестно", info.Version);
    }

    [Fact]
    public void Current_ReadsTheRunningAssembly()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Current.Version));
    }
}

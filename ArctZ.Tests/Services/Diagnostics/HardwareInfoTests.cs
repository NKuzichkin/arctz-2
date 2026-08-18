using System;
using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class HardwareInfoTests
{
    [Fact]
    public void Capture_ReportsTheNumberOfLogicalProcessors()
    {
        var hardware = HardwareInfo.Capture(storageLocation: null);

        Assert.Equal(Environment.ProcessorCount, hardware.LogicalProcessors);
    }

    [Fact]
    public void Capture_NamesTheProcessorOnThisPlatform()
    {
        var hardware = HardwareInfo.Capture(storageLocation: null);

        // Windows and Linux/Android both have a source for this; the test host is one of them.
        Assert.False(string.IsNullOrWhiteSpace(hardware.CpuModel));
    }

    [Fact]
    public void Capture_ReportsSystemMemory()
    {
        var hardware = HardwareInfo.Capture(storageLocation: null);

        Assert.NotNull(hardware.TotalMemoryBytes);
        Assert.True(hardware.TotalMemoryBytes > 0);
        Assert.NotNull(hardware.UsedMemoryBytes);
        Assert.InRange(hardware.UsedMemoryBytes!.Value, 0, hardware.TotalMemoryBytes!.Value);
    }

    [Fact]
    public void Capture_ReportsTheMemoryThisProcessIsUsing()
    {
        var hardware = HardwareInfo.Capture(storageLocation: null);

        Assert.True(hardware.ProcessMemoryBytes > 0);
    }

    [Fact]
    public void Capture_LeavesStorageUnknownWhenThereIsNoStorageDirectory()
    {
        var hardware = HardwareInfo.Capture(storageLocation: null);

        Assert.Null(hardware.StorageLocation);
        Assert.Null(hardware.TotalStorageBytes);
        Assert.Null(hardware.UsedStorageBytes);
    }

    [Fact]
    public void Capture_MeasuresTheVolumeHoldingTheStorageDirectory()
    {
        var hardware = HardwareInfo.Capture(AppContext.BaseDirectory);

        Assert.Equal(AppContext.BaseDirectory, hardware.StorageLocation);
        Assert.NotNull(hardware.TotalStorageBytes);
        Assert.True(hardware.TotalStorageBytes > 0);
        Assert.InRange(hardware.UsedStorageBytes!.Value, 0, hardware.TotalStorageBytes!.Value);
    }

    [Fact]
    public void Capture_SurvivesAStorageDirectoryThatDoesNotExist()
    {
        // Diagnostics must never be the thing that crashes: a report gathered while something
        // is already wrong is exactly when an unreadable path is most likely.
        var hardware = HardwareInfo.Capture(@"Q:\нет\такого\каталога");

        Assert.Null(hardware.TotalStorageBytes);
        Assert.Null(hardware.UsedStorageBytes);
    }
}

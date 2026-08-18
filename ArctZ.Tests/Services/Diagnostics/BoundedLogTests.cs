using System;
using ArctZ.Services.Diagnostics;

namespace ArctZ.Tests.Services.Diagnostics;

public class BoundedLogTests
{
    [Fact]
    public void Snapshot_IsEmptyForNewLog()
    {
        var log = new BoundedLog<string>(3);

        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void Add_KeepsEveryEntryWhileBelowCapacity()
    {
        var log = new BoundedLog<string>(3);

        log.Add("a");
        log.Add("b");

        Assert.Equal(new[] { "a", "b" }, log.Snapshot());
    }

    [Fact]
    public void Add_DropsOldestEntriesOnceCapacityIsExceeded()
    {
        var log = new BoundedLog<string>(3);

        log.Add("a");
        log.Add("b");
        log.Add("c");
        log.Add("d");

        Assert.Equal(new[] { "b", "c", "d" }, log.Snapshot());
    }

    [Fact]
    public void Snapshot_IsNotAffectedByLaterAdds()
    {
        var log = new BoundedLog<string>(3);
        log.Add("a");

        var snapshot = log.Snapshot();
        log.Add("b");

        Assert.Equal(new[] { "a" }, snapshot);
    }

    [Fact]
    public void Clear_RemovesEveryEntry()
    {
        var log = new BoundedLog<string>(3);
        log.Add("a");
        log.Add("b");

        log.Clear();

        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLog<string>(0));
    }
}

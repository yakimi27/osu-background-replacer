using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Services;
using Xunit;

namespace OsuBackgroundReplacer.Tests;

[Collection("LogServiceState")]
public class LogServiceTests : IDisposable
{
    private readonly int _originalMax;

    public LogServiceTests()
    {
        _originalMax = LogService.MaxEntries;
        LogService.Clear();
    }

    public void Dispose()
    {
        LogService.Clear();
        LogService.MaxEntries = _originalMax;
    }

    [Fact]
    public void RunningCounters_UpdateImmediatelyAndAccurately()
    {
        LogService.Clear();

        LogService.AddEntry("C:\\osu\\Songs\\map1\\bg.jpg", success: true);
        LogService.AddEntry("C:\\osu\\Songs\\map2\\bg.jpg", success: true);
        LogService.AddEntry("C:\\osu\\Songs\\map3\\bg.jpg", success: false, error: "Access denied");

        Assert.Equal(2, LogService.TotalSuccessCount);
        Assert.Equal(2, LogService.TotalReplacedCount);
        Assert.Equal(1, LogService.TotalFailureCount);
        Assert.NotNull(LogService.LastRunTime);
    }

    [Fact]
    public void MaxEntries_CappingDropsOldestEntries()
    {
        LogService.Clear();
        LogService.MaxEntries = 5;

        for (int i = 1; i <= 10; i++)
        {
            LogService.AddEntry($"C:\\osu\\Songs\\map{i}\\bg.jpg", success: true);
        }

        LogService.Flush();

        Assert.Equal(5, LogService.Entries.Count);
        Assert.Equal(10, LogService.TotalReplacedCount); // Running counter retains total added
        // Newest entry is map10 (at index 0), oldest kept is map6
        Assert.Contains("map10", LogService.Entries[0].FilePath);
        Assert.Contains("map6", LogService.Entries[4].FilePath);
    }

    [Fact]
    public void Flush_DrainsPendingEntriesAndRaisesEvent()
    {
        LogService.Clear();
        bool eventFired = false;
        Action handler = () => eventFired = true;
        LogService.EntriesChanged += handler;

        try
        {
            LogService.AddEntry("C:\\osu\\Songs\\map1\\bg.jpg", success: true);
            LogService.Flush();

            Assert.True(eventFired);
            Assert.Single(LogService.Entries);
        }
        finally
        {
            LogService.EntriesChanged -= handler;
        }
    }

    [Fact]
    public void Clear_ResetsCountersAndEntries()
    {
        LogService.AddEntry("C:\\osu\\Songs\\map1\\bg.jpg", success: true);
        LogService.AddEntry("C:\\osu\\Songs\\map2\\bg.jpg", success: false, "err");
        LogService.Flush();

        Assert.True(LogService.Entries.Count > 0);
        Assert.Equal(1, LogService.TotalSuccessCount);
        Assert.Equal(1, LogService.TotalFailureCount);

        LogService.Clear();

        Assert.Empty(LogService.Entries);
        Assert.Equal(0, LogService.TotalSuccessCount);
        Assert.Equal(0, LogService.TotalFailureCount);
        Assert.Equal(0, LogService.TotalReplacedCount);
        Assert.Null(LogService.LastRunTime);
    }
}

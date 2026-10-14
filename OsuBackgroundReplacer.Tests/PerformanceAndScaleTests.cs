using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Logic;
using OsuBackgroundReplacerMain.Services;
using Xunit;

namespace OsuBackgroundReplacer.Tests;

[Collection("OperationsStaticState")]
public class PerformanceAndScaleTests : IDisposable
{
    private readonly string _testRoot;

    public PerformanceAndScaleTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "ScaleTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        ImageOperations.setPath(null);
        FolderOperations.setPath(null);
    }

    public void Dispose()
    {
        try
        {
            ImageOperations.setPath(null);
            FolderOperations.setPath(null);

            if (Directory.Exists(_testRoot))
            {
                foreach (var file in Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_testRoot, true);
            }
        }
        catch { }
    }

    [Fact]
    public void GetBeatmapCount_CountsOnlyFoldersContainingOsuFiles()
    {
        string songsDir = Path.Combine(_testRoot, "FilterSongs");
        Directory.CreateDirectory(songsDir);

        // 1. Valid beatmap folder with .osu file
        string beatmap1 = Path.Combine(songsDir, "1001 Artist - Song One");
        Directory.CreateDirectory(beatmap1);
        File.WriteAllText(Path.Combine(beatmap1, "beatmap.osu"), "osu file format v14");

        // 2. Another valid beatmap folder with multiple .osu files
        string beatmap2 = Path.Combine(songsDir, "1002 Artist - Song Two");
        Directory.CreateDirectory(beatmap2);
        File.WriteAllText(Path.Combine(beatmap2, "diff1.osu"), "osu file format v14");
        File.WriteAllText(Path.Combine(beatmap2, "diff2.osu"), "osu file format v14");

        // 3. Folder without .osu file (only image)
        string nonBeatmap1 = Path.Combine(songsDir, "random_folder_images");
        Directory.CreateDirectory(nonBeatmap1);
        File.WriteAllText(Path.Combine(nonBeatmap1, "pic.jpg"), "image content");

        // 4. Completely empty folder
        string nonBeatmap2 = Path.Combine(songsDir, "empty_folder");
        Directory.CreateDirectory(nonBeatmap2);

        // 5. Folder with only .txt file
        string nonBeatmap3 = Path.Combine(songsDir, "notes_folder");
        Directory.CreateDirectory(nonBeatmap3);
        File.WriteAllText(Path.Combine(nonBeatmap3, "readme.txt"), "hello");

        // Total directories is 5, but only 2 contain .osu files
        int count = FolderOperations.GetBeatmapCount(songsDir);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetBeatmapCountAsync_BenchmarkSeveralThousandFolders()
    {
        string songsDir = Path.Combine(_testRoot, "BenchmarkSongs");
        Directory.CreateDirectory(songsDir);

        const int totalFolders = 3000;
        const int validBeatmapFolders = 2000;

        // Create 3000 folders: 2000 with .osu files, 1000 without
        for (int i = 1; i <= totalFolders; i++)
        {
            string folder = Path.Combine(songsDir, $"Beatmap_{i}");
            Directory.CreateDirectory(folder);

            if (i <= validBeatmapFolders)
            {
                File.WriteAllText(Path.Combine(folder, "song.osu"), "osu file format v14");
            }
            else
            {
                File.WriteAllText(Path.Combine(folder, "other.txt"), "not a beatmap");
            }
        }

        var sw = Stopwatch.StartNew();
        int counted = await FolderOperations.GetBeatmapCountAsync(songsDir, CancellationToken.None);
        sw.Stop();

        Assert.Equal(validBeatmapFolders, counted);
        // Fast folder enumeration should complete well within 2 seconds even on disk
        Assert.True(sw.ElapsedMilliseconds < 2500, $"Scan of 3000 folders took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LogService_ScaleBenchmark_HandlesThousandsOfEntriesQuickly()
    {
        LogService.Clear();
        LogService.MaxEntries = 5000;

        const int entryCount = 10000;
        var sw = Stopwatch.StartNew();

        for (int i = 1; i <= entryCount; i++)
        {
            LogService.AddEntry($"C:\\osu\\Songs\\folder_{i}\\bg.jpg", success: true);
        }

        LogService.Flush();
        sw.Stop();

        Assert.Equal(entryCount, LogService.TotalSuccessCount);
        Assert.Equal(entryCount, LogService.TotalReplacedCount);
        Assert.Equal(5000, LogService.Entries.Count);
        // Adding and capping 10,000 entries with running counters must take < 500 ms
        Assert.True(sw.ElapsedMilliseconds < 500, $"10,000 log entries took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Replacement_ContinuousProgress_Reports50PercentForBackup_And100ForReplacement()
    {
        string songsDir = Path.Combine(_testRoot, "ProgSongs");
        string backupDir = Path.Combine(_testRoot, "ProgBackups");
        Directory.CreateDirectory(songsDir);
        Directory.CreateDirectory(backupDir);

        string beatmapDir = Path.Combine(songsDir, "1001 Artist - Song");
        Directory.CreateDirectory(beatmapDir);

        string originalBg = Path.Combine(beatmapDir, "bg.jpg");
        await File.WriteAllTextAsync(originalBg, "Original Image Data");

        string osuFile = Path.Combine(beatmapDir, "song.osu");
        await File.WriteAllTextAsync(osuFile, "[Events]\n0,0,\"bg.jpg\",0,0\n");

        string replacementImage = Path.Combine(_testRoot, "new_bg.jpg");
        await File.WriteAllTextAsync(replacementImage, "New Image Data");

        ImageOperations.setPath(replacementImage);
        FolderOperations.setPath(songsDir);

        var detailedProgressList = new List<ReplacementProgress>();
        var simpleProgressList = new List<int>();

        var detailedProgress = new Progress<ReplacementProgress>(p =>
        {
            lock (detailedProgressList)
            {
                detailedProgressList.Add(new ReplacementProgress
                {
                    Current = p.Current,
                    Total = p.Total,
                    Percentage = p.Percentage,
                    CurrentFile = p.CurrentFile
                });
            }
        });

        var simpleProgress = new Progress<int>(p =>
        {
            lock (simpleProgressList)
            {
                simpleProgressList.Add(p);
            }
        });

        var backupService = new BackupService(backupDir);

        var replaced = await Operations.Replacement(
            detailedProgress,
            simpleProgress,
            CancellationToken.None,
            enableBackup: true,
            backupService: backupService);

        Assert.Single(replaced);
        Assert.Equal(Path.GetFullPath(originalBg), replaced[0]);
        Assert.Equal("New Image Data", await File.ReadAllTextAsync(originalBg));

        // Wait briefly for progress events to post
        await Task.Delay(150);

        lock (simpleProgressList)
        {
            Assert.NotEmpty(simpleProgressList);
            // Minimum progress reported is >= 0
            Assert.True(simpleProgressList.Min() >= 0);
            // Maximum progress reported is 100
            Assert.Equal(100, simpleProgressList.Max());

            // Backup phase should report values up to 50
            Assert.Contains(simpleProgressList, pct => pct <= 50);
            // Replacement phase should report values >= 50
            Assert.Contains(simpleProgressList, pct => pct >= 50);
        }

        lock (detailedProgressList)
        {
            Assert.NotEmpty(detailedProgressList);
            // Total steps for 1 image with backup enabled is 2 (1 backup + 1 replace)
            Assert.Equal(2, detailedProgressList.Last().Total);
            Assert.Equal(2, detailedProgressList.Last().Current);
            Assert.Equal(100, detailedProgressList.Last().Percentage);
        }
    }
}

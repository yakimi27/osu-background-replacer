using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Services;
using Xunit;

namespace OsuBackgroundReplacer.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _backupDir;
    private readonly string _songsDir;
    private readonly BackupService _backupService;

    public BackupServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "BackupServiceTests_" + Guid.NewGuid().ToString("N"));
        _backupDir = Path.Combine(_testRoot, "Backups");
        _songsDir = Path.Combine(_testRoot, "Songs");

        Directory.CreateDirectory(_backupDir);
        Directory.CreateDirectory(_songsDir);

        _backupService = new BackupService(_backupDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                // Clear any read-only attributes that might prevent deletion
                foreach (var file in Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Ignore cleanup errors during test teardown
        }
    }

    [Fact]
    public async Task CreateBackupSession_PreservesRelativePathAndWritesManifest()
    {
        // Setup beatmap folders and images
        string beatmap1 = Path.Combine(_songsDir, "1001 Artist - Song One");
        string beatmap2 = Path.Combine(_songsDir, "1002 Artist - Song Two");
        Directory.CreateDirectory(beatmap1);
        Directory.CreateDirectory(beatmap2);

        string file1 = Path.Combine(beatmap1, "bg.jpg");
        string file2 = Path.Combine(beatmap2, "background.png");
        await File.WriteAllTextAsync(file1, "Original Content 1");
        await File.WriteAllTextAsync(file2, "Original Content 2");

        var files = new List<string> { file1, file2 };

        // Act
        var result = await _backupService.CreateBackupSessionAsync(files, _songsDir);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.BackedUpCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(Directory.Exists(result.SessionDirectory));

        // Check relative paths preserved: beatmap folder + filename
        string backupFile1 = Path.Combine(result.SessionDirectory, "1001 Artist - Song One", "bg.jpg");
        string backupFile2 = Path.Combine(result.SessionDirectory, "1002 Artist - Song Two", "background.png");
        Assert.True(File.Exists(backupFile1));
        Assert.True(File.Exists(backupFile2));
        Assert.Equal("Original Content 1", await File.ReadAllTextAsync(backupFile1));
        Assert.Equal("Original Content 2", await File.ReadAllTextAsync(backupFile2));

        // Check manifest.json
        string manifestPath = Path.Combine(result.SessionDirectory, "manifest.json");
        Assert.True(File.Exists(manifestPath));

        string manifestJson = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.Entries.Count);

        var entry1 = manifest.Entries.First(e => e.OriginalPath == Path.GetFullPath(file1));
        Assert.Equal(Path.GetFullPath(backupFile1), entry1.BackupPath);
        Assert.False(string.IsNullOrEmpty(entry1.Sha256));
        Assert.Equal(await BackupService.ComputeSha256Async(file1), entry1.Sha256);
    }

    [Fact]
    public async Task RestoreLastBackup_RestoresFilesAndVerifiesHashes()
    {
        // Setup
        string beatmap = Path.Combine(_songsDir, "2001 Artist - Song");
        Directory.CreateDirectory(beatmap);
        string file = Path.Combine(beatmap, "bg.jpg");
        await File.WriteAllTextAsync(file, "Original Background Art");

        // Create backup
        var backupResult = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);
        Assert.True(backupResult.Success);

        // Simulate replacement overwriting original
        await File.WriteAllTextAsync(file, "REPLACED IMAGE CONTENT");

        // Act: Undo / Restore
        var restoreResult = await _backupService.RestoreLastBackupAsync();

        // Assert
        Assert.True(restoreResult.HasBackupFound);
        Assert.Equal(1, restoreResult.RestoredCount);
        Assert.Equal(0, restoreResult.SkippedCount);
        Assert.Equal(0, restoreResult.FailedCount);

        // Verify original content was restored
        string restoredContent = await File.ReadAllTextAsync(file);
        Assert.Equal("Original Background Art", restoredContent);
    }

    [Fact]
    public async Task RestoreLastBackup_SkipsMissingFilesGracefully()
    {
        // Setup 2 files
        string beatmap1 = Path.Combine(_songsDir, "3001 Artist - MapA");
        string beatmap2 = Path.Combine(_songsDir, "3002 Artist - MapB");
        Directory.CreateDirectory(beatmap1);
        Directory.CreateDirectory(beatmap2);

        string file1 = Path.Combine(beatmap1, "bg.jpg");
        string file2 = Path.Combine(beatmap2, "bg.jpg");
        await File.WriteAllTextAsync(file1, "A Original");
        await File.WriteAllTextAsync(file2, "B Original");

        var backupResult = await _backupService.CreateBackupSessionAsync(new[] { file1, file2 }, _songsDir);

        // Simulate replacement
        await File.WriteAllTextAsync(file1, "A Replaced");
        await File.WriteAllTextAsync(file2, "B Replaced");

        // Delete backup of file2 to test missing file skipping
        string backupFile2 = Path.Combine(backupResult.SessionDirectory, "3002 Artist - MapB", "bg.jpg");
        File.Delete(backupFile2);

        // Act
        var restoreResult = await _backupService.RestoreLastBackupAsync();

        // Assert
        Assert.Equal(1, restoreResult.RestoredCount);
        Assert.Equal(1, restoreResult.SkippedCount);
        Assert.Equal(0, restoreResult.FailedCount);

        // file1 restored, file2 skipped (retains replaced version without crashing)
        Assert.Equal("A Original", await File.ReadAllTextAsync(file1));
        Assert.Equal("B Replaced", await File.ReadAllTextAsync(file2));
    }

    [Fact]
    public async Task RestoreLastBackup_DetectsHashMismatch_FailsGracefullyWithoutOverwriting()
    {
        // Setup
        string beatmap = Path.Combine(_songsDir, "4001 Artist - Song");
        Directory.CreateDirectory(beatmap);
        string file = Path.Combine(beatmap, "bg.jpg");
        await File.WriteAllTextAsync(file, "Original Clean Content");

        var backupResult = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        // Overwrite original
        await File.WriteAllTextAsync(file, "Replaced Content");

        // Corrupt the backup file
        string backupFile = Path.Combine(backupResult.SessionDirectory, "4001 Artist - Song", "bg.jpg");
        await File.WriteAllTextAsync(backupFile, "Corrupted Backup Byte Payload");

        // Act
        var restoreResult = await _backupService.RestoreLastBackupAsync();

        // Assert: should fail hash check and NOT overwrite original with corrupt data
        Assert.Equal(0, restoreResult.RestoredCount);
        Assert.Equal(0, restoreResult.SkippedCount);
        Assert.Equal(1, restoreResult.FailedCount);
        Assert.Single(restoreResult.Errors);
        Assert.Contains("Hash mismatch", restoreResult.Errors[0].ErrorMessage);

        // Original remains untouched (not overwritten with corrupt payload)
        Assert.Equal("Replaced Content", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task CreateBackupSession_HandlesIoErrorPerFile_WithoutAbortingWholeOperation()
    {
        string beatmap = Path.Combine(_songsDir, "5001 Artist - Song");
        Directory.CreateDirectory(beatmap);
        string validFile = Path.Combine(beatmap, "bg.jpg");
        await File.WriteAllTextAsync(validFile, "Valid File");
        string nonExistentFile = Path.Combine(beatmap, "does_not_exist.jpg");

        var files = new List<string> { nonExistentFile, validFile };

        // Act
        var result = await _backupService.CreateBackupSessionAsync(files, _songsDir);

        // Assert: non-existent file failed, but valid file was backed up successfully
        Assert.False(result.Success);
        Assert.Equal(1, result.BackedUpCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
        Assert.Equal(nonExistentFile, result.Errors[0].FilePath);

        string backupFile = Path.Combine(result.SessionDirectory, "5001 Artist - Song", "bg.jpg");
        Assert.True(File.Exists(backupFile));
    }

    [Fact]
    public async Task RestoreLastBackup_HandlesTargetIoError_WithoutAbortingRemainingFiles()
    {
        string beatmap1 = Path.Combine(_songsDir, "6001 Artist - Song1");
        string beatmap2 = Path.Combine(_songsDir, "6002 Artist - Song2");
        Directory.CreateDirectory(beatmap1);
        Directory.CreateDirectory(beatmap2);

        string file1 = Path.Combine(beatmap1, "bg.jpg");
        string file2 = Path.Combine(beatmap2, "bg.jpg");
        await File.WriteAllTextAsync(file1, "Content 1");
        await File.WriteAllTextAsync(file2, "Content 2");

        var backupResult = await _backupService.CreateBackupSessionAsync(new[] { file1, file2 }, _songsDir);

        // Modify both
        await File.WriteAllTextAsync(file1, "Replaced 1");
        await File.WriteAllTextAsync(file2, "Replaced 2");

        // Lock file1 by opening it exclusively with FileShare.None to induce an IO error during restore
        using var lockStream = new FileStream(file1, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act: Restore
        var restoreResult = await _backupService.RestoreLastBackupAsync();

        // Assert: file1 should fail due to IO lock, but file2 should be restored!
        Assert.Equal(1, restoreResult.RestoredCount);
        Assert.Equal(1, restoreResult.FailedCount);
        Assert.Single(restoreResult.Errors);
        Assert.Equal(file1, restoreResult.Errors[0].OriginalPath);

        // file2 was restored despite file1 failing
        Assert.Equal("Content 2", await File.ReadAllTextAsync(file2));
    }

    [Fact]
    public async Task RestoreLastBackup_NoBackupFound_ReturnsGracefulResult()
    {
        var emptyService = new BackupService(Path.Combine(_testRoot, "NonExistentBackups"));

        var result = await emptyService.RestoreLastBackupAsync();

        Assert.False(result.HasBackupFound);
        Assert.Equal(0, result.TotalEntries);
        Assert.Equal(0, result.RestoredCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public async Task CreateBackupSession_SupportsCancellationToken()
    {
        string beatmap = Path.Combine(_songsDir, "7001 Artist - Song");
        Directory.CreateDirectory(beatmap);
        string file = Path.Combine(beatmap, "bg.jpg");
        await File.WriteAllTextAsync(file, "Content");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir, cancellationToken: cts.Token);
        });
    }
}

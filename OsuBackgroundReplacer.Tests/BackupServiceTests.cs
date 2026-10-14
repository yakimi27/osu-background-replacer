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
                foreach (var file in Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task CreateBackupSession_PreservesRelativePathAndWritesManifest()
    {
        string beatmap1 = Path.Combine(_songsDir, "1001 Artist - Song One");
        string beatmap2 = Path.Combine(_songsDir, "1002 Artist - Song Two");
        Directory.CreateDirectory(beatmap1);
        Directory.CreateDirectory(beatmap2);

        string file1 = Path.Combine(beatmap1, "bg.jpg");
        string file2 = Path.Combine(beatmap2, "background.png");
        await File.WriteAllTextAsync(file1, "Original Content 1");
        await File.WriteAllTextAsync(file2, "Original Content 2");

        var files = new List<string> { file1, file2 };

        var result = await _backupService.CreateBackupSessionAsync(files, _songsDir);

        Assert.True(result.Success);
        Assert.Equal(2, result.BackedUpCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(Directory.Exists(result.SessionDirectory));

        string backupFile1 = Path.Combine(result.SessionDirectory, "1001 Artist - Song One", "bg.jpg");
        string backupFile2 = Path.Combine(result.SessionDirectory, "1002 Artist - Song Two", "background.png");
        Assert.True(File.Exists(backupFile1));
        Assert.True(File.Exists(backupFile2));
        Assert.Equal("Original Content 1", await File.ReadAllTextAsync(backupFile1));
        Assert.Equal("Original Content 2", await File.ReadAllTextAsync(backupFile2));

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
        string beatmap = Path.Combine(_songsDir, "2001 Artist - Song");
        Directory.CreateDirectory(beatmap);
        string file = Path.Combine(beatmap, "bg.jpg");
        await File.WriteAllTextAsync(file, "Original Background Art");

        var backupResult = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);
        Assert.True(backupResult.Success);

        await File.WriteAllTextAsync(file, "REPLACED IMAGE CONTENT");

        var restoreResult = await _backupService.RestoreLastBackupAsync();

        Assert.True(restoreResult.HasBackupFound);
        Assert.Equal(1, restoreResult.RestoredCount);
        Assert.Equal(0, restoreResult.SkippedCount);
        Assert.Equal(0, restoreResult.FailedCount);

        string restoredContent = await File.ReadAllTextAsync(file);
        Assert.Equal("Original Background Art", restoredContent);
    }

    [Fact]
    public async Task RestoreLastBackup_SkipsMissingFilesGracefully()
    {
        string beatmap1 = Path.Combine(_songsDir, "3001 Artist - MapA");
        string beatmap2 = Path.Combine(_songsDir, "3002 Artist - MapB");
        Directory.CreateDirectory(beatmap1);
        Directory.CreateDirectory(beatmap2);

        string file1 = Path.Combine(beatmap1, "bg.jpg");
        string file2 = Path.Combine(beatmap2, "bg.jpg");
        await File.WriteAllTextAsync(file1, "A Original");
        await File.WriteAllTextAsync(file2, "B Original");

        var backupResult = await _backupService.CreateBackupSessionAsync(new[] { file1, file2 }, _songsDir);

        await File.WriteAllTextAsync(file1, "A Replaced");
        await File.WriteAllTextAsync(file2, "B Replaced");

        string backupFile2 = Path.Combine(backupResult.SessionDirectory, "3002 Artist - MapB", "bg.jpg");
        File.Delete(backupFile2);

        var restoreResult = await _backupService.RestoreLastBackupAsync();

        Assert.Equal(1, restoreResult.RestoredCount);
        Assert.Equal(1, restoreResult.SkippedCount);
        Assert.Equal(0, restoreResult.FailedCount);

        Assert.Equal("A Original", await File.ReadAllTextAsync(file1));
        Assert.Equal("B Replaced", await File.ReadAllTextAsync(file2));
    }

    [Fact]
    public async Task RestoreLastBackup_DetectsHashMismatch_FailsGracefullyWithoutOverwriting()
    {
        string beatmap = Path.Combine(_songsDir, "4001 Artist - Song");
        Directory.CreateDirectory(beatmap);
        string file = Path.Combine(beatmap, "bg.jpg");
        await File.WriteAllTextAsync(file, "Original Clean Content");

        var backupResult = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        await File.WriteAllTextAsync(file, "Replaced Content");

        string backupFile = Path.Combine(backupResult.SessionDirectory, "4001 Artist - Song", "bg.jpg");
        await File.WriteAllTextAsync(backupFile, "Corrupted Backup Byte Payload");

        var restoreResult = await _backupService.RestoreLastBackupAsync();

        Assert.Equal(0, restoreResult.RestoredCount);
        Assert.Equal(0, restoreResult.SkippedCount);
        Assert.Equal(1, restoreResult.FailedCount);
        Assert.Single(restoreResult.Errors);
        Assert.Contains("Hash mismatch", restoreResult.Errors[0].ErrorMessage);

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

        var result = await _backupService.CreateBackupSessionAsync(files, _songsDir);

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

        await File.WriteAllTextAsync(file1, "Replaced 1");
        await File.WriteAllTextAsync(file2, "Replaced 2");

        using var lockStream = new FileStream(file1, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var restoreResult = await _backupService.RestoreLastBackupAsync();

        Assert.Equal(1, restoreResult.RestoredCount);
        Assert.Equal(1, restoreResult.FailedCount);
        Assert.Single(restoreResult.Errors);
        Assert.Equal(file1, restoreResult.Errors[0].OriginalPath);

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

    private sealed class CancelOnFirstReport : IProgress<BackupProgress>
    {
        private readonly CancellationTokenSource _cts;
        public CancelOnFirstReport(CancellationTokenSource cts) => _cts = cts;
        public void Report(BackupProgress value) => _cts.Cancel();
    }

    private async Task<string> MakeFile(string folder, string name, string content)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private void WriteSession(string name, DateTimeOffset timestamp, bool? completed, params string[] originals)
    {
        string dir = Path.Combine(_backupDir, name);
        Directory.CreateDirectory(dir);
        var manifest = new BackupManifest
        {
            Timestamp = timestamp,
            Completed = completed,
            Entries = originals.Select(o => new BackupManifestEntry { OriginalPath = o, BackupPath = Path.Combine(dir, "x"), Sha256 = "00" }).ToList()
        };
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
    }

    [Fact]
    public async Task SecondBackupRun_ThenUndo_RestoresOriginals()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8001 Map"), "bg.jpg", "ORIGINAL");

        var first = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);
        await File.WriteAllTextAsync(file, "REPLACED-1");

        var second = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);
        await File.WriteAllTextAsync(file, "REPLACED-2");

        Assert.True(second.Success);
        Assert.Equal(1, second.BackedUpCount);
        Assert.Equal(first.Entries[0].BackupPath, second.Entries[0].BackupPath);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(first.Entries[0].BackupPath));

        var restore = await _backupService.RestoreLastBackupAsync();

        Assert.Equal(1, restore.RestoredCount);
        Assert.Equal(0, restore.FailedCount);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task FailedBackup_FileIsNotReportedAsBackedUp()
    {
        string good = await MakeFile(Path.Combine(_songsDir, "8101 Map"), "bg.jpg", "GOOD");
        string locked = await MakeFile(Path.Combine(_songsDir, "8102 Map"), "bg.jpg", "LOCKED");

        using var lockStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await _backupService.CreateBackupSessionAsync(new[] { good, locked }, _songsDir);

        Assert.False(result.Success);
        Assert.True(result.IsBackedUp(good));
        Assert.False(result.IsBackedUp(locked));
        Assert.Contains(result.Errors, e => e.FilePath == locked);
    }

    [Fact]
    public async Task CancelledBackup_IsIgnoredByUndoAndLatestLookup()
    {
        string a = await MakeFile(Path.Combine(_songsDir, "8201 Map"), "bg.jpg", "A");
        string b = await MakeFile(Path.Combine(_songsDir, "8202 Map"), "bg.jpg", "B");

        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _backupService.CreateBackupSessionAsync(new[] { a, b }, _songsDir, new CancelOnFirstReport(cts), cts.Token));

        string sessionDir = Directory.GetDirectories(_backupDir).Single();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(
            await File.ReadAllTextAsync(Path.Combine(sessionDir, "manifest.json")))!;
        Assert.Equal(false, manifest.Completed);

        Assert.Null(_backupService.GetLatestSessionDirectory());
        Assert.Empty(_backupService.GetBackupSessions());

        await File.WriteAllTextAsync(a, "REPLACED");
        var restore = await _backupService.RestoreLastBackupAsync();
        Assert.False(restore.HasBackupFound);
        Assert.Equal("REPLACED", await File.ReadAllTextAsync(a));

        var explicitRestore = await _backupService.RestoreBackupSessionAsync(sessionDir);
        Assert.Equal(0, explicitRestore.RestoredCount);
        Assert.Equal(1, explicitRestore.FailedCount);
    }

    [Fact]
    public async Task CompletedBackup_WritesCompletedFlag()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8301 Map"), "bg.jpg", "X");
        var result = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        var manifest = JsonSerializer.Deserialize<BackupManifest>(
            await File.ReadAllTextAsync(Path.Combine(result.SessionDirectory, "manifest.json")))!;
        Assert.Equal(true, manifest.Completed);
    }

    [Fact]
    public async Task BackupDisabledRun_UndoTargetsOlderSession_AndPreviewShowsIt()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8401 Map"), "bg.jpg", "ORIGINAL");
        var backup = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        await File.WriteAllTextAsync(file, "REPLACED-NO-BACKUP");

        var info = _backupService.GetLatestSessionInfo();
        Assert.NotNull(info);
        Assert.Equal(backup.SessionDirectory, info!.Directory);
        Assert.Equal(1, info.FileCount);
        Assert.Equal(backup.Timestamp, info.Timestamp);
    }

    [Fact]
    public async Task MovedSongsFolder_WithSubfolders_RestoresIntoCurrentSongsFolder()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8501 Map", "sub", "deeper"), "bg.jpg", "ORIGINAL");
        string sibling = await MakeFile(Path.Combine(_songsDir, "8502 Map", "sub"), "bg.jpg", "SIBLING");

        var backup = await _backupService.CreateBackupSessionAsync(new[] { file, sibling }, _songsDir);
        Assert.True(backup.Success);
        Assert.Equal(Path.Combine("8501 Map", "sub", "deeper", "bg.jpg"), backup.Entries[0].RelativePath);

        string movedSongs = Path.Combine(_testRoot, "MovedSongs");
        Directory.Move(_songsDir, movedSongs);
        string movedFile = Path.Combine(movedSongs, "8501 Map", "sub", "deeper", "bg.jpg");
        string movedSibling = Path.Combine(movedSongs, "8502 Map", "sub", "bg.jpg");
        await File.WriteAllTextAsync(movedFile, "REPLACED");
        await File.WriteAllTextAsync(movedSibling, "REPLACED");

        var restore = await _backupService.RestoreLastBackupAsync(songsFolderPath: movedSongs);

        Assert.Equal(2, restore.RestoredCount);
        Assert.Equal(0, restore.FailedCount);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(movedFile));
        Assert.Equal("SIBLING", await File.ReadAllTextAsync(movedSibling));
        Assert.False(Directory.Exists(_songsDir));
    }

    [Fact]
    public async Task MovedSongsFolder_SecondBackupRunStillKeepsOriginal()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8601 Map"), "bg.jpg", "ORIGINAL");
        await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        string movedSongs = Path.Combine(_testRoot, "MovedSongs2");
        Directory.Move(_songsDir, movedSongs);
        string movedFile = Path.Combine(movedSongs, "8601 Map", "bg.jpg");
        await File.WriteAllTextAsync(movedFile, "REPLACED");

        await _backupService.CreateBackupSessionAsync(new[] { movedFile }, movedSongs);
        await File.WriteAllTextAsync(movedFile, "REPLACED-AGAIN");

        await _backupService.RestoreLastBackupAsync(songsFolderPath: movedSongs);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(movedFile));
    }

    [Fact]
    public async Task RestoreRejectsRelativePathEscapingSongsFolder()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8701 Map"), "bg.jpg", "ORIGINAL");
        var backup = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        string manifestPath = Path.Combine(backup.SessionDirectory, "manifest.json");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath))!;
        manifest.Entries[0].RelativePath = Path.Combine("..", "evil", "bg.jpg");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        var restore = await _backupService.RestoreLastBackupAsync(songsFolderPath: _songsDir);
        Assert.Equal(0, restore.RestoredCount);
        Assert.Equal(1, restore.SkippedCount);
        Assert.Equal(0, restore.FailedCount);
        Assert.Equal(RestoreStatus.Skipped, restore.Items[0].Status);
        Assert.Contains("invalid traversal", restore.Items[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_testRoot, "evil", "bg.jpg")));
    }

    [Fact]
    public async Task Restore_TamperedManifestWithDotDotTraversal_SkipsAndDoesNotThrow()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8702 Map"), "bg.jpg", "ORIGINAL");
        var backup = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        string manifestPath = Path.Combine(backup.SessionDirectory, "manifest.json");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath))!;
        manifest.Entries[0].RelativePath = "8702 Map/../../evil.jpg";
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        var restore = await _backupService.RestoreLastBackupAsync(songsFolderPath: _songsDir);
        Assert.Equal(0, restore.RestoredCount);
        Assert.Equal(1, restore.SkippedCount);
        Assert.Equal(0, restore.FailedCount);
        Assert.Equal(RestoreStatus.Skipped, restore.Items[0].Status);
        Assert.Contains("invalid traversal", restore.Items[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_testRoot, "evil.jpg")));
    }

    [Fact]
    public async Task Restore_TamperedManifestWithAbsoluteOutsideSongs_SkipsAndDoesNotThrow()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8703 Map"), "bg.jpg", "ORIGINAL");
        var backup = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        string outsideFile = Path.Combine(_testRoot, "Outside", "pwned.jpg");
        string manifestPath = Path.Combine(backup.SessionDirectory, "manifest.json");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath))!;
        manifest.Entries[0].RelativePath = null;
        manifest.Entries[0].OriginalPath = outsideFile;
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        var restore = await _backupService.RestoreLastBackupAsync(songsFolderPath: _songsDir);
        Assert.Equal(0, restore.RestoredCount);
        Assert.Equal(1, restore.SkippedCount);
        Assert.Equal(0, restore.FailedCount);
        Assert.Equal(RestoreStatus.Skipped, restore.Items[0].Status);
        Assert.Contains("outside the selected Songs folder", restore.Items[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outsideFile));
    }

    [Fact]
    public async Task Restore_TamperedManifestWithNonImageExtension_SkipsAndDoesNotThrow()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8704 Map"), "bg.jpg", "ORIGINAL");
        var backup = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);

        string manifestPath = Path.Combine(backup.SessionDirectory, "manifest.json");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath))!;
        manifest.Entries[0].RelativePath = "8704 Map/malicious.exe";
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        var restore = await _backupService.RestoreLastBackupAsync(songsFolderPath: _songsDir);
        Assert.Equal(0, restore.RestoredCount);
        Assert.Equal(1, restore.SkippedCount);
        Assert.Equal(0, restore.FailedCount);
        Assert.Equal(RestoreStatus.Skipped, restore.Items[0].Status);
        Assert.Contains("does not have a supported image extension", restore.Items[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_songsDir, "8704 Map", "malicious.exe")));
    }

    [Fact]
    public async Task Restore_ValidManifestWithSupportedImageExtensions_SuccessfullyRestores()
    {
        string mapDir = Path.Combine(_songsDir, "8705 MultiExt Map");
        string jpg = await MakeFile(mapDir, "bg1.jpg", "JPG_ORIGINAL");
        string png = await MakeFile(mapDir, "bg2.png", "PNG_ORIGINAL");
        string bmp = await MakeFile(mapDir, "bg3.bmp", "BMP_ORIGINAL");
        string gif = await MakeFile(mapDir, "bg4.gif", "GIF_ORIGINAL");

        var backup = await _backupService.CreateBackupSessionAsync(new[] { jpg, png, bmp, gif }, _songsDir);
        Assert.True(backup.Success);
        Assert.Equal(4, backup.BackedUpCount);

        await File.WriteAllTextAsync(jpg, "REPLACED");
        await File.WriteAllTextAsync(png, "REPLACED");
        await File.WriteAllTextAsync(bmp, "REPLACED");
        await File.WriteAllTextAsync(gif, "REPLACED");

        var restore = await _backupService.RestoreLastBackupAsync(songsFolderPath: _songsDir);
        Assert.Equal(4, restore.RestoredCount);
        Assert.Equal(0, restore.SkippedCount);
        Assert.Equal(0, restore.FailedCount);

        Assert.Equal("JPG_ORIGINAL", await File.ReadAllTextAsync(jpg));
        Assert.Equal("PNG_ORIGINAL", await File.ReadAllTextAsync(png));
        Assert.Equal("BMP_ORIGINAL", await File.ReadAllTextAsync(bmp));
        Assert.Equal("GIF_ORIGINAL", await File.ReadAllTextAsync(gif));
    }

    [Fact]
    public async Task LegacyManifest_WithAbsolutePathsOnly_StillRestores()
    {
        string file = await MakeFile(Path.Combine(_songsDir, "8801 Map"), "bg.jpg", "ORIGINAL");
        var backup = await _backupService.CreateBackupSessionAsync(new[] { file }, _songsDir);
        await File.WriteAllTextAsync(file, "REPLACED");

        string manifestPath = Path.Combine(backup.SessionDirectory, "manifest.json");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath))!;
        manifest.Completed = null;
        manifest.Entries[0].RelativePath = null;
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        var restore = await _backupService.RestoreLastBackupAsync(songsFolderPath: _songsDir);

        Assert.Equal(1, restore.RestoredCount);
        Assert.Equal(0, restore.SkippedCount);
        Assert.Equal(0, restore.FailedCount);
        Assert.Equal("ORIGINAL", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public void GetBackupSessions_AndLatest_UseManifestTimestampOrdering()
    {
        var now = DateTimeOffset.Now;
        WriteSession("zzz-oldest", now.AddHours(-3), true, "C:\\a.jpg");
        WriteSession("aaa-newest", now, true, "C:\\a.jpg", "C:\\b.jpg");
        WriteSession("mmm-middle", now.AddHours(-1), true, "C:\\a.jpg");
        WriteSession("ppp-incomplete-newer", now.AddHours(1), false, "C:\\a.jpg");

        var sessions = _backupService.GetBackupSessions();

        Assert.Equal(new[] { "aaa-newest", "mmm-middle", "zzz-oldest" }, sessions.Select(Path.GetFileName));
        Assert.Equal(sessions[0], _backupService.GetLatestSessionDirectory());
        Assert.Equal(2, _backupService.GetLatestSessionInfo()!.FileCount);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Services;
using Xunit;

namespace OsuBackgroundReplacer.Tests;

public class BackupServiceRetentionTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _backupDir;
    private readonly string _songsDir;
    private readonly BackupService _backupService;

    public BackupServiceRetentionTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "RetentionTests_" + Guid.NewGuid().ToString("N"));
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
        catch { }
    }

    [Fact]
    public void CleanupOldSessions_WhenKeepLastNIsZero_DeletesNothing()
    {
        // Setup 3 fake completed sessions
        for (int i = 1; i <= 3; i++)
        {
            CreateFakeCompletedSession($"session_{i}", i, new List<(string name, string content)>
            {
                ($"bg_{i}.jpg", $"content_{i}")
            });
        }

        var deleted = _backupService.CleanupOldSessions(0);

        Assert.Empty(deleted);
        Assert.Equal(3, _backupService.GetBackupSessions().Count);
    }

    [Fact]
    public void CleanupOldSessions_WhenCountWithinLimit_DeletesNothing()
    {
        for (int i = 1; i <= 3; i++)
        {
            CreateFakeCompletedSession($"session_{i}", i, new List<(string name, string content)>
            {
                ($"bg_{i}.jpg", $"content_{i}")
            });
        }

        var deleted = _backupService.CleanupOldSessions(5);

        Assert.Empty(deleted);
        Assert.Equal(3, _backupService.GetBackupSessions().Count);
    }

    [Fact]
    public void CleanupOldSessions_NeverDeletesSessionWithSoleCopyOfOriginalFile()
    {
        // Session 1 has a unique original file "original_solo.jpg" (oldest, 40 min ago)
        string s1 = CreateFakeCompletedSession("session_1", 40, new List<(string name, string content)>
        {
            ("original_solo.jpg", "Sole original content that must never be lost")
        });

        // Sessions 2, 3, 4 only reference Session 1's file and have no physical files
        string s2 = CreateFakeReferencingSession("session_2", 30, s1, "original_solo.jpg", "Sole original content that must never be lost");
        string s3 = CreateFakeReferencingSession("session_3", 20, s1, "original_solo.jpg", "Sole original content that must never be lost");
        string s4 = CreateFakeReferencingSession("session_4", 10, s1, "original_solo.jpg", "Sole original content that must never be lost");

        // Keep last 2 sessions (which would nominally delete session_1 and session_2)
        var deleted = _backupService.CleanupOldSessions(2);

        // Session 1 contains the only copy of "original_solo.jpg", so it MUST NOT be deleted!
        Assert.DoesNotContain(s1, deleted);
        Assert.True(Directory.Exists(s1));

        // Session 2 does not contain any physical files (or unique copies), so it is safely deleted!
        Assert.Contains(s2, deleted);
        Assert.False(Directory.Exists(s2));

        // Sessions 3 and 4 are the last 2 and are kept
        Assert.True(Directory.Exists(s3));
        Assert.True(Directory.Exists(s4));
    }

    [Fact]
    public void CleanupOldSessions_CanDeleteOlderSessionIfNewerKeptSessionHasCopy()
    {
        // Session 1 has file A (oldest, 30 min ago)
        string s1 = CreateFakeCompletedSession("session_1", 30, new List<(string name, string content)>
        {
            ("bg.jpg", "Identical Content")
        });

        // Session 2 has an independent physical copy of file A (20 min ago)
        string s2 = CreateFakeCompletedSession("session_2", 20, new List<(string name, string content)>
        {
            ("bg.jpg", "Identical Content")
        });

        // Session 3 (newest, 10 min ago)
        string s3 = CreateFakeCompletedSession("session_3", 10, new List<(string name, string content)>
        {
            ("bg.jpg", "Identical Content")
        });

        // Keep last 2 (Session 2 and Session 3)
        var deleted = _backupService.CleanupOldSessions(2);

        // Session 1 can be deleted because Session 2 and 3 also have physical copies of identical content!
        Assert.Contains(s1, deleted);
        Assert.False(Directory.Exists(s1));
        Assert.True(Directory.Exists(s2));
        Assert.True(Directory.Exists(s3));
    }

    private string CreateFakeCompletedSession(string dirName, int minutesAgo, List<(string name, string content)> files)
    {
        string dir = Path.Combine(_backupDir, dirName);
        Directory.CreateDirectory(dir);

        var entries = new List<BackupManifestEntry>();
        DateTimeOffset timestamp = DateTimeOffset.Now.AddMinutes(-minutesAgo);

        foreach (var (name, content) in files)
        {
            string filePath = Path.Combine(dir, name);
            File.WriteAllText(filePath, content);
            string sha = BackupService.ComputeSha256(filePath);

            entries.Add(new BackupManifestEntry
            {
                OriginalPath = Path.Combine(_songsDir, name),
                BackupPath = filePath,
                RelativePath = name,
                Sha256 = sha,
                Timestamp = timestamp
            });
        }

        var manifest = new BackupManifest
        {
            Completed = true,
            Timestamp = timestamp,
            Entries = entries
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        return dir;
    }

    private string CreateFakeReferencingSession(string dirName, int minutesAgo, string sourceSessionDir, string fileName, string content)
    {
        string dir = Path.Combine(_backupDir, dirName);
        Directory.CreateDirectory(dir);

        DateTimeOffset timestamp = DateTimeOffset.Now.AddMinutes(-minutesAgo);
        string referencedFilePath = Path.Combine(sourceSessionDir, fileName);
        string sha = BackupService.ComputeSha256(referencedFilePath);

        var entries = new List<BackupManifestEntry>
        {
            new BackupManifestEntry
            {
                OriginalPath = Path.Combine(_songsDir, fileName),
                BackupPath = referencedFilePath, // Points to sourceSessionDir, no file in dir!
                RelativePath = fileName,
                Sha256 = sha,
                Timestamp = timestamp
            }
        };

        var manifest = new BackupManifest
        {
            Completed = true,
            Timestamp = timestamp,
            Entries = entries
        };

        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(manifest));
        return dir;
    }
}

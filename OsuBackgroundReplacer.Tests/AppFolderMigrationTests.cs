using System;
using System.IO;
using OsuBackgroundReplacerMain.Services;
using Xunit;

namespace OsuBackgroundReplacer.Tests;

public class AppFolderMigrationTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly string _legacyRoot;
    private readonly string _targetRoot;

    public AppFolderMigrationTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "MigrationTests_" + Guid.NewGuid().ToString("N"));
        _legacyRoot = Path.Combine(_testBaseDir, "yakimi27", "OsuBackgroundReplacer");
        _targetRoot = Path.Combine(_testBaseDir, "OsuBackgroundReplacer");
        Directory.CreateDirectory(_legacyRoot);
        Directory.CreateDirectory(_targetRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBaseDir))
            {
                Directory.Delete(_testBaseDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void DefaultBackupDirectory_UsesUnifiedRootWithoutYakimi27()
    {
        string defaultDir = BackupService.GetDefaultBackupDirectory();
        Assert.DoesNotContain("yakimi27", defaultDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("OsuBackgroundReplacer", "Backups"), defaultDir);
    }

    [Fact]
    public void MigrateLegacyFolders_WhenLegacyBackupsExist_MigratesToTarget()
    {
        string legacyBackups = Path.Combine(_legacyRoot, "Backups");
        string sessionDir = Path.Combine(legacyBackups, "2026-01-01_10-00-00");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "manifest.json"), "{\"entries\":[]}");
        File.WriteAllText(Path.Combine(sessionDir, "bg.jpg"), "test image data");

        SettingsService.MigrateLegacyFolders(_legacyRoot, _targetRoot);

        string targetSessionDir = Path.Combine(_targetRoot, "Backups", "2026-01-01_10-00-00");
        Assert.True(Directory.Exists(targetSessionDir));
        Assert.True(File.Exists(Path.Combine(targetSessionDir, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(targetSessionDir, "bg.jpg")));
        Assert.False(Directory.Exists(_legacyRoot));
    }

    [Fact]
    public void MigrateLegacyFolders_WhenLegacySettingsExistAndTargetDoesNot_MovesSettings()
    {
        string legacySettings = Path.Combine(_legacyRoot, "settings.json");
        File.WriteAllText(legacySettings, "{\"ConfirmBeforeReplace\": false}");

        string targetSettings = Path.Combine(_targetRoot, "settings.json");
        if (File.Exists(targetSettings))
        {
            File.Delete(targetSettings);
        }

        SettingsService.MigrateLegacyFolders(_legacyRoot, _targetRoot);

        Assert.True(File.Exists(targetSettings));
        Assert.Contains("ConfirmBeforeReplace", File.ReadAllText(targetSettings));
        Assert.False(File.Exists(legacySettings));
    }

    [Fact]
    public void MigrateLegacyFolders_WhenTargetSettingsAlreadyExist_PreservesTargetSettings()
    {
        string legacySettings = Path.Combine(_legacyRoot, "settings.json");
        File.WriteAllText(legacySettings, "{\"ConfirmBeforeReplace\": false}");

        string targetSettings = Path.Combine(_targetRoot, "settings.json");
        File.WriteAllText(targetSettings, "{\"ConfirmBeforeReplace\": true}");

        SettingsService.MigrateLegacyFolders(_legacyRoot, _targetRoot);

        Assert.True(File.Exists(targetSettings));
        Assert.Contains("true", File.ReadAllText(targetSettings));
        Assert.False(File.Exists(legacySettings));
    }
}

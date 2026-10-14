using System;
using System.IO;
using OsuBackgroundReplacerMain.Logic;
using OsuBackgroundReplacerMain.Services;
using Xunit;

namespace OsuBackgroundReplacer.Tests;

public class OperationsPathTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _originalSettingsPath;

    public OperationsPathTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "OperationsPathTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _originalSettingsPath = SettingsService.SettingsFilePath;
        SettingsService.SettingsFilePath = Path.Combine(_tempDirectory, "settings.json");
        SettingsService.CustomSongsDetector = null;
        SettingsService.IsFolderClearedInSession = false;

        // Reset state
        ImageOperations.setPath(null);
        FolderOperations.setPath(null);
        SettingsService.IsFolderClearedInSession = false;
    }

    public void Dispose()
    {
        try
        {
            ImageOperations.setPath(null);
            FolderOperations.setPath(null);
            SettingsService.CustomSongsDetector = null;
            SettingsService.IsFolderClearedInSession = false;
            SettingsService.SettingsFilePath = _originalSettingsPath;

            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void ImageOperations_SetPath_ThenClear_ReturnsNull()
    {
        string imagePath = Path.Combine(_tempDirectory, "test.png");
        File.WriteAllText(imagePath, "image content");

        ImageOperations.setPath(imagePath);
        Assert.Equal(imagePath, ImageOperations.getPath());
        Assert.Equal(imagePath, SettingsService.Current.LastImagePath);

        ImageOperations.setPath(null);
        Assert.Null(ImageOperations.getPath());
        Assert.Equal(string.Empty, SettingsService.Current.LastImagePath);
    }

    [Fact]
    public void ImageOperations_SetPath_ThenClear_ReloadSettings_ReturnsNull()
    {
        string imagePath = Path.Combine(_tempDirectory, "test_reload.png");
        File.WriteAllText(imagePath, "image content");

        ImageOperations.setPath(imagePath);
        Assert.Equal(imagePath, ImageOperations.getPath());

        ImageOperations.setPath(null);
        Assert.Null(ImageOperations.getPath());

        // Reload settings from disk
        SettingsService.Load();
        Assert.Equal(string.Empty, SettingsService.Current.LastImagePath);
        Assert.Null(ImageOperations.getPath());
    }

    [Fact]
    public void FolderOperations_SetPath_ThenClear_ReturnsNull()
    {
        string folderPath = Path.Combine(_tempDirectory, "Songs");
        Directory.CreateDirectory(folderPath);

        FolderOperations.setPath(folderPath);
        Assert.Equal(folderPath, FolderOperations.getPath());
        Assert.Equal(folderPath, SettingsService.Current.LastFolderPath);

        FolderOperations.setPath(null);
        Assert.Null(FolderOperations.getPath());
        Assert.Equal(string.Empty, SettingsService.Current.LastFolderPath);
    }

    [Fact]
    public void FolderOperations_SetPath_ThenClear_ReloadSettings_ReturnsNull()
    {
        string folderPath = Path.Combine(_tempDirectory, "Songs_Reload");
        Directory.CreateDirectory(folderPath);

        FolderOperations.setPath(folderPath);
        Assert.Equal(folderPath, FolderOperations.getPath());

        FolderOperations.setPath(null);
        Assert.Null(FolderOperations.getPath());

        // Reload settings from disk
        SettingsService.Load();
        Assert.Equal(string.Empty, SettingsService.Current.LastFolderPath);
        Assert.Null(FolderOperations.getPath());
    }

    [Fact]
    public void FolderOperations_Clear_PreventsAutoDetectInSameSession_EvenAfterReload()
    {
        string detectedDir = Path.Combine(_tempDirectory, "AutoDetectedSongs");
        Directory.CreateDirectory(detectedDir);
        SettingsService.CustomSongsDetector = () => detectedDir;

        string initialDir = Path.Combine(_tempDirectory, "InitialSongs");
        Directory.CreateDirectory(initialDir);

        FolderOperations.setPath(initialDir);
        Assert.Equal(initialDir, FolderOperations.getPath());

        // User explicitly clears folder in this session
        FolderOperations.setPath(null);
        Assert.Null(FolderOperations.getPath());
        Assert.Equal(string.Empty, SettingsService.Current.LastFolderPath);

        // Reloading settings in the same session must NOT re-detect
        SettingsService.Load();
        Assert.Equal(string.Empty, SettingsService.Current.LastFolderPath);
        Assert.Null(FolderOperations.getPath());
    }

    [Fact]
    public void FolderOperations_AutoDetectOnLaunch_WhenNotClearedInSession()
    {
        string detectedDir = Path.Combine(_tempDirectory, "AutoDetectedSongs");
        Directory.CreateDirectory(detectedDir);
        SettingsService.CustomSongsDetector = () => detectedDir;

        // Fresh launch state (IsFolderClearedInSession is false, LastFolderPath is empty)
        SettingsService.IsFolderClearedInSession = false;
        SettingsService.Current.LastFolderPath = string.Empty;
        SettingsService.Save();

        // Load runs on launch
        SettingsService.Load();
        Assert.Equal(detectedDir, SettingsService.Current.LastFolderPath);
        Assert.Equal(detectedDir, FolderOperations.getPath());
    }

    [Fact]
    public void ClearPath_FiresPathChangedEvents()
    {
        bool imageChangedFired = false;
        bool folderChangedFired = false;

        Action onImagePathChanged = () => imageChangedFired = true;
        Action onFolderPathChanged = () => folderChangedFired = true;

        ImageOperations.PathChanged += onImagePathChanged;
        FolderOperations.PathChanged += onFolderPathChanged;

        try
        {
            ImageOperations.setPath("test.png");
            imageChangedFired = false;
            ImageOperations.setPath(null);
            Assert.True(imageChangedFired);

            FolderOperations.setPath("test_folder");
            folderChangedFired = false;
            FolderOperations.setPath(null);
            Assert.True(folderChangedFired);
        }
        finally
        {
            ImageOperations.PathChanged -= onImagePathChanged;
            FolderOperations.PathChanged -= onFolderPathChanged;
        }
    }
}

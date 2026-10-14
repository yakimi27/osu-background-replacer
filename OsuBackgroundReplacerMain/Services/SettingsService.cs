using System;
using System.IO;
using System.Text.Json;

namespace OsuBackgroundReplacerMain.Services
{
    public class SettingsData
    {
        public bool ConfirmBeforeReplace { get; set; } = true;
        public bool SaveLastFolderPath { get; set; } = true;
        public bool SaveLastImagePath { get; set; } = true;
        public string LastFolderPath { get; set; } = string.Empty;
        public string LastImagePath { get; set; } = string.Empty;
        public string AppTheme { get; set; } = "Default";
        public bool BackupOriginalBackgrounds { get; set; } = false;
        public int KeepLastNBackupSessions { get; set; } = 5;
    }

    public static class SettingsService
    {
        private static string _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OsuBackgroundReplacer");

        private static string _settingsFilePath = Path.Combine(_settingsDir, "settings.json");

        public static string SettingsFilePath
        {
            get => _settingsFilePath;
            set
            {
                _settingsFilePath = value;
                _settingsDir = Path.GetDirectoryName(value) ?? _settingsDir;
            }
        }

        public static bool IsFolderClearedInSession { get; set; } = false;
        public static Func<string?>? CustomSongsDetector { get; set; }

        public static SettingsData Current { get; private set; } = new SettingsData();

        public static event Action? SettingsChanged;

        static SettingsService()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var data = JsonSerializer.Deserialize<SettingsData>(json);
                    if (data != null)
                    {
                        Current = data;
                    }
                }
            }
            catch
            {
                Current = new SettingsData();
            }

            if (!IsFolderClearedInSession && string.IsNullOrWhiteSpace(Current.LastFolderPath))
            {
                string? detected = TryDetectOsuSongsPath();
                if (!string.IsNullOrWhiteSpace(detected))
                {
                    Current.LastFolderPath = detected;
                }
            }
        }

        public static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsFilePath) ?? _settingsDir;
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
                SettingsChanged?.Invoke();
            }
            catch
            {
            }
        }

        public static string? TryDetectOsuSongsPath()
        {
            if (CustomSongsDetector != null)
            {
                return CustomSongsDetector();
            }

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string path1 = Path.Combine(localAppData, "osu!", "Songs");
                if (Directory.Exists(path1)) return path1;

                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string path2 = Path.Combine(progFiles, "osu!", "Songs");
                if (Directory.Exists(path2)) return path2;

                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string path3 = Path.Combine(progFilesX86, "osu!", "Songs");
                if (Directory.Exists(path3)) return path3;

                string[] commonRoots = { @"C:\osu!\Songs", @"D:\osu!\Songs", @"E:\osu!\Songs", @"F:\osu!\Songs" };
                foreach (var root in commonRoots)
                {
                    if (Directory.Exists(root)) return root;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}

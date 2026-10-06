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
        public string AppTheme { get; set; } = "Default"; // "Default", "Light", "Dark"
    }

    public static class SettingsService
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OsuBackgroundReplacer");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

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

            if (string.IsNullOrWhiteSpace(Current.LastFolderPath))
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
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }

                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
                SettingsChanged?.Invoke();
            }
            catch
            {
                // silently handle any write issues
            }
        }

        public static string? TryDetectOsuSongsPath()
        {
            try
            {
                // 1. default osu! install in LocalAppData
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string path1 = Path.Combine(localAppData, "osu!", "Songs");
                if (Directory.Exists(path1)) return path1;

                // 2. program files
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string path2 = Path.Combine(progFiles, "osu!", "Songs");
                if (Directory.Exists(path2)) return path2;

                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string path3 = Path.Combine(progFilesX86, "osu!", "Songs");
                if (Directory.Exists(path3)) return path3;

                // 3. root drives
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

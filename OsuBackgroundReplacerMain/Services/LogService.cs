using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Models;

namespace OsuBackgroundReplacerMain.Services
{
    public static class LogService
    {
        public static ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        public static event Action? EntriesChanged;

        public static int TotalReplacedCount => Entries.Count(e => e.IsSuccess);
        public static DateTime? LastRunTime { get; private set; }

        public static void AddEntry(string filePath, bool success = true, string? error = null)
        {
            var entry = LogEntry.CreateFromFilePath(filePath, success, error);
            Entries.Insert(0, entry); // newest first
            LastRunTime = DateTime.Now;
            EntriesChanged?.Invoke();
        }

        public static void AddEntries(IEnumerable<string> filePaths)
        {
            foreach (var file in filePaths)
            {
                var entry = LogEntry.CreateFromFilePath(file, true, null);
                Entries.Insert(0, entry);
            }
            LastRunTime = DateTime.Now;
            EntriesChanged?.Invoke();
        }

        public static void Clear()
        {
            Entries.Clear();
            LastRunTime = null;
            EntriesChanged?.Invoke();
        }

        public static async Task<string> ExportToFileAsync(string targetFilePath)
        {
            var lines = Entries.Select(e =>
                $"[{e.FormattedTime}] [{(e.IsSuccess ? "SUCCESS" : "ERROR")}] {e.FolderName} -> {e.FileName} ({e.FilePath})" +
                (string.IsNullOrEmpty(e.ErrorMessage) ? "" : $" Reason: {e.ErrorMessage}")
            );

            await File.WriteAllLinesAsync(targetFilePath, lines);
            return targetFilePath;
        }
    }
}

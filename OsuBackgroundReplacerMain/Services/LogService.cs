using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Models;

namespace OsuBackgroundReplacerMain.Services
{
    public static class LogService
    {
        private static readonly object _lock = new();
        private static readonly ConcurrentQueue<LogEntry> _pendingEntries = new();
        private static readonly Timer _flushTimer = new(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        private static int _isFlushScheduled = 0;

        private static int _successCount = 0;
        private static int _failureCount = 0;

        public static ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        public static List<LogEntry> GetEntriesSnapshot()
        {
            lock (_lock)
            {
                return Entries.ToList();
            }
        }

        public static event Action? EntriesChanged;

        public static int MaxEntries { get; set; } = 5000;

        public static int TotalReplacedCount => _successCount;
        public static int TotalSuccessCount => _successCount;
        public static int TotalFailureCount => _failureCount;
        public static DateTime? LastRunTime { get; private set; }

        public static void AddEntry(string filePath, bool success = true, string? error = null)
        {
            var entry = LogEntry.CreateFromFilePath(filePath, success, error);

            if (success)
            {
                Interlocked.Increment(ref _successCount);
            }
            else
            {
                Interlocked.Increment(ref _failureCount);
            }

            LastRunTime = DateTime.Now;
            _pendingEntries.Enqueue(entry);

            ScheduleFlush();
        }

        public static void AddEntries(IEnumerable<string> filePaths)
        {
            foreach (var file in filePaths)
            {
                var entry = LogEntry.CreateFromFilePath(file, true, null);
                Interlocked.Increment(ref _successCount);
                _pendingEntries.Enqueue(entry);
            }

            LastRunTime = DateTime.Now;
            ScheduleFlush();
        }

        private static void ScheduleFlush()
        {
            if (Interlocked.CompareExchange(ref _isFlushScheduled, 1, 0) == 0)
            {
                _flushTimer.Change(100, Timeout.Infinite);
            }
        }

        public static void Flush()
        {
            _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Interlocked.Exchange(ref _isFlushScheduled, 0);

            if (_pendingEntries.IsEmpty)
            {
                return;
            }

            var drained = new List<LogEntry>();
            while (_pendingEntries.TryDequeue(out var entry))
            {
                drained.Add(entry);
            }

            if (drained.Count == 0)
            {
                return;
            }

            lock (_lock)
            {
                // Insert drained entries in reverse order so the newest stays at index 0
                for (int i = 0; i < drained.Count; i++)
                {
                    Entries.Insert(i, drained[drained.Count - 1 - i]);
                }

                while (Entries.Count > MaxEntries)
                {
                    Entries.RemoveAt(Entries.Count - 1);
                }
            }

            EntriesChanged?.Invoke();
        }

        public static void Clear()
        {
            _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Interlocked.Exchange(ref _isFlushScheduled, 0);

            while (_pendingEntries.TryDequeue(out _)) { }

            lock (_lock)
            {
                Entries.Clear();
            }

            Interlocked.Exchange(ref _successCount, 0);
            Interlocked.Exchange(ref _failureCount, 0);
            LastRunTime = null;
            EntriesChanged?.Invoke();
        }

        public static async Task<string> ExportToFileAsync(string targetFilePath)
        {
            Flush();

            List<string> lines;
            lock (_lock)
            {
                lines = Entries.Select(e =>
                    $"[{e.FormattedTime}] [{(e.IsSuccess ? "SUCCESS" : "ERROR")}] {e.FolderName} -> {e.FileName} ({e.FilePath})" +
                    (string.IsNullOrEmpty(e.ErrorMessage) ? "" : $" Reason: {e.ErrorMessage}")
                ).ToList();
            }

            await File.WriteAllLinesAsync(targetFilePath, lines);
            return targetFilePath;
        }
    }
}

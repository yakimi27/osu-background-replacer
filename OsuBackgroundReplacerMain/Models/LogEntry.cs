using System;
using System.IO;

namespace OsuBackgroundReplacerMain.Models
{
    public class LogEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsSuccess { get; set; } = true;
        public string? ErrorMessage { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss");

        public static LogEntry CreateFromFilePath(string filePath, bool success = true, string? error = null)
        {
            string fileName = Path.GetFileName(filePath);
            string? folderName = Path.GetFileName(Path.GetDirectoryName(filePath));

            return new LogEntry
            {
                FilePath = filePath,
                FolderName = string.IsNullOrEmpty(folderName) ? "Unknown" : folderName,
                FileName = fileName,
                Timestamp = DateTime.Now,
                IsSuccess = success,
                ErrorMessage = error
            };
        }
    }
}

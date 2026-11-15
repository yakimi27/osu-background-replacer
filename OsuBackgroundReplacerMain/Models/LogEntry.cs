#if !TESTS
using Microsoft.UI.Xaml;
#endif
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
        public string StatusText => IsSuccess ? "Success" : "Failed";
        public string StatusGlyph => IsSuccess ? "\uE73E" : "\uEA39";
        public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

#if !TESTS
        public Visibility SuccessVisibility => IsSuccess ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FailureVisibility => IsSuccess ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ErrorMessageVisibility => HasErrorMessage ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SuccessMessageVisibility => (IsSuccess && HasErrorMessage) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FailureMessageVisibility => (!IsSuccess && HasErrorMessage) ? Visibility.Visible : Visibility.Collapsed;
#endif

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

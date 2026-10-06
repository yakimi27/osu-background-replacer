using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OsuBackgroundReplacerMain.Models;
using OsuBackgroundReplacerMain.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace OsuBackgroundReplacerMain.Views
{
    public sealed partial class ActivityLogPage : Page
    {
        public ActivityLogPage()
        {
            this.InitializeComponent();
            this.Loaded += ActivityLogPage_Loaded;
            LogService.EntriesChanged += RefreshList;
        }

        private void ActivityLogPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        private void RefreshList()
        {
            string filter = SearchBox.Text?.Trim() ?? string.Empty;
            IEnumerable<LogEntry> items = LogService.Entries;

            if (!string.IsNullOrEmpty(filter))
            {
                items = items.Where(i =>
                    i.FolderName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    i.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    i.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            var itemList = items.ToList();
            LogListView.ItemsSource = itemList;

            bool hasEntries = LogService.Entries.Count > 0;
            EmptyStateContainer.Visibility = hasEntries ? Visibility.Collapsed : Visibility.Visible;
            LogListView.Visibility = hasEntries ? Visibility.Visible : Visibility.Collapsed;

            TotalReplacedTextBlock.Text = $"{LogService.TotalReplacedCount:N0} backgrounds replaced";

            if (LogService.LastRunTime.HasValue)
            {
                LastRunTextBlock.Text = $"Last run: {LogService.LastRunTime.Value:HH:mm:ss}";
            }
            else
            {
                LastRunTextBlock.Text = "No operations yet";
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            RefreshList();
        }

        private void OpenItemFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string filePath)
            {
                string? folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = folder,
                        UseShellExecute = true
                    });
                }
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            LogService.Clear();
            RefreshList();
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (LogService.Entries.Count == 0)
            {
                LogNotificationInfoBar.Message = "Activity log is empty. Nothing to export.";
                LogNotificationInfoBar.Severity = InfoBarSeverity.Warning;
                LogNotificationInfoBar.IsOpen = true;
                return;
            }

            try
            {
                var savePicker = new FileSavePicker();
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Text Document", new List<string>() { ".txt" });
                savePicker.SuggestedFileName = $"osu-replaced-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

                StorageFile file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    await LogService.ExportToFileAsync(file.Path);
                    LogNotificationInfoBar.Message = $"Log exported successfully to: {file.Name}";
                    LogNotificationInfoBar.Severity = InfoBarSeverity.Success;
                    LogNotificationInfoBar.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                LogNotificationInfoBar.Message = $"Export failed: {ex.Message}";
                LogNotificationInfoBar.Severity = InfoBarSeverity.Error;
                LogNotificationInfoBar.IsOpen = true;
            }
        }
    }
}

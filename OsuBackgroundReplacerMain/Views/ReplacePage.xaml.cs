using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OsuBackgroundReplacerMain.Logic;
using OsuBackgroundReplacerMain.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace OsuBackgroundReplacerMain.Views
{
    public sealed partial class ReplacePage : Page
    {
        private bool _isLoaded;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _folderScanCts;
        private bool _isProcessing = false;

        public ReplacePage()
        {
            this.InitializeComponent();
            this.Loaded += ReplacePage_Loaded;
            this.Unloaded += ReplacePage_Unloaded;
        }

        private void ReplacePage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            FolderOperations.PathChanged -= UpdateFolderUI;
            FolderOperations.PathChanged += UpdateFolderUI;

            ImageOperations.PathChanged -= UpdateImageUI;
            ImageOperations.PathChanged += UpdateImageUI;

            SettingsService.SettingsChanged -= UpdateSettingsUI;
            SettingsService.SettingsChanged += UpdateSettingsUI;

            UpdateFolderUI();
            UpdateImageUI();
            UpdateSettingsUI();
        }

        private void ReplacePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            _folderScanCts?.Cancel();
            _folderScanCts?.Dispose();
            _folderScanCts = null;

            FolderOperations.PathChanged -= UpdateFolderUI;
            ImageOperations.PathChanged -= UpdateImageUI;
            SettingsService.SettingsChanged -= UpdateSettingsUI;
        }

        private void UpdateSettingsUI()
        {
            if (!_isLoaded) return;
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(UpdateSettingsUI);
                return;
            }
            if (!_isLoaded) return;

            BackupToggle.IsOn = SettingsService.Current.BackupOriginalBackgrounds;
        }

        private void BackupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (SettingsService.Current.BackupOriginalBackgrounds != BackupToggle.IsOn)
            {
                SettingsService.Current.BackupOriginalBackgrounds = BackupToggle.IsOn;
                SettingsService.Save();
            }
        }

        private void UpdateFolderUI()
        {
            if (!_isLoaded) return;
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(UpdateFolderUI);
                return;
            }
            if (!_isLoaded) return;

            _folderScanCts?.Cancel();
            _folderScanCts?.Dispose();
            _folderScanCts = new CancellationTokenSource();
            var scanToken = _folderScanCts.Token;

            string? folder = FolderOperations.getPath();
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                FolderPathTextBlock.Text = folder;
                OpenFolderButton.IsEnabled = true;

                FolderStatusGrid.Visibility = Visibility.Visible;
                FolderStatusTextBlock.Text = "Scanning beatmap folders...";

                bool isOsu = folder.Contains("osu!\\Songs", StringComparison.OrdinalIgnoreCase) ||
                             folder.Contains("osu!/Songs", StringComparison.OrdinalIgnoreCase);

                if (!isOsu)
                {
                    FolderStatusIcon.Glyph = "\uE7BA"; // Warning
                }
                else
                {
                    FolderStatusIcon.Glyph = "\uE73E"; // Checkmark
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        int count = await FolderOperations.GetBeatmapCountAsync(folder, scanToken);

                        if (scanToken.IsCancellationRequested) return;

                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            if (!_isLoaded || scanToken.IsCancellationRequested) return;
                            if (FolderOperations.getPath() != folder) return;

                            string note = !isOsu ? " (Note: Folder path doesn't contain 'osu!\\Songs')" : "";
                            FolderStatusTextBlock.Text = $"{count:N0} beatmap folders found{note}";
                        });
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            if (!_isLoaded || scanToken.IsCancellationRequested) return;
                            FolderStatusTextBlock.Text = "Unable to read beatmap count";
                        });
                    }
                }, scanToken);
            }
            else
            {
                FolderPathTextBlock.Text = "No folder selected. Auto-detect or browse for your osu!\\Songs folder.";
                OpenFolderButton.IsEnabled = false;
                FolderStatusGrid.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateImageUI()
        {
            if (!_isLoaded) return;
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(UpdateImageUI);
                return;
            }
            if (!_isLoaded) return;

            string? imagePath = ImageOperations.getPath();
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                ImagePathTextBlock.Text = imagePath;
                ImagePreviewContainer.Visibility = Visibility.Visible;
                ImageNameTextBlock.Text = Path.GetFileName(imagePath);
                ImageMetaTextBlock.Text = $"{ImageOperations.GetFormattedFileSize()} • {Path.GetExtension(imagePath).ToUpper()}";

                try
                {
                    var bitmap = new BitmapImage(new Uri(imagePath));
                    ImagePreviewThumbnail.Source = bitmap;
                }
                catch
                {
                    ImagePreviewThumbnail.Source = null;
                }
            }
            else
            {
                ImagePathTextBlock.Text = "No image selected. Choose a .jpg, .jpeg, or .png picture.";
                ImagePreviewContainer.Visibility = Visibility.Collapsed;
                ImagePreviewThumbnail.Source = null;
            }
        }

        private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            await FolderOperations.ChooseFolderManually(MainWindow.Current);
            UpdateFolderUI();
        }

        private async void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            await ImageOperations.ChooseImageManually(MainWindow.Current);
            UpdateImageUI();
        }

        private void AutoDetect_Click(object sender, RoutedEventArgs e)
        {
            string? detected = SettingsService.TryDetectOsuSongsPath();
            if (!string.IsNullOrEmpty(detected))
            {
                FolderOperations.setPath(detected);
                UpdateFolderUI();
                ShowInfo("osu! Songs directory detected successfully.", InfoBarSeverity.Success);
            }
            else
            {
                ShowInfo("Could not auto-detect osu! Songs directory. Please browse manually.", InfoBarSeverity.Warning);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string? path = FolderOperations.getPath();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }

        private void ClearImage_Click(object sender, RoutedEventArgs e)
        {
            ImageOperations.setPath(null);
            UpdateImageUI();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }

        private void FolderDropZone_DragLeave(object sender, DragEventArgs e)
        {
        }

        private void ImageDropZone_DragLeave(object sender, DragEventArgs e)
        {
        }

        private async void DropFolder(object sender, DragEventArgs e)
        {
            await FolderOperations.DragAndDropFolder(e);
            UpdateFolderUI();
        }

        private async void DropFile(object sender, DragEventArgs e)
        {
            await ImageOperations.DragAndDropImage(e);
            UpdateImageUI();
        }

        private async void Replace_Click(object sender, RoutedEventArgs e)
        {
            string? folder = FolderOperations.getPath();
            string? image = ImageOperations.getPath();

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                ShowInfo("Please select a valid osu! Songs folder first.", InfoBarSeverity.Error);
                return;
            }

            if (string.IsNullOrEmpty(image) || !File.Exists(image))
            {
                ShowInfo("Please select a valid replacement image file first.", InfoBarSeverity.Error);
                return;
            }

            if (SettingsService.Current.ConfirmBeforeReplace)
            {
                string confirmMsg = $"You are about to replace all background images in:\n'{folder}'\n\nwith:\n'{Path.GetFileName(image)}'.\n\nAre you sure you want to proceed?";
                var confirm = await MainWindow.ShowDialogAsync(confirmMsg, "Confirm Replacement", "Yes, Replace", "Cancel");
                if (confirm != ContentDialogResult.Primary) return;
            }

            if (!folder.Contains("osu!\\Songs", StringComparison.OrdinalIgnoreCase) &&
                !folder.Contains("osu!/Songs", StringComparison.OrdinalIgnoreCase))
            {
                var warnResult = await MainWindow.ShowDialogAsync(
                    "The selected folder does not appear to be an osu!\\Songs folder. Are you sure you want to continue?",
                    "Warning", "Continue Anyway", "Cancel");
                if (warnResult != ContentDialogResult.Primary) return;
            }

            SetProcessing(true);
            _cts = new CancellationTokenSource();

            var progress = new Progress<ReplacementProgress>(p =>
            {
                ReplacingProgressBar.Value = p.Percentage;
                ProgressPercentTextBlock.Text = $"{p.Percentage}%";
                ProgressDetailTextBlock.Text = $"Processing {p.Current:N0} of {p.Total:N0}: {p.CurrentFile}";
            });

            try
            {
                List<string> replaced = await Operations.Replacement(progress, null, _cts.Token);
                SetProcessing(false);

                if (_cts.IsCancellationRequested)
                {
                    ShowInfo($"Operation cancelled. {replaced.Count:N0} backgrounds replaced before stopping.", InfoBarSeverity.Warning);
                }
                else
                {
                    var backupResult = Operations.LastBackupResult;
                    var repErrors = Operations.LastReplacementErrors;
                    int totalErrors = (backupResult?.FailedCount ?? 0) + repErrors.Count;

                    if (totalErrors > 0)
                    {
                        var errorLines = new List<string>();
                        if (backupResult != null && backupResult.FailedCount > 0)
                        {
                            errorLines.Add($"Backup errors ({backupResult.FailedCount}):");
                            errorLines.AddRange(backupResult.Errors.Take(10).Select(e => $"• {Path.GetFileName(e.FilePath)}: {e.ErrorMessage}"));
                        }
                        if (repErrors.Count > 0)
                        {
                            errorLines.Add($"Replacement errors ({repErrors.Count}):");
                            errorLines.AddRange(repErrors.Take(10).Select(e => $"• {Path.GetFileName(e.FilePath)}: {e.ErrorMessage}"));
                        }
                        if (totalErrors > 20)
                        {
                            errorLines.Add($"... and {totalErrors - 20} more error(s).");
                        }

                        string dialogContent = $"Replacement finished with {totalErrors} issue(s).\nSuccessfully replaced: {replaced.Count:N0}\n\n" + string.Join("\n", errorLines);
                        await MainWindow.ShowDialogAsync(dialogContent, "Replacement Warnings");
                        ShowInfo($"Replaced {replaced.Count:N0} backgrounds with {totalErrors} issue(s). Check Activity Log for details.", InfoBarSeverity.Warning);
                    }
                    else
                    {
                        string backupMsg = (backupResult != null && backupResult.BackedUpCount > 0)
                            ? $" ({backupResult.BackedUpCount:N0} backed up)"
                            : "";
                        ShowInfo($"Successfully replaced {replaced.Count:N0} backgrounds{backupMsg}! Check Activity Log for full details.", InfoBarSeverity.Success);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                SetProcessing(false);
                ShowInfo("Operation cancelled.", InfoBarSeverity.Warning);
            }
            catch (Exception ex)
            {
                SetProcessing(false);
                ShowInfo($"Error during replacement: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            var backupService = new BackupService();
            var latestSession = backupService.GetLatestSessionInfo();

            if (latestSession == null)
            {
                await MainWindow.ShowDialogAsync("No backup sessions were found to restore.", "Undo Last Replacement");
                ShowInfo("No backup sessions available.", InfoBarSeverity.Informational);
                return;
            }

            var confirm = await MainWindow.ShowDialogAsync(
                $"Restore original backgrounds from the backup session created {latestSession.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({latestSession.FileCount:N0} file(s))?\n\nThis may not be your most recent replacement: runs with backup disabled do not create a session.\n\nThis will verify file hashes, skip missing files gracefully, and restore the original images.",
                "Confirm Undo", "Yes, Restore", "Cancel");

            if (confirm != ContentDialogResult.Primary)
            {
                return;
            }

            SetProcessing(true);
            _cts = new CancellationTokenSource();
            StatusInfoBar.IsOpen = false;
            ProgressSection.Visibility = Visibility.Visible;
            ReplacingProgressBar.Value = 0;
            ProgressPercentTextBlock.Text = "0%";
            ProgressDetailTextBlock.Text = "Restoring backgrounds from backup...";

            var progress = new Progress<RestoreProgress>(p =>
            {
                ReplacingProgressBar.Value = p.Percentage;
                ProgressPercentTextBlock.Text = $"{p.Percentage}%";
                ProgressDetailTextBlock.Text = $"Restoring {p.Current:N0} of {p.Total:N0}: {p.CurrentFile}";
            });

            try
            {
                var result = await backupService.RestoreLastBackupAsync(progress, _cts.Token, FolderOperations.getPath());
                SetProcessing(false);

                foreach (var item in result.Items)
                {
                    if (item.Status == RestoreStatus.Restored)
                    {
                        LogService.AddEntry(item.OriginalPath, true, "Restored from backup");
                    }
                    else if (item.Status == RestoreStatus.Failed)
                    {
                        LogService.AddEntry(item.OriginalPath, false, $"Restore failed: {item.ErrorMessage}");
                    }
                    else if (item.Status == RestoreStatus.Skipped)
                    {
                        LogService.AddEntry(string.IsNullOrWhiteSpace(item.OriginalPath) ? "Unknown" : item.OriginalPath, false, $"Restore skipped: {item.ErrorMessage}");
                    }
                }

                string summary = $"Undo Operation Summary:\n\n• Restored: {result.RestoredCount:N0}\n• Skipped: {result.SkippedCount:N0}\n• Failed: {result.FailedCount:N0}";

                if (result.FailedCount > 0)
                {
                    var errorLines = result.Errors.Take(10).Select(err => $"• {Path.GetFileName(err.OriginalPath)}: {err.ErrorMessage}");
                    string errorDetails = string.Join("\n", errorLines);
                    if (result.FailedCount > 10)
                    {
                        errorDetails += $"\n... and {result.FailedCount - 10} more error(s).";
                    }

                    string fullContent = $"{summary}\n\nErrors encountered:\n{errorDetails}";
                    await MainWindow.ShowDialogAsync(fullContent, "Undo Summary - Issues Encountered");
                    ShowInfo($"Undo completed with {result.FailedCount:N0} failed, {result.RestoredCount:N0} restored, {result.SkippedCount:N0} skipped.", InfoBarSeverity.Warning);
                }
                else
                {
                    await MainWindow.ShowDialogAsync(summary, "Undo Completed Successfully");
                    ShowInfo($"Successfully restored {result.RestoredCount:N0} backgrounds from backup ({result.SkippedCount:N0} skipped).", InfoBarSeverity.Success);
                }
            }
            catch (OperationCanceledException)
            {
                SetProcessing(false);
                ShowInfo("Undo operation cancelled.", InfoBarSeverity.Warning);
            }
            catch (Exception ex)
            {
                SetProcessing(false);
                await MainWindow.ShowDialogAsync($"Error during undo: {ex.Message}", "Undo Error");
                ShowInfo($"Error during undo: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            CancelButton.IsEnabled = false;
        }

        private void SetProcessing(bool processing)
        {
            _isProcessing = processing;
            if (!_isLoaded) return;

            ReplaceButton.IsEnabled = !processing;
            UndoButton.IsEnabled = !processing;
            BackupToggle.IsEnabled = !processing;
            BrowseFolderButton.IsEnabled = !processing;
            BrowseImageButton.IsEnabled = !processing;
            AutoDetectButton.IsEnabled = !processing;
            FolderDropZone.IsHitTestVisible = !processing;
            ImageDropZone.IsHitTestVisible = !processing;

            ProgressSection.Visibility = processing ? Visibility.Visible : Visibility.Collapsed;
            CancelButton.Visibility = processing ? Visibility.Visible : Visibility.Collapsed;
            CancelButton.IsEnabled = true;

            if (processing)
            {
                StatusInfoBar.IsOpen = false;
                ReplacingProgressBar.Value = 0;
                ProgressPercentTextBlock.Text = "0%";
                ProgressDetailTextBlock.Text = "Starting operation...";
            }
        }

        private void ShowInfo(string message, InfoBarSeverity severity)
        {
            if (!_isLoaded) return;

            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }
    }
}

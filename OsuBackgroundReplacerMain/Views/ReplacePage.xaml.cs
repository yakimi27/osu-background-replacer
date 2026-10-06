using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OsuBackgroundReplacerMain.Logic;
using OsuBackgroundReplacerMain.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace OsuBackgroundReplacerMain.Views
{
    public sealed partial class ReplacePage : Page
    {
        private CancellationTokenSource? _cts;
        private bool _isProcessing = false;

        public ReplacePage()
        {
            this.InitializeComponent();
            this.Loaded += ReplacePage_Loaded;
            FolderOperations.PathChanged += UpdateFolderUI;
            ImageOperations.PathChanged += UpdateImageUI;
        }

        private void ReplacePage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateFolderUI();
            UpdateImageUI();
        }

        private void UpdateFolderUI()
        {
            string? folder = FolderOperations.getPath();
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                FolderPathTextBlock.Text = folder;
                OpenFolderButton.IsEnabled = true;

                int count = FolderOperations.GetBeatmapCount();
                FolderStatusGrid.Visibility = Visibility.Visible;
                FolderStatusTextBlock.Text = $"{count:N0} beatmap folders found";

                bool isOsu = folder.Contains("osu!\\Songs", StringComparison.OrdinalIgnoreCase) ||
                             folder.Contains("osu!/Songs", StringComparison.OrdinalIgnoreCase);

                if (!isOsu)
                {
                    FolderStatusIcon.Glyph = "\uE7BA"; // Warning
                    FolderStatusTextBlock.Text += " (Note: Folder path doesn't contain 'osu!\\Songs')";
                }
                else
                {
                    FolderStatusIcon.Glyph = "\uE73E"; // Checkmark
                }
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
                    ShowInfo($"Successfully replaced {replaced.Count:N0} backgrounds! Check Activity Log for full details.", InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                SetProcessing(false);
                ShowInfo($"Error during replacement: {ex.Message}", InfoBarSeverity.Error);
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
            ReplaceButton.IsEnabled = !processing;
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
                ProgressDetailTextBlock.Text = "Scanning beatmap directories...";
            }
        }

        private void ShowInfo(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }
    }
}

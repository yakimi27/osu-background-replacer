using Microsoft.UI.Xaml;
using OsuBackgroundReplacerMain.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace OsuBackgroundReplacerMain.Logic
{
    public static class ImageOperations
    {
        private static string? _selectedImagePath;

        public static event Action? PathChanged;

        public static string? getPath()
        {
            if (string.IsNullOrEmpty(_selectedImagePath) && SettingsService.Current.SaveLastImagePath)
            {
                if (File.Exists(SettingsService.Current.LastImagePath))
                {
                    _selectedImagePath = SettingsService.Current.LastImagePath;
                }
            }
            return _selectedImagePath;
        }

        public static void setPath(string? path)
        {
            _selectedImagePath = path;
            if (SettingsService.Current.SaveLastImagePath && !string.IsNullOrEmpty(path))
            {
                SettingsService.Current.LastImagePath = path;
                SettingsService.Save();
            }
            PathChanged?.Invoke();
        }

        public static string GetFormattedFileSize()
        {
            try
            {
                string? path = getPath();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    long bytes = fileInfo.Length;
                    if (bytes >= 1024 * 1024)
                    {
                        return $"{bytes / (1024.0 * 1024.0):F2} MB";
                    }
                    if (bytes >= 1024)
                    {
                        return $"{bytes / 1024.0:F1} KB";
                    }
                    return $"{bytes} B";
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        public static async Task ChooseImageManually(Window window)
        {
            try
            {
                var openPicker = new FileOpenPicker();

                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

                openPicker.ViewMode = PickerViewMode.Thumbnail;
                openPicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                openPicker.FileTypeFilter.Add(".jpg");
                openPicker.FileTypeFilter.Add(".jpeg");
                openPicker.FileTypeFilter.Add(".png");

                StorageFile file = await openPicker.PickSingleFileAsync();
                if (file != null)
                {
                    setPath(file.Path);
                }
            }
            catch (Exception exception)
            {
                await MainWindow.ShowDialogAsync(exception.Message, "Image Error");
            }
        }

        public static async Task DragAndDropImage(DragEventArgs e)
        {
            try
            {
                if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    if (items.Count > 0)
                    {
                        var file = items[0] as StorageFile;
                        if (file != null)
                        {
                            string type = file.FileType.ToLower();
                            if (Constants.SupportedImageExtensions.Contains(type))
                            {
                                setPath(file.Path);
                            }
                            else
                            {
                                await MainWindow.ShowDialogAsync("The dropped item is not a valid image file (.jpg, .jpeg, .png).", "Error");
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                await MainWindow.ShowDialogAsync(exception.Message, "Error selecting image");
            }
        }
    }
}
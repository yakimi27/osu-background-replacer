using Microsoft.UI.Xaml;
using OsuBackgroundReplacerMain.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace OsuBackgroundReplacerMain.Logic
{
    public static class FolderOperations
    {
        private static string? _selectedPath;

        public static event Action? PathChanged;

        public static string? getPath()
        {
            if (string.IsNullOrEmpty(_selectedPath) && SettingsService.Current.SaveLastFolderPath)
            {
                _selectedPath = SettingsService.Current.LastFolderPath;
            }
            return _selectedPath;
        }

        public static void setPath(string? path)
        {
            _selectedPath = path;
            if (SettingsService.Current.SaveLastFolderPath && !string.IsNullOrEmpty(path))
            {
                SettingsService.Current.LastFolderPath = path;
                SettingsService.Save();
            }
            PathChanged?.Invoke();
        }

        public static int GetBeatmapCount()
        {
            try
            {
                string? path = getPath();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    return Directory.GetDirectories(path).Length;
                }
            }
            catch
            {
            }
            return 0;
        }

        public static async Task ChooseFolderManually(Window window)
        {
            try
            {
                var folderPicker = new FolderPicker();

                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hWnd);

                folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                folderPicker.FileTypeFilter.Add("*");

                StorageFolder folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    setPath(folder.Path);
                }
            }
            catch (Exception exception)
            {
                await MainWindow.ShowDialogAsync(exception.Message, "Folder Error");
            }
        }

        public static async Task DragAndDropFolder(DragEventArgs e)
        {
            try
            {
                if (e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    if (items.Count > 0)
                    {
                        var folder = items[0] as StorageFolder;
                        if (folder != null)
                        {
                            setPath(folder.Path);
                        }
                        else
                        {
                            await MainWindow.ShowDialogAsync("The dropped item is not a folder.", "Error");
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                await MainWindow.ShowDialogAsync(exception.Message, "Error selecting folder");
            }
        }
    }
}
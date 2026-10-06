using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Services;

namespace OsuBackgroundReplacerMain.Logic
{
    public class ReplacementProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public int Percentage => Total > 0 ? (int)((double)Current / Total * 100) : 0;
        public string CurrentFile { get; set; } = string.Empty;
    }

    internal class Operations
    {
        public static async Task<List<string>> Replacement(
            IProgress<ReplacementProgress>? detailedProgress = null,
            IProgress<int>? simpleProgress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                string? imagePath = ImageOperations.getPath();
                string? folderPath = FolderOperations.getPath();

                if (string.IsNullOrEmpty(imagePath) ||
                    string.IsNullOrEmpty(folderPath) ||
                    !File.Exists(imagePath) ||
                    !Directory.Exists(folderPath))
                {
                    throw new InvalidOperationException("File or folder does not exist.");
                }

                List<string> replacedFiles = new List<string>();

                var allImageFiles = await Task.Run(() =>
                        Directory.GetDirectories(folderPath)
                        .SelectMany(folder => Directory.GetFiles(folder, "*.*"))
                        .Where(f => Constants.IsSupportedImage(f))
                        .ToList(),
                        cancellationToken
                    );

                int total = allImageFiles.Count;
                int current = 0;

                foreach (var imageFile in allImageFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Run(() => File.Copy(imagePath, imageFile, true), cancellationToken);
                        replacedFiles.Add(imageFile);
                        LogService.AddEntry(imageFile, true);
                    }
                    catch (Exception exception)
                    {
                        LogService.AddEntry(imageFile, false, exception.Message);
                        throw new Exception($"Issue copying to {imageFile}: {exception.Message}", exception);
                    }

                    current++;
                    simpleProgress?.Report((int)((double)current / total * 100));
                    detailedProgress?.Report(new ReplacementProgress
                    {
                        Current = current,
                        Total = total,
                        CurrentFile = Path.GetFileName(Path.GetDirectoryName(imageFile)) ?? Path.GetFileName(imageFile)
                    });
                }

                return replacedFiles;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(exception.Message);
            }
        }
    }
}
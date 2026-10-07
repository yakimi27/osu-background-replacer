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
        public static BackupSessionResult? LastBackupResult { get; private set; }
        public static List<FileErrorInfo> LastReplacementErrors { get; } = new();

        public static async Task<List<string>> Replacement(
            IProgress<ReplacementProgress>? detailedProgress = null,
            IProgress<int>? simpleProgress = null,
            CancellationToken cancellationToken = default,
            bool? enableBackup = null,
            IBackupService? backupService = null)
        {
            try
            {
                LastBackupResult = null;
                LastReplacementErrors.Clear();

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
                    {
                        var targets = new List<string>();
                        string fullFolderPath = Path.GetFullPath(folderPath);

                        foreach (var beatmapFolder in Directory.GetDirectories(folderPath))
                        {
                            var backgroundFilenames = OsuBeatmapParser.GetBackgroundFiles(beatmapFolder);
                            if (backgroundFilenames.Count == 0)
                            {
                                continue;
                            }

                            string fullBeatmapFolder = Path.GetFullPath(beatmapFolder);

                            foreach (var bgFileName in backgroundFilenames)
                            {
                                try
                                {
                                    string resolvedPath = Path.GetFullPath(Path.Combine(beatmapFolder, bgFileName));

                                    // Guard against path traversal like ..\ to ensure the resolved file is inside the beatmap folder
                                    if (resolvedPath.StartsWith(fullBeatmapFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                        && File.Exists(resolvedPath))
                                    {
                                        targets.Add(resolvedPath);
                                    }
                                }
                                catch
                                {
                                    // Skip invalid path formats
                                }
                            }
                        }

                        return targets;
                    },
                    cancellationToken
                );

                if (cancellationToken.IsCancellationRequested)
                {
                    return replacedFiles;
                }

                // Execute backup if enabled
                bool shouldBackup = enableBackup ?? SettingsService.Current.BackupOriginalBackgrounds;
                if (shouldBackup && allImageFiles.Count > 0)
                {
                    backupService ??= new BackupService();

                    detailedProgress?.Report(new ReplacementProgress
                    {
                        Current = 0,
                        Total = allImageFiles.Count,
                        CurrentFile = "Backing up original backgrounds..."
                    });

                    var backupProgress = new Progress<BackupProgress>(bp =>
                    {
                        detailedProgress?.Report(new ReplacementProgress
                        {
                            Current = bp.Current,
                            Total = bp.Total,
                            CurrentFile = $"Backing up: {bp.CurrentFile}"
                        });
                        simpleProgress?.Report(bp.Percentage);
                    });

                    LastBackupResult = await backupService.CreateBackupSessionAsync(
                        allImageFiles,
                        folderPath,
                        backupProgress,
                        cancellationToken);
                }

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
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        LogService.AddEntry(imageFile, false, exception.Message);
                        LastReplacementErrors.Add(new FileErrorInfo(imageFile, exception.Message));
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(exception.Message, exception);
            }
        }
    }
}
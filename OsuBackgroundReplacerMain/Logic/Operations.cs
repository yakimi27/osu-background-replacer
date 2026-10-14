using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OsuBackgroundReplacerMain.Services;

namespace OsuBackgroundReplacerMain.Logic
{
    public class ReplacementProgress
    {
        private int? _percentage;
        public int Current { get; set; }
        public int Total { get; set; }
        public int Percentage
        {
            get => _percentage ?? (Total > 0 ? (int)((double)Current / Total * 100) : 0);
            set => _percentage = value;
        }
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

                                    if (resolvedPath.StartsWith(fullBeatmapFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                        && File.Exists(resolvedPath))
                                    {
                                        targets.Add(resolvedPath);
                                    }
                                }
                                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Failed to resolve background path '{bgFileName}' in '{beatmapFolder}': {ex.Message}");
                                    LogService.AddEntry(Path.Combine(beatmapFolder, bgFileName), false, $"Invalid background path: {ex.Message}");
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

                bool shouldBackup = enableBackup ?? SettingsService.Current.BackupOriginalBackgrounds;
                int totalSteps = shouldBackup && allImageFiles.Count > 0 ? allImageFiles.Count * 2 : allImageFiles.Count;

                if (shouldBackup && allImageFiles.Count > 0)
                {
                    backupService ??= new BackupService();

                    detailedProgress?.Report(new ReplacementProgress
                    {
                        Current = 0,
                        Total = totalSteps,
                        Percentage = 0,
                        CurrentFile = "Backing up original backgrounds..."
                    });
                    simpleProgress?.Report(0);

                    var backupProgress = new Progress<BackupProgress>(bp =>
                    {
                        int pct = bp.Total > 0 ? (int)((double)bp.Current / bp.Total * 50) : 0;
                        detailedProgress?.Report(new ReplacementProgress
                        {
                            Current = bp.Current,
                            Total = totalSteps,
                            Percentage = pct,
                            CurrentFile = $"Backing up: {bp.CurrentFile}"
                        });
                        simpleProgress?.Report(pct);
                    });

                    LastBackupResult = await backupService.CreateBackupSessionAsync(
                        allImageFiles,
                        folderPath,
                        backupProgress,
                        cancellationToken);
                }

                var filesToReplace = allImageFiles;
                if (LastBackupResult != null)
                {
                    var backupResult = LastBackupResult;
                    filesToReplace = allImageFiles.Where(backupResult.IsBackedUp).ToList();
                    foreach (var skipped in allImageFiles.Except(filesToReplace))
                    {
                        string reason = backupResult.Errors.FirstOrDefault(e =>
                            string.Equals(Path.GetFullPath(e.FilePath), skipped, StringComparison.OrdinalIgnoreCase))?.ErrorMessage
                            ?? "Backup failed.";
                        string message = $"Skipped, not replaced because backup failed: {reason}";
                        LogService.AddEntry(skipped, false, message);
                        LastReplacementErrors.Add(new FileErrorInfo(skipped, message));
                    }
                }

                int totalToReplace = filesToReplace.Count;
                int completedCount = 0;

                await Task.Run(async () =>
                {
                    long lastReportTicks = Stopwatch.GetTimestamp();
                    long throttleIntervalTicks = (long)(Stopwatch.Frequency * 0.1); // ~100ms
                    object sync = new();

                    int startPct = shouldBackup ? 50 : 0;
                    simpleProgress?.Report(startPct);
                    detailedProgress?.Report(new ReplacementProgress
                    {
                        Current = shouldBackup ? allImageFiles.Count : 0,
                        Total = totalSteps,
                        Percentage = startPct,
                        CurrentFile = totalToReplace > 0 ? "Replacing backgrounds..." : "Done"
                    });

                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 4,
                        CancellationToken = cancellationToken
                    };

                    await Parallel.ForEachAsync(filesToReplace, parallelOptions, async (imageFile, ct) =>
                    {
                        try
                        {
                            File.Copy(imagePath, imageFile, true);
                            lock (sync)
                            {
                                replacedFiles.Add(imageFile);
                            }
                            LogService.AddEntry(imageFile, true);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            LogService.AddEntry(imageFile, false, exception.Message);
                            lock (sync)
                            {
                                LastReplacementErrors.Add(new FileErrorInfo(imageFile, exception.Message));
                            }
                        }

                        int currentDone = Interlocked.Increment(ref completedCount);
                        long now = Stopwatch.GetTimestamp();
                        bool isLast = (currentDone == totalToReplace);

                        if (isLast || now - Volatile.Read(ref lastReportTicks) >= throttleIntervalTicks)
                        {
                            Volatile.Write(ref lastReportTicks, now);
                            int pct = shouldBackup
                                ? (totalToReplace > 0 ? 50 + (int)((double)currentDone / totalToReplace * 50) : 100)
                                : (totalToReplace > 0 ? (int)((double)currentDone / totalToReplace * 100) : 100);

                            int currentOverall = shouldBackup ? allImageFiles.Count + currentDone : currentDone;

                            simpleProgress?.Report(pct);
                            detailedProgress?.Report(new ReplacementProgress
                            {
                                Current = currentOverall,
                                Total = totalSteps,
                                Percentage = pct,
                                CurrentFile = Path.GetFileName(Path.GetDirectoryName(imageFile)) ?? Path.GetFileName(imageFile)
                            });
                        }
                    });

                    LogService.Flush();
                }, cancellationToken);

                int keepN = SettingsService.Current.KeepLastNBackupSessions;
                if (keepN > 0 && LastBackupResult != null)
                {
                    backupService ??= new BackupService();
                    backupService.CleanupOldSessions(keepN);
                }

                return replacedFiles;
            }
            catch (OperationCanceledException)
            {
                LogService.Flush();
                throw;
            }
            catch (Exception exception)
            {
                LogService.Flush();
                throw new InvalidOperationException(exception.Message, exception);
            }
        }
    }
}
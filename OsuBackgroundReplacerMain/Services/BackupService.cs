using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace OsuBackgroundReplacerMain.Services
{
    public class BackupManifestEntry
    {
        [JsonPropertyName("originalPath")]
        public string OriginalPath { get; set; } = string.Empty;

        [JsonPropertyName("backupPath")]
        public string BackupPath { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }

    public class BackupManifest
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonPropertyName("entries")]
        public List<BackupManifestEntry> Entries { get; set; } = new();
    }

    public class FileErrorInfo
    {
        public string FilePath { get; set; }
        public string ErrorMessage { get; set; }

        public FileErrorInfo(string filePath, string errorMessage)
        {
            FilePath = filePath;
            ErrorMessage = errorMessage;
        }
    }

    public class BackupProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public int Percentage => Total > 0 ? (int)((double)Current / Total * 100) : 0;
        public string CurrentFile { get; set; } = string.Empty;
    }

    public class RestoreProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public int Percentage => Total > 0 ? (int)((double)Current / Total * 100) : 0;
        public string CurrentFile { get; set; } = string.Empty;
    }

    public enum RestoreStatus
    {
        Restored,
        Skipped,
        Failed
    }

    public class RestoreItemResult
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        public RestoreStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class BackupSessionResult
    {
        public string SessionDirectory { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public int TotalFiles { get; set; }
        public int BackedUpCount => Entries.Count;
        public int FailedCount => Errors.Count;
        public List<BackupManifestEntry> Entries { get; set; } = new();
        public List<FileErrorInfo> Errors { get; set; } = new();
        public bool Success => FailedCount == 0;
    }

    public class RestoreSessionResult
    {
        public string? SessionDirectory { get; set; }
        public DateTimeOffset? SessionTimestamp { get; set; }
        public int TotalEntries { get; set; }
        public int RestoredCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public List<RestoreItemResult> Items { get; set; } = new();
        public List<RestoreItemResult> Errors => Items.Where(i => i.Status == RestoreStatus.Failed).ToList();
        public string SummaryMessage => $"Restored: {RestoredCount}, Skipped: {SkippedCount}, Failed: {FailedCount}";
        public bool HasBackupFound => !string.IsNullOrEmpty(SessionDirectory);
    }

    public interface IBackupService
    {
        string BaseBackupDirectory { get; }
        Task<BackupSessionResult> CreateBackupSessionAsync(
            IEnumerable<string> files,
            string? rootFolderPath = null,
            IProgress<BackupProgress>? progress = null,
            CancellationToken cancellationToken = default);
        Task<RestoreSessionResult> RestoreLastBackupAsync(
            IProgress<RestoreProgress>? progress = null,
            CancellationToken cancellationToken = default);
        Task<RestoreSessionResult> RestoreBackupSessionAsync(
            string sessionDirectory,
            IProgress<RestoreProgress>? progress = null,
            CancellationToken cancellationToken = default);
        string? GetLatestSessionDirectory();
        List<string> GetBackupSessions();
    }

    public class BackupService : IBackupService
    {
        public const string DefaultAppName = "OsuBackgroundReplacer";
        private readonly string _baseBackupDirectory;

        public string BaseBackupDirectory => _baseBackupDirectory;

        public BackupService(string? baseBackupDirectory = null)
        {
            _baseBackupDirectory = baseBackupDirectory ?? GetDefaultBackupDirectory();
        }

        public static string GetDefaultBackupDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "yakimi27",
                DefaultAppName,
                "Backups");
        }

        public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static string ComputeSha256(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public async Task<BackupSessionResult> CreateBackupSessionAsync(
            IEnumerable<string> files,
            string? rootFolderPath = null,
            IProgress<BackupProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var fileList = files.ToList();
            var result = new BackupSessionResult
            {
                TotalFiles = fileList.Count,
                Timestamp = DateTimeOffset.Now
            };

            if (fileList.Count == 0)
            {
                return result;
            }

            Directory.CreateDirectory(_baseBackupDirectory);

            string sessionName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string sessionDir = Path.Combine(_baseBackupDirectory, sessionName);
            int counter = 1;
            while (Directory.Exists(sessionDir))
            {
                sessionDir = Path.Combine(_baseBackupDirectory, $"{sessionName}_{counter++}");
            }

            Directory.CreateDirectory(sessionDir);
            result.SessionDirectory = sessionDir;

            int current = 0;
            foreach (var file in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current++;

                try
                {
                    if (!File.Exists(file))
                    {
                        result.Errors.Add(new FileErrorInfo(file, "File does not exist."));
                        continue;
                    }

                    string relativePath = GetRelativePath(file, rootFolderPath);
                    string backupPath = Path.Combine(sessionDir, relativePath);

                    string? parentDir = Path.GetDirectoryName(backupPath);
                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    string sha256 = await ComputeSha256Async(file, cancellationToken);

                    await Task.Run(() => File.Copy(file, backupPath, true), cancellationToken);

                    result.Entries.Add(new BackupManifestEntry
                    {
                        OriginalPath = Path.GetFullPath(file),
                        BackupPath = Path.GetFullPath(backupPath),
                        Timestamp = DateTimeOffset.Now,
                        Sha256 = sha256
                    });
                }
                catch (OperationCanceledException)
                {
                    await TryWriteManifestAsync(sessionDir, result.Timestamp, result.Entries);
                    throw;
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new FileErrorInfo(file, ex.Message));
                }
                finally
                {
                    progress?.Report(new BackupProgress
                    {
                        Current = current,
                        Total = fileList.Count,
                        CurrentFile = Path.GetFileName(file)
                    });
                }
            }

            await TryWriteManifestAsync(sessionDir, result.Timestamp, result.Entries);

            return result;
        }

        private static async Task TryWriteManifestAsync(
            string sessionDir,
            DateTimeOffset timestamp,
            List<BackupManifestEntry> entries)
        {
            try
            {
                var manifest = new BackupManifest
                {
                    Timestamp = timestamp,
                    Entries = entries
                };

                string manifestPath = Path.Combine(sessionDir, "manifest.json");
                string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(manifestPath, json);
            }
            catch
            {
                // Silently ignore or suppress manifest write issues during emergency cleanup
            }
        }

        public async Task<RestoreSessionResult> RestoreLastBackupAsync(
            IProgress<RestoreProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string? latestSession = GetLatestSessionDirectory();
            if (string.IsNullOrEmpty(latestSession))
            {
                return new RestoreSessionResult
                {
                    SessionDirectory = null,
                    SessionTimestamp = null
                };
            }

            return await RestoreBackupSessionAsync(latestSession, progress, cancellationToken);
        }

        public async Task<RestoreSessionResult> RestoreBackupSessionAsync(
            string sessionDirectory,
            IProgress<RestoreProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new RestoreSessionResult
            {
                SessionDirectory = sessionDirectory
            };

            if (!Directory.Exists(sessionDirectory))
            {
                result.FailedCount = 1;
                result.Items.Add(new RestoreItemResult
                {
                    Status = RestoreStatus.Failed,
                    ErrorMessage = $"Backup session folder '{sessionDirectory}' does not exist."
                });
                return result;
            }

            string manifestPath = Path.Combine(sessionDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                result.FailedCount = 1;
                result.Items.Add(new RestoreItemResult
                {
                    Status = RestoreStatus.Failed,
                    ErrorMessage = $"manifest.json not found in '{sessionDirectory}'."
                });
                return result;
            }

            BackupManifest? manifest = null;
            try
            {
                string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                try
                {
                    manifest = JsonSerializer.Deserialize<BackupManifest>(json, options);
                }
                catch
                {
                    var list = JsonSerializer.Deserialize<List<BackupManifestEntry>>(json, options);
                    if (list != null)
                    {
                        manifest = new BackupManifest
                        {
                            Timestamp = Directory.GetCreationTimeUtc(sessionDirectory),
                            Entries = list
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                result.FailedCount = 1;
                result.Items.Add(new RestoreItemResult
                {
                    Status = RestoreStatus.Failed,
                    ErrorMessage = $"Failed to read manifest.json: {ex.Message}"
                });
                return result;
            }

            if (manifest == null || manifest.Entries == null)
            {
                result.FailedCount = 1;
                result.Items.Add(new RestoreItemResult
                {
                    Status = RestoreStatus.Failed,
                    ErrorMessage = "manifest.json is empty or invalid."
                });
                return result;
            }

            result.SessionTimestamp = manifest.Timestamp;
            result.TotalEntries = manifest.Entries.Count;

            int current = 0;
            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current++;

                try
                {
                    if (string.IsNullOrWhiteSpace(entry.OriginalPath))
                    {
                        result.SkippedCount++;
                        result.Items.Add(new RestoreItemResult
                        {
                            Status = RestoreStatus.Skipped,
                            ErrorMessage = "Manifest entry has empty original path."
                        });
                        continue;
                    }

                    string backupFile = entry.BackupPath;
                    if (!File.Exists(backupFile))
                    {
                        // Check relative path inside session directory if moved
                        string rel = GetRelativePath(entry.OriginalPath, null);
                        string fallback = Path.Combine(sessionDirectory, rel);
                        if (File.Exists(fallback))
                        {
                            backupFile = fallback;
                        }
                    }

                    // 1. Skip missing backup files gracefully
                    if (!File.Exists(backupFile))
                    {
                        result.SkippedCount++;
                        result.Items.Add(new RestoreItemResult
                        {
                            OriginalPath = entry.OriginalPath,
                            BackupPath = entry.BackupPath,
                            Status = RestoreStatus.Skipped,
                            ErrorMessage = "Backup file not found on disk."
                        });
                        continue;
                    }

                    // 2. Verify hash
                    string actualHash;
                    try
                    {
                        actualHash = await ComputeSha256Async(backupFile, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        result.Items.Add(new RestoreItemResult
                        {
                            OriginalPath = entry.OriginalPath,
                            BackupPath = backupFile,
                            Status = RestoreStatus.Failed,
                            ErrorMessage = $"Failed to compute hash for backup file: {ex.Message}"
                        });
                        continue;
                    }

                    if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        result.FailedCount++;
                        result.Items.Add(new RestoreItemResult
                        {
                            OriginalPath = entry.OriginalPath,
                            BackupPath = backupFile,
                            Status = RestoreStatus.Failed,
                            ErrorMessage = $"Hash mismatch for '{Path.GetFileName(backupFile)}': expected {entry.Sha256}, got {actualHash}."
                        });
                        continue;
                    }

                    // 3. Restore file
                    string? targetDir = Path.GetDirectoryName(entry.OriginalPath);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    await Task.Run(() => File.Copy(backupFile, entry.OriginalPath, true), cancellationToken);

                    result.RestoredCount++;
                    result.Items.Add(new RestoreItemResult
                    {
                        OriginalPath = entry.OriginalPath,
                        BackupPath = backupFile,
                        Status = RestoreStatus.Restored
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Items.Add(new RestoreItemResult
                    {
                        OriginalPath = entry.OriginalPath,
                        BackupPath = entry.BackupPath,
                        Status = RestoreStatus.Failed,
                        ErrorMessage = $"IO error restoring '{entry.OriginalPath}': {ex.Message}"
                    });
                }
                finally
                {
                    progress?.Report(new RestoreProgress
                    {
                        Current = current,
                        Total = result.TotalEntries,
                        CurrentFile = Path.GetFileName(entry.OriginalPath)
                    });
                }
            }

            return result;
        }

        public string? GetLatestSessionDirectory()
        {
            if (!Directory.Exists(_baseBackupDirectory))
            {
                return null;
            }

            var dirs = Directory.GetDirectories(_baseBackupDirectory);
            var validSessions = new List<(string Directory, DateTimeOffset Timestamp)>();

            foreach (var dir in dirs)
            {
                string manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                DateTimeOffset timestamp = Directory.GetCreationTimeUtc(dir);
                try
                {
                    string json = File.ReadAllText(manifestPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("timestamp", out var tsProp) ||
                        doc.RootElement.TryGetProperty("Timestamp", out tsProp))
                    {
                        if (tsProp.TryGetDateTimeOffset(out var parsedTs))
                        {
                            timestamp = parsedTs;
                        }
                    }
                }
                catch
                {
                }

                validSessions.Add((dir, timestamp));
            }

            if (validSessions.Count == 0)
            {
                return null;
            }

            return validSessions
                .OrderByDescending(s => s.Timestamp)
                .ThenByDescending(s => Path.GetFileName(s.Directory))
                .First()
                .Directory;
        }

        public List<string> GetBackupSessions()
        {
            if (!Directory.Exists(_baseBackupDirectory))
            {
                return new List<string>();
            }

            return Directory.GetDirectories(_baseBackupDirectory)
                .Where(dir => File.Exists(Path.Combine(dir, "manifest.json")))
                .OrderByDescending(dir => dir)
                .ToList();
        }

        public static string GetRelativePath(string filePath, string? rootFolderPath)
        {
            if (!string.IsNullOrEmpty(rootFolderPath))
            {
                try
                {
                    string fullRoot = Path.GetFullPath(rootFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string fullFile = Path.GetFullPath(filePath);
                    if (fullFile.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        return fullFile.Substring(fullRoot.Length + 1);
                    }
                }
                catch
                {
                }
            }

            string? parentDir = Path.GetFileName(Path.GetDirectoryName(filePath));
            string fileName = Path.GetFileName(filePath);
            return Path.Combine(string.IsNullOrEmpty(parentDir) ? "Unknown" : parentDir, fileName);
        }
    }
}

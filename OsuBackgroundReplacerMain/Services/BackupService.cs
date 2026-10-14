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

        [JsonPropertyName("relativePath")]
        public string? RelativePath { get; set; }
    }

    public class BackupManifest
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonPropertyName("completed")]
        public bool? Completed { get; set; }

        [JsonPropertyName("entries")]
        public List<BackupManifestEntry> Entries { get; set; } = new();
    }

    public class BackupSessionInfo
    {
        public string Directory { get; }
        public DateTimeOffset Timestamp { get; }
        public int FileCount { get; }

        public BackupSessionInfo(string directory, DateTimeOffset timestamp, int fileCount)
        {
            Directory = directory;
            Timestamp = timestamp;
            FileCount = fileCount;
        }
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

        public bool IsBackedUp(string filePath)
        {
            string full = Path.GetFullPath(filePath);
            return Entries.Any(e => string.Equals(e.OriginalPath, full, StringComparison.OrdinalIgnoreCase));
        }
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
            CancellationToken cancellationToken = default,
            string? songsFolderPath = null);
        Task<RestoreSessionResult> RestoreBackupSessionAsync(
            string sessionDirectory,
            IProgress<RestoreProgress>? progress = null,
            CancellationToken cancellationToken = default,
            string? songsFolderPath = null);
        string? GetLatestSessionDirectory();
        BackupSessionInfo? GetLatestSessionInfo();
        List<string> GetBackupSessions();
        List<string> CleanupOldSessions(int keepLastN);
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

            var storedOriginals = LoadStoredOriginals();

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
                current++;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!File.Exists(file))
                    {
                        result.Errors.Add(new FileErrorInfo(file, "File does not exist."));
                        continue;
                    }

                    string fullFile = Path.GetFullPath(file);
                    string? songsRelative = GetRootRelativePath(fullFile, rootFolderPath);

                    BackupManifestEntry? existing = null;
                    if (!storedOriginals.TryGetValue("abs:" + fullFile, out existing) && songsRelative != null)
                    {
                        storedOriginals.TryGetValue("rel:" + songsRelative, out existing);
                    }

                    if (existing != null)
                    {
                        result.Entries.Add(new BackupManifestEntry
                        {
                            OriginalPath = fullFile,
                            BackupPath = existing.BackupPath,
                            RelativePath = songsRelative ?? existing.RelativePath,
                            Timestamp = existing.Timestamp,
                            Sha256 = existing.Sha256
                        });
                        continue;
                    }

                    string relativePath = songsRelative ?? GetRelativePath(file, rootFolderPath);
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
                        OriginalPath = fullFile,
                        BackupPath = Path.GetFullPath(backupPath),
                        RelativePath = songsRelative,
                        Timestamp = DateTimeOffset.Now,
                        Sha256 = sha256
                    });
                }
                catch (OperationCanceledException)
                {
                    await TryWriteManifestAsync(sessionDir, result.Timestamp, result.Entries, completed: false);
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

            if (!await TryWriteManifestAsync(sessionDir, result.Timestamp, result.Entries, completed: true))
            {
                foreach (var entry in result.Entries)
                {
                    result.Errors.Add(new FileErrorInfo(entry.OriginalPath, "Failed to write backup manifest."));
                }
                result.Entries.Clear();
            }

            return result;
        }

        private static async Task<bool> TryWriteManifestAsync(
            string sessionDir,
            DateTimeOffset timestamp,
            List<BackupManifestEntry> entries,
            bool completed)
        {
            try
            {
                var manifest = new BackupManifest
                {
                    Timestamp = timestamp,
                    Completed = completed,
                    Entries = entries
                };

                string manifestPath = Path.Combine(sessionDir, "manifest.json");
                string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(manifestPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif"
        };

        private static bool TryResolveTargetPath(
            BackupManifestEntry entry,
            string? songsFolderPath,
            out string? targetPath,
            out string? errorMessage)
        {
            targetPath = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(entry.OriginalPath) && string.IsNullOrWhiteSpace(entry.RelativePath))
            {
                errorMessage = "Manifest entry has no path specified.";
                return false;
            }

            bool hasRelative = !string.IsNullOrWhiteSpace(entry.RelativePath);

            if (hasRelative)
            {
                string rel = entry.RelativePath!;

                if (rel.Contains(".."))
                {
                    errorMessage = $"Relative path '{rel}' contains invalid traversal ('..').";
                    return false;
                }

                if (Path.IsPathRooted(rel) || !string.IsNullOrEmpty(Path.GetPathRoot(rel)) || rel.StartsWith('/') || rel.StartsWith('\\'))
                {
                    errorMessage = $"Relative path '{rel}' is rooted or contains a drive specifier.";
                    return false;
                }

                string candidate;
                if (!string.IsNullOrEmpty(songsFolderPath))
                {
                    string fullSongs = Path.GetFullPath(songsFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    candidate = Path.GetFullPath(Path.Combine(fullSongs, rel));

                    if (!string.Equals(Path.GetPathRoot(candidate), Path.GetPathRoot(fullSongs), StringComparison.OrdinalIgnoreCase) ||
                        !candidate.StartsWith(fullSongs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Target path '{candidate}' escapes the Songs folder.";
                        return false;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(entry.OriginalPath) || !Path.IsPathFullyQualified(entry.OriginalPath))
                    {
                        errorMessage = "Manifest entry lacks valid Songs folder context and absolute OriginalPath.";
                        return false;
                    }
                    candidate = Path.GetFullPath(entry.OriginalPath);
                }

                string ext = Path.GetExtension(candidate);
                if (string.IsNullOrEmpty(ext) || !AllowedImageExtensions.Contains(ext))
                {
                    errorMessage = $"Target path '{candidate}' does not have a supported image extension.";
                    return false;
                }

                targetPath = candidate;
                return true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(entry.OriginalPath))
                {
                    errorMessage = "Manifest entry has empty original path.";
                    return false;
                }

                if (entry.OriginalPath.Contains(".."))
                {
                    errorMessage = $"Original path '{entry.OriginalPath}' contains invalid traversal ('..').";
                    return false;
                }

                if (!Path.IsPathFullyQualified(entry.OriginalPath))
                {
                    errorMessage = $"Legacy original path '{entry.OriginalPath}' is not absolute.";
                    return false;
                }

                string candidate = Path.GetFullPath(entry.OriginalPath);

                string ext = Path.GetExtension(candidate);
                if (string.IsNullOrEmpty(ext) || !AllowedImageExtensions.Contains(ext))
                {
                    errorMessage = $"Original path '{candidate}' does not have a supported image extension.";
                    return false;
                }

                if (!string.IsNullOrEmpty(songsFolderPath))
                {
                    string fullSongs = Path.GetFullPath(songsFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!string.Equals(Path.GetPathRoot(candidate), Path.GetPathRoot(fullSongs), StringComparison.OrdinalIgnoreCase) ||
                        !candidate.StartsWith(fullSongs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Original path '{candidate}' is outside the selected Songs folder '{fullSongs}'.";
                        return false;
                    }
                }

                targetPath = candidate;
                return true;
            }
        }

        public async Task<RestoreSessionResult> RestoreLastBackupAsync(
            IProgress<RestoreProgress>? progress = null,
            CancellationToken cancellationToken = default,
            string? songsFolderPath = null)
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

            return await RestoreBackupSessionAsync(latestSession, progress, cancellationToken, songsFolderPath);
        }

        public async Task<RestoreSessionResult> RestoreBackupSessionAsync(
            string sessionDirectory,
            IProgress<RestoreProgress>? progress = null,
            CancellationToken cancellationToken = default,
            string? songsFolderPath = null)
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
                manifest = ParseManifestJson(json, sessionDirectory);
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

            if (manifest.Completed == false)
            {
                result.FailedCount = 1;
                result.Items.Add(new RestoreItemResult
                {
                    Status = RestoreStatus.Failed,
                    ErrorMessage = "Backup session is incomplete (cancelled or failed) and cannot be restored."
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
                    if (!TryResolveTargetPath(entry, songsFolderPath, out string? targetPath, out string? pathError))
                    {
                        result.SkippedCount++;
                        result.Items.Add(new RestoreItemResult
                        {
                            OriginalPath = entry.OriginalPath,
                            BackupPath = entry.BackupPath,
                            Status = RestoreStatus.Skipped,
                            ErrorMessage = pathError
                        });
                        continue;
                    }

                    string backupFile = ResolveBackupFile(entry, sessionDirectory);

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

                    string? targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    await Task.Run(() => File.Copy(backupFile, targetPath!, true), cancellationToken);

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
            return GetValidSessions().FirstOrDefault()?.Directory;
        }

        public BackupSessionInfo? GetLatestSessionInfo()
        {
            return GetValidSessions().FirstOrDefault();
        }

        public List<string> GetBackupSessions()
        {
            return GetValidSessions().Select(s => s.Directory).ToList();
        }

        public List<string> CleanupOldSessions(int keepLastN)
        {
            var deletedSessions = new List<string>();
            if (keepLastN <= 0)
            {
                return deletedSessions;
            }

            var validSessions = GetValidSessions(); // Newest first
            if (validSessions.Count <= keepLastN)
            {
                return deletedSessions;
            }

            var currentSessions = validSessions.ToList();
            var candidates = currentSessions.Skip(keepLastN).OrderBy(s => s.Timestamp).ToList();

            foreach (var candidate in candidates)
            {
                var otherSessions = currentSessions.Where(s => !string.Equals(s.Directory, candidate.Directory, StringComparison.OrdinalIgnoreCase)).ToList();

                if (ContainsOnlyCopyOfAnyOriginal(candidate, otherSessions))
                {
                    // Never delete the session that contains the only copy of an original file
                    continue;
                }

                try
                {
                    if (Directory.Exists(candidate.Directory))
                    {
                        Directory.Delete(candidate.Directory, true);
                    }
                    deletedSessions.Add(candidate.Directory);
                    currentSessions.Remove(candidate);
                }
                catch
                {
                    // Ignore deletion errors (e.g. file lock or permissions)
                }
            }

            return deletedSessions;
        }

        private static bool ContainsOnlyCopyOfAnyOriginal(BackupSessionInfo candidate, List<BackupSessionInfo> otherSessions)
        {
            if (!Directory.Exists(candidate.Directory))
            {
                return false;
            }

            string fullCandidateDir = Path.GetFullPath(candidate.Directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var physicalFiles = Directory.GetFiles(candidate.Directory, "*", SearchOption.AllDirectories)
                .Where(f => !string.Equals(Path.GetFileName(f), "manifest.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (physicalFiles.Count == 0)
            {
                return false;
            }

            var otherHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var other in otherSessions)
            {
                if (!Directory.Exists(other.Directory)) continue;

                string fullOtherDir = Path.GetFullPath(other.Directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                string manifestPath = Path.Combine(other.Directory, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var manifest = ParseManifestJson(File.ReadAllText(manifestPath), other.Directory);
                        if (manifest?.Entries != null)
                        {
                            foreach (var entry in manifest.Entries)
                            {
                                string backupFile = ResolveBackupFile(entry, other.Directory);
                                if (File.Exists(backupFile) && Path.GetFullPath(backupFile).StartsWith(fullOtherDir, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!string.IsNullOrEmpty(entry.Sha256))
                                    {
                                        otherHashes.Add(entry.Sha256);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (var otherFile in Directory.GetFiles(other.Directory, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFileName(otherFile), "manifest.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        string hash = ComputeSha256(otherFile);
                        otherHashes.Add(hash);
                    }
                    catch
                    {
                    }
                }
            }

            var candidateManifestHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string candidateManifestPath = Path.Combine(candidate.Directory, "manifest.json");
            if (File.Exists(candidateManifestPath))
            {
                try
                {
                    var manifest = ParseManifestJson(File.ReadAllText(candidateManifestPath), candidate.Directory);
                    if (manifest?.Entries != null)
                    {
                        foreach (var entry in manifest.Entries)
                        {
                            string backupFile = ResolveBackupFile(entry, candidate.Directory);
                            if (Path.GetFullPath(backupFile).StartsWith(fullCandidateDir, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrEmpty(entry.Sha256))
                                {
                                    candidateManifestHashes[Path.GetFullPath(backupFile)] = entry.Sha256;
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            foreach (var file in physicalFiles)
            {
                string fullPath = Path.GetFullPath(file);
                if (!candidateManifestHashes.TryGetValue(fullPath, out string? hash) || string.IsNullOrEmpty(hash))
                {
                    try
                    {
                        hash = ComputeSha256(file);
                    }
                    catch
                    {
                        return true;
                    }
                }

                if (!otherHashes.Contains(hash))
                {
                    return true;
                }
            }

            return false;
        }

        private List<BackupSessionInfo> GetValidSessions()
        {
            var sessions = new List<BackupSessionInfo>();
            if (!Directory.Exists(_baseBackupDirectory))
            {
                return sessions;
            }

            foreach (var dir in Directory.GetDirectories(_baseBackupDirectory))
            {
                string manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    var manifest = ParseManifestJson(File.ReadAllText(manifestPath), dir);
                    if (manifest == null || manifest.Completed == false || manifest.Entries == null || manifest.Entries.Count == 0)
                    {
                        continue;
                    }

                    DateTimeOffset timestamp = manifest.Timestamp == default
                        ? Directory.GetCreationTimeUtc(dir)
                        : manifest.Timestamp;
                    sessions.Add(new BackupSessionInfo(dir, timestamp, manifest.Entries.Count));
                }
                catch
                {
                }
            }

            return sessions
                .OrderByDescending(s => s.Timestamp)
                .ThenByDescending(s => Path.GetFileName(s.Directory), StringComparer.Ordinal)
                .ToList();
        }

        private static BackupManifest? ParseManifestJson(string json, string sessionDirectory)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                return JsonSerializer.Deserialize<BackupManifest>(json, options);
            }
            catch
            {
                var list = JsonSerializer.Deserialize<List<BackupManifestEntry>>(json, options);
                return list == null
                    ? null
                    : new BackupManifest
                    {
                        Timestamp = Directory.GetCreationTimeUtc(sessionDirectory),
                        Entries = list
                    };
            }
        }

        private Dictionary<string, BackupManifestEntry> LoadStoredOriginals()
        {
            var stored = new Dictionary<string, BackupManifestEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in GetValidSessions().OrderBy(s => s.Timestamp))
            {
                BackupManifest? manifest;
                try
                {
                    manifest = ParseManifestJson(File.ReadAllText(Path.Combine(session.Directory, "manifest.json")), session.Directory);
                }
                catch
                {
                    continue;
                }

                foreach (var entry in manifest?.Entries ?? new List<BackupManifestEntry>())
                {
                    if (string.IsNullOrWhiteSpace(entry.OriginalPath))
                    {
                        continue;
                    }

                    string backupFile = ResolveBackupFile(entry, session.Directory);
                    if (!File.Exists(backupFile))
                    {
                        continue;
                    }

                    var resolved = new BackupManifestEntry
                    {
                        OriginalPath = entry.OriginalPath,
                        BackupPath = backupFile,
                        RelativePath = entry.RelativePath,
                        Timestamp = entry.Timestamp,
                        Sha256 = entry.Sha256
                    };

                    stored.TryAdd("abs:" + Path.GetFullPath(entry.OriginalPath), resolved);
                    if (!string.IsNullOrEmpty(entry.RelativePath))
                    {
                        stored.TryAdd("rel:" + entry.RelativePath, resolved);
                    }
                }
            }

            return stored;
        }

        private static string ResolveBackupFile(BackupManifestEntry entry, string sessionDirectory)
        {
            if (File.Exists(entry.BackupPath))
            {
                return entry.BackupPath;
            }

            string rel = !string.IsNullOrEmpty(entry.RelativePath)
                ? entry.RelativePath
                : GetRelativePath(entry.OriginalPath, null);
            string fallback = Path.Combine(sessionDirectory, rel);
            return File.Exists(fallback) ? fallback : entry.BackupPath;
        }

        private static string? GetRootRelativePath(string fullFilePath, string? rootFolderPath)
        {
            if (string.IsNullOrEmpty(rootFolderPath))
            {
                return null;
            }

            try
            {
                string fullRoot = Path.GetFullPath(rootFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (fullFilePath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return fullFilePath.Substring(fullRoot.Length + 1);
                }
            }
            catch
            {
            }

            return null;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OsuBackgroundReplacerMain.Logic
{
    public static class OsuBeatmapParser
    {
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        /// <summary>
        /// Reads all *.osu files in the specified beatmap folder and extracts distinct background image filenames.
        /// Returns an empty set if no background entry is found, or if no .osu files exist.
        /// </summary>
        public static HashSet<string> GetBackgroundFiles(string beatmapFolder)
        {
            var backgroundFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(beatmapFolder) || !Directory.Exists(beatmapFolder))
            {
                return backgroundFiles;
            }

            string[] osuFiles;
            try
            {
                osuFiles = Directory.GetFiles(beatmapFolder, "*.osu");
            }
            catch
            {
                return backgroundFiles;
            }

            foreach (var osuFile in osuFiles)
            {
                string? bg = ExtractBackgroundFilename(osuFile);
                if (!string.IsNullOrEmpty(bg))
                {
                    backgroundFiles.Add(bg);
                }
            }

            return backgroundFiles;
        }

        /// <summary>
        /// Extracts the background filename from a single .osu file.
        /// Returns null if missing [Events] section or no valid background line.
        /// </summary>
        public static string? ExtractBackgroundFilename(string osuFilePath)
        {
            if (string.IsNullOrEmpty(osuFilePath) || !File.Exists(osuFilePath))
            {
                return null;
            }

            try
            {
                using var stream = new FileStream(osuFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);

                bool inEventsSection = false;
                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();

                    // Check for section headers [SectionName]
                    if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                    {
                        if (trimmed.Equals("[Events]", StringComparison.OrdinalIgnoreCase))
                        {
                            inEventsSection = true;
                        }
                        else if (inEventsSection)
                        {
                            // Left [Events] section into another section
                            break;
                        }
                        continue;
                    }

                    if (!inEventsSection)
                    {
                        continue;
                    }

                    // Ignore comments or empty lines
                    if (trimmed.StartsWith("//") || trimmed.Length == 0)
                    {
                        continue;
                    }

                    string? bg = ParseBackgroundEventLine(line);
                    if (bg != null)
                    {
                        return bg;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Parses a single event line to check if it's a type 0 (Background) event and returns the filename.
        /// Format: 0,0,"bg.jpg",0,0 (or unquoted filename, optional whitespace).
        /// Ignores video events (type 1 / Video) and storyboard sprite lines (Sprite/Animation).
        /// </summary>
        public static string? ParseBackgroundEventLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var parts = ParseCsvLine(line);
            if (parts.Count < 3)
            {
                return null;
            }

            string eventType = parts[0].Trim();
            // Only event type 0 background line counts (ignoring 1 / Video, Sprite, Animation, etc.)
            if (eventType != "0")
            {
                return null;
            }

            string filename = parts[2].Trim();
            if (filename.StartsWith('"') && filename.EndsWith('"') && filename.Length >= 2)
            {
                filename = filename.Substring(1, filename.Length - 2).Trim();
            }

            return string.IsNullOrEmpty(filename) ? null : filename;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    current.Append(c);
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result;
        }
    }
}

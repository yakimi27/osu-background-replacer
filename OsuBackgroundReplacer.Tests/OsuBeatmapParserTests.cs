using System;
using System.IO;
using System.Text;
using OsuBackgroundReplacerMain.Logic;
using Xunit;

namespace OsuBackgroundReplacer.Tests;

public class OsuBeatmapParserTests : IDisposable
{
    private readonly string _tempDirectory;

    public OsuBeatmapParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "OsuParserTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup failures in test teardown
        }
    }

    [Fact]
    public void ParseBackgroundEventLine_NormalBgLine_ReturnsFilename()
    {
        string line1 = "0,0,\"bg.jpg\",0,0";
        string line2 = "0, 0, \"my background.png\", 0, 0";
        string line3 = "0,0,bg_unquoted.jpeg,0,0";
        string line4 = " 0 , 0 , \"spaced.jpg\" , 0 , 0 ";

        Assert.Equal("bg.jpg", OsuBeatmapParser.ParseBackgroundEventLine(line1));
        Assert.Equal("my background.png", OsuBeatmapParser.ParseBackgroundEventLine(line2));
        Assert.Equal("bg_unquoted.jpeg", OsuBeatmapParser.ParseBackgroundEventLine(line3));
        Assert.Equal("spaced.jpg", OsuBeatmapParser.ParseBackgroundEventLine(line4));
    }

    [Fact]
    public void ParseBackgroundEventLine_NoBgLine_ReturnsNull()
    {
        Assert.Null(OsuBeatmapParser.ParseBackgroundEventLine(""));
        Assert.Null(OsuBeatmapParser.ParseBackgroundEventLine("   "));
        Assert.Null(OsuBeatmapParser.ParseBackgroundEventLine("// Background and Video events"));
        Assert.Null(OsuBeatmapParser.ParseBackgroundEventLine("2, 1000, 2000")); // Break line
    }

    [Fact]
    public void ParseBackgroundEventLine_VideoAndSpriteLines_ReturnsNull()
    {
        // Video event: type 1 or Video
        string videoLine1 = "1,0,\"video.avi\",0,0";
        string videoLine2 = "Video,0,\"video.mp4\"";
        string spriteLine = "Sprite,Pass,Centre,\"sb\\sprite.png\",320,240";
        string animationLine = "Animation,Fail,Centre,\"sb\\anim.png\",320,240,10,50,LoopForever";

        Assert.Null(OsuBeatmapParser.ParseBackgroundEventLine(videoLine1));
        Assert.Null(OsuBeatmapParser.ParseBackgroundEventLine(videoLine2));
        Assert.Null(OsuBeatmapParser.ParseBackgroundEventLine(spriteLine));
        Assert.Null(OsuBeatmapParser.ParseBackgroundEventLine(animationLine));
    }

    [Fact]
    public void ExtractBackgroundFilename_NormalBeatmapWithBom_ReturnsBgFilename()
    {
        string osuContent =
@"osu file format v14

[General]
AudioFilename: audio.mp3

[Events]
//Background and Video events
0,0,""epic_bg.jpg"",0,0
1,0,""intro.avi"",0,0

[TimingPoints]
100,500,4,2,0,100,1,0
";
        string filePath = Path.Combine(_tempDirectory, "beatmap.osu");
        // Write with UTF-8 BOM to ensure BOM skipping works
        File.WriteAllText(filePath, osuContent, new UTF8Encoding(true));

        string? bg = OsuBeatmapParser.ExtractBackgroundFilename(filePath);
        Assert.Equal("epic_bg.jpg", bg);
    }

    [Fact]
    public void ExtractBackgroundFilename_MissingEventsSection_ReturnsNull()
    {
        string osuContent =
@"osu file format v14

[General]
AudioFilename: audio.mp3

[Difficulty]
HPDrainRate:5
";
        string filePath = Path.Combine(_tempDirectory, "no_events.osu");
        File.WriteAllText(filePath, osuContent, Encoding.UTF8);

        string? bg = OsuBeatmapParser.ExtractBackgroundFilename(filePath);
        Assert.Null(bg);
    }

    [Fact]
    public void ExtractBackgroundFilename_MalformedFile_ReturnsNullWithoutThrowing()
    {
        string filePath = Path.Combine(_tempDirectory, "corrupted.osu");
        File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xFE, 0x00, 0x12, 0x34, 0x56 });

        string? bg = OsuBeatmapParser.ExtractBackgroundFilename(filePath);
        Assert.Null(bg);

        // Also test non-existent file
        Assert.Null(OsuBeatmapParser.ExtractBackgroundFilename(Path.Combine(_tempDirectory, "non_existent.osu")));
    }

    [Fact]
    public void GetBackgroundFiles_MultipleOsuFiles_CollectsDistinctSet()
    {
        string beatmapFolder = Path.Combine(_tempDirectory, "MapSet1");
        Directory.CreateDirectory(beatmapFolder);

        // Diff 1: uses bg1.jpg
        string diff1 =
@"[Events]
0,0,""bg1.jpg"",0,0
";
        // Diff 2: also uses bg1.jpg (case insensitive variation)
        string diff2 =
@"[Events]
0,0,""BG1.JPG"",0,0
";
        // Diff 3: uses bg2.png
        string diff3 =
@"[Events]
0,0,""bg2.png"",0,0
";
        // Diff 4: has no bg line (only video)
        string diff4 =
@"[Events]
1,0,""video.avi"",0,0
";

        File.WriteAllText(Path.Combine(beatmapFolder, "diff1.osu"), diff1, Encoding.UTF8);
        File.WriteAllText(Path.Combine(beatmapFolder, "diff2.osu"), diff2, Encoding.UTF8);
        File.WriteAllText(Path.Combine(beatmapFolder, "diff3.osu"), diff3, Encoding.UTF8);
        File.WriteAllText(Path.Combine(beatmapFolder, "diff4.osu"), diff4, Encoding.UTF8);

        var backgrounds = OsuBeatmapParser.GetBackgroundFiles(beatmapFolder);

        Assert.Equal(2, backgrounds.Count);
        Assert.Contains("bg1.jpg", backgrounds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("bg2.png", backgrounds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetBackgroundFiles_MissingBackgroundEntries_ReturnsEmptySet()
    {
        string beatmapFolder = Path.Combine(_tempDirectory, "MapSetNoBg");
        Directory.CreateDirectory(beatmapFolder);

        string diffWithoutBg =
@"[Events]
// No bg here
1,0,""video.mp4"",0,0
";
        File.WriteAllText(Path.Combine(beatmapFolder, "nobg.osu"), diffWithoutBg, Encoding.UTF8);

        var backgrounds = OsuBeatmapParser.GetBackgroundFiles(beatmapFolder);
        Assert.Empty(backgrounds);
    }
}

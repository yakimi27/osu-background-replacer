using OsuBackgroundReplacerMain.Logic;
using Xunit;

namespace OsuBackgroundReplacer.Tests;

public class DragDropValidationTests
{
    [Theory]
    [InlineData("image.jpg", true)]
    [InlineData("photo.JPEG", true)]
    [InlineData("art.png", true)]
    [InlineData("folder/nested/wallpaper.PNG", true)]
    [InlineData("document.txt", false)]
    [InlineData("beatmap.osu", false)]
    [InlineData("video.mp4", false)]
    [InlineData("program.exe", false)]
    [InlineData("", false)]
    [InlineData("no_extension", false)]
    public void IsSupportedImage_ValidatesExtensionsCorrectly(string fileName, bool expected)
    {
        bool actual = Constants.IsSupportedImage(fileName);
        Assert.Equal(expected, actual);
    }
}

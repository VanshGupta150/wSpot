using wSpot.Services;
using Xunit;

namespace wSpot.Tests
{
    public class FileResultFilterTests
    {
        [Theory]
        [InlineData(@"C:\Tools\app.dll")]
        [InlineData(@"C:\Windows\System32\drivers\net.sys")]
        [InlineData(@"C:\Temp\cache.tmp")]
        [InlineData(@"C:\App\debug.pdb")]
        [InlineData(@"C:\App\settings.ini")]
        public void IsNoise_NoiseExtensions_ReturnsTrue(string path)
        {
            Assert.True(FileResultFilter.IsNoise(path));
        }

        [Theory]
        [InlineData(@"C:\Users\John\AppData\Local\Google\Chrome\data.txt")]
        [InlineData(@"C:\Projects\webapp\node_modules\express\index.js")]
        [InlineData(@"C:\Projects\repo\.git\config")]
        [InlineData(@"C:\Projects\app\bin\Debug\net8.0\app.exe")]
        [InlineData(@"C:\Windows\System32\notepad.exe")]
        [InlineData(@"C:\Program Files\App\readme.txt")]
        public void IsNoise_NoiseFolders_ReturnsTrue(string path)
        {
            Assert.True(FileResultFilter.IsNoise(path));
        }

        [Theory]
        [InlineData(@"C:\Users\Student\Documents\SE_Report.docx")]
        [InlineData(@"D:\Projects\wSpot\Documentation.md")]
        [InlineData(@"C:\Users\Student\Desktop\presentation.pptx")]
        [InlineData(@"D:\Music\favorite_song.mp3")]
        public void IsNoise_UserDocumentsAndMedia_ReturnsFalse(string path)
        {
            Assert.False(FileResultFilter.IsNoise(path));
        }

        [Fact]
        public void IsNoise_EmptyOrNull_ReturnsTrue()
        {
            Assert.True(FileResultFilter.IsNoise(""));
            Assert.True(FileResultFilter.IsNoise("   "));
        }
    }
}

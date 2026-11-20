using wSpot.Models;
using Xunit;

namespace wSpot.Tests
{
    public class AppEntryTests
    {
        [Fact]
        public void UsageKey_ForApp_CombinesKindTargetAndName()
        {
            var app = new AppEntry
            {
                Kind = ResultKind.App,
                Name = "Notepad",
                TargetPath = @"C:\Windows\notepad.exe"
            };

            Assert.Equal(@"app|c:\windows\notepad.exe|notepad", app.UsageKey);
        }

        [Fact]
        public void UsageKey_ForCommand_CombinesKindAndName()
        {
            var cmd = new AppEntry
            {
                Kind = ResultKind.Command,
                Name = "Lock"
            };

            Assert.Equal("command|lock", cmd.UsageKey);
        }

        [Fact]
        public void UsageKey_ForFile_CombinesKindAndTargetPath()
        {
            var file = new AppEntry
            {
                Kind = ResultKind.File,
                Name = "Report.pdf",
                TargetPath = @"D:\Docs\Report.pdf"
            };

            Assert.Equal(@"file|d:\docs\report.pdf", file.UsageKey);
        }

        [Fact]
        public void KindLabel_ReturnsExpectedBadges()
        {
            Assert.Equal("App", new AppEntry { Kind = ResultKind.App }.KindLabel);
            Assert.Equal("File", new AppEntry { Kind = ResultKind.File }.KindLabel);
            Assert.Equal("Folder", new AppEntry { Kind = ResultKind.Folder }.KindLabel);
            Assert.Equal("Command", new AppEntry { Kind = ResultKind.Command }.KindLabel);
            Assert.Equal("Calculator", new AppEntry { Kind = ResultKind.Calculator }.KindLabel);
            Assert.Equal("Web", new AppEntry { Kind = ResultKind.WebSearch }.KindLabel);
        }
    }
}

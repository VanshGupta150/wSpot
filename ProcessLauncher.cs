using System.Diagnostics;
using wSpot.Models;

namespace wSpot.Services
{
    public static class ProcessLauncher
    {
        public static void Launch(AppEntry app)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = app.TargetPath,
                Arguments = app.Arguments,
                UseShellExecute = true 
            };

            Process.Start(startInfo);
        }
    }
}

using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace wSpot.Services
{
    /// <summary>
    /// Built-in actions that are a function call rather than a program to start.
    /// Each returns an error message, or null when it worked (the shape of AppEntry.Run).
    /// </summary>
    public static class SystemActions
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        /// <summary>
        /// Puts the PC to sleep (not hibernate).
        ///
        /// Calling the API directly is deliberate. The well-known command
        /// "rundll32 powrprof.dll,SetSuspendState" passes the wrong first argument and
        /// hibernates instead of sleeping on PCs where hibernation is enabled.
        ///
        /// SetSuspendState doesn't return until the PC wakes up again, so it runs on a
        /// worker thread. Otherwise the launcher itself would freeze for the whole sleep.
        /// Because of that, a failure to sleep isn't reported back.
        /// </summary>
        public static string? Sleep()
        {
            Task.Run(() => SetSuspendState(false, false, false));
            return null;
        }
    }
}

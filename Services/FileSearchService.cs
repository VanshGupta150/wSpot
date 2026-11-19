using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace wSpot.Services
{
    public enum FileSearchSource
    {
        Everything,
        FolderScan
    }

    /// <summary>
    /// What a file search found, and which engine found it. Note is empty when all is
    /// well, or a short explanation for the UI when we had to fall back.
    /// </summary>
    public sealed record FileSearchResult(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Folders,
        FileSearchSource Source,
        string Note);

    /// <summary>
    /// The one place the rest of the app asks for files. It tries Everything first
    /// and quietly falls back to scanning a few folders if Everything isn't usable.
    /// </summary>
    public class FileSearchService
    {
        // How many candidate files to ask for; the ViewModel then ranks and trims them.
        private const int CandidateCount = 100;

        // After Everything fails we skip it for a few seconds, so we don't start a
        // failing process on every keystroke.
        private static readonly TimeSpan EverythingRetryDelay = TimeSpan.FromSeconds(5);

        private readonly EverythingClient _everything = new EverythingClient();
        private readonly FolderScanFileSearcher _fallback = new FolderScanFileSearcher();

        private DateTime _everythingRetryAtUtc = DateTime.MinValue;
        private string _fallbackNote = string.Empty;

        /// <summary>
        /// Call once at startup. If es.exe isn't present we already know the fallback
        /// will be needed, so start its folder scan now instead of at the first search.
        /// </summary>
        public void WarmUp()
        {
            if (_everything.FindEsExe() is null)
                _fallback.WarmUp();
        }

        public async Task<FileSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
        {
            if (DateTime.UtcNow >= _everythingRetryAtUtc)
            {
                EverythingResult result = await _everything
                    .SearchAsync(query, CandidateCount, cancellationToken)
                    .ConfigureAwait(false);

                if (result.Status == EverythingStatus.Available)
                {
                    List<string> files = result.FilePaths.Where(path => !FileResultFilter.IsNoise(path)).ToList();
                    List<string> folders = result.FolderPaths.Where(path => !FileResultFilter.IsInNoiseFolder(path + "\\")).ToList();
                    return new FileSearchResult(files, folders, FileSearchSource.Everything, string.Empty);
                }

                _everythingRetryAtUtc = DateTime.UtcNow + EverythingRetryDelay;
                _fallbackNote = result.Message + " — searching " + _fallback.Description + " only";
            }

            var fallback = await _fallback
                .SearchAsync(query, CandidateCount, cancellationToken)
                .ConfigureAwait(false);

            return new FileSearchResult(fallback.Files, fallback.Folders, FileSearchSource.FolderScan, _fallbackNote);
        }
    }
}

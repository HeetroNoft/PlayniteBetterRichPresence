using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterRichPresence
{
    internal sealed class FoundProcess
    {
        public int Id { get; set; }
        public string Path { get; set; }
    }

    internal static class GameProcess
    {
        public static string GetPath(int processId)
        {
            if (processId <= 0)
            {
                return null;
            }

            // Playnite is 32-bit, so Process.MainModule fails on 64-bit games; this API doesn't.
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var buffer = new StringBuilder(1024);
                var size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static bool IsUnder(string path, string directory)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory))
            {
                return false;
            }

            var root = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Finds the game's own process among those running from its install folder,
        /// preferring one with a window (the game rather than a crash handler or helper).
        /// </summary>
        public static FoundProcess FindIn(string installDirectory)
        {
            if (string.IsNullOrEmpty(installDirectory) || !Directory.Exists(installDirectory))
            {
                return null;
            }

            var ownId = Process.GetCurrentProcess().Id;
            var candidates = Process.GetProcesses()
                .Where(p => p.Id != ownId)
                .Select(p => new { Process = p, Path = GetPath(p.Id) })
                .Where(c => IsUnder(c.Path, installDirectory))
                .ToList();

            var best = candidates.FirstOrDefault(c => HasWindow(c.Process)) ?? candidates.FirstOrDefault();
            return best == null ? null : new FoundProcess { Id = best.Process.Id, Path = best.Path };
        }

        private static bool HasWindow(Process process)
        {
            try
            {
                return process.MainWindowHandle != IntPtr.Zero;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private const int ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

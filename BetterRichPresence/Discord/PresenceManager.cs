using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BetterRichPresence.Discord
{
    internal sealed class Presence
    {
        public string ApplicationId { get; set; }
        /// <summary>Process the activity belongs to; 0 means Playnite itself.</summary>
        public int ProcessId { get; set; }
        public Dictionary<string, object> Activity { get; set; }
    }

    /// <summary>
    /// Pushes the latest requested presence to Discord from a background thread,
    /// reconnecting when Discord starts later or the application ID changes.
    /// </summary>
    internal sealed class PresenceManager : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);

        private readonly object sync = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread worker;
        private readonly int pid = Process.GetCurrentProcess().Id;

        private Presence desired;
        private bool applied = true;
        private bool disposed;
        private DiscordIpcClient client;
        private int clientPid;

        public PresenceManager()
        {
            worker = new Thread(Run) { IsBackground = true, Name = "BetterRichPresence" };
            worker.Start();
        }

        /// <summary>Requests a new presence; null clears it.</summary>
        public void Set(Presence presence)
        {
            lock (sync)
            {
                desired = presence;
                applied = false;
            }

            wake.Set();
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposed = true;
            }

            wake.Set();
            worker.Join(TimeSpan.FromSeconds(3));
        }

        private void Run()
        {
            while (true)
            {
                wake.WaitOne(RetryInterval);

                Presence target;
                lock (sync)
                {
                    if (disposed)
                    {
                        break;
                    }

                    if (applied && (desired == null || client?.IsConnected == true))
                    {
                        continue;
                    }

                    target = desired;
                }

                var success = Apply(target);
                lock (sync)
                {
                    // Only mark as applied if nothing newer was requested meanwhile.
                    if (success && ReferenceEquals(target, desired))
                    {
                        applied = true;
                    }
                }
            }

            Clear();
        }

        private bool Apply(Presence target)
        {
            try
            {
                if (target == null)
                {
                    logger.Debug("Clearing presence");
                    Clear();
                    return true;
                }

                var targetPid = target.ProcessId > 0 ? target.ProcessId : pid;
                // A new connection per process, so no activity stays attached to the previous one.
                if (client == null || client.ClientId != target.ApplicationId || clientPid != targetPid || !client.IsConnected)
                {
                    Clear();
                    logger.Debug($"Connecting to Discord as app {target.ApplicationId} for pid {targetPid}");
                    client = new DiscordIpcClient(target.ApplicationId);
                    if (!client.Connect())
                    {
                        client = null;
                        return false;
                    }
                }

                clientPid = targetPid;
                client.SetActivity(targetPid, target.Activity);
                return true;
            }
            catch (Exception e)
            {
                logger.Warn(e, "Failed to update Discord presence");
                client?.Dispose();
                client = null;
                return false;
            }
        }

        /// <summary>Explicitly empties the activity before disconnecting, rather than relying on Discord noticing the closed pipe.</summary>
        private void Clear()
        {
            if (client == null)
            {
                return;
            }

            try
            {
                if (client.IsConnected)
                {
                    client.SetActivity(clientPid, null);
                }
            }
            catch (Exception e)
            {
                logger.Debug($"Could not clear Discord activity: {e.Message}");
            }

            client.Dispose();
            client = null;
        }
    }
}

using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace BetterRichPresence.Discord
{
    /// <summary>
    /// Minimal Discord RPC client speaking the local IPC protocol directly.
    /// Playnite already loads its own DiscordRPC.dll, so we avoid that library
    /// to stay clear of version conflicts and to use fields it doesn't expose.
    /// </summary>
    internal sealed class DiscordIpcClient : IDisposable
    {
        private const int OpHandshake = 0;
        private const int OpFrame = 1;
        private const int OpClose = 2;
        private const int OpPing = 3;
        private const int OpPong = 4;

        private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
        private static readonly ILogger logger = LogManager.GetLogger();

        private NamedPipeClientStream pipe;

        public string ClientId { get; }
        public bool IsConnected => pipe?.IsConnected == true;

        public DiscordIpcClient(string clientId)
        {
            ClientId = clientId;
        }

        public bool Connect()
        {
            for (var i = 0; i < 10; i++)
            {
                var candidate = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    candidate.Connect(200);
                }
                catch (Exception)
                {
                    candidate.Dispose();
                    continue;
                }

                pipe = candidate;
                try
                {
                    Write(OpHandshake, new Dictionary<string, object> { ["v"] = 1, ["client_id"] = ClientId });
                    var ready = ReadFrame();
                    if (ready != null && ready.Contains("\"READY\""))
                    {
                        return true;
                    }

                    logger.Warn($"Discord handshake rejected for client {ClientId}: {ready}");
                }
                catch (Exception e)
                {
                    logger.Warn(e, $"Discord handshake failed on discord-ipc-{i}");
                }

                Close();
            }

            return false;
        }

        /// <summary>Sets the activity, or clears it when <paramref name="activity"/> is null.</summary>
        public void SetActivity(int pid, Dictionary<string, object> activity)
        {
            var args = new Dictionary<string, object> { ["pid"] = pid };
            if (activity != null)
            {
                args["activity"] = activity;
            }

            Write(OpFrame, new Dictionary<string, object>
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = args,
                ["nonce"] = Guid.NewGuid().ToString()
            });

            var response = ReadFrame();
            if (response != null && response.Contains("\"evt\":\"ERROR\""))
            {
                logger.Warn($"Discord rejected activity: {response}");
            }
            else
            {
                logger.Debug($"Activity set (app {ClientId}, pid {pid}): {response}");
            }
        }

        public void Close()
        {
            if (pipe == null)
            {
                return;
            }

            try
            {
                if (pipe.IsConnected)
                {
                    Write(OpClose, new Dictionary<string, object>());
                }
            }
            catch (Exception)
            {
                // Pipe already gone, nothing to tell Discord.
            }

            pipe.Dispose();
            pipe = null;
        }

        public void Dispose() => Close();

        private void Write(int opcode, object payload) => WriteRaw(opcode, Serialization.ToJson(payload));

        /// <summary>Reads frames until a non-ping frame arrives, answering pings on the way.</summary>
        private string ReadFrame()
        {
            while (true)
            {
                var header = ReadExact(8);
                var opcode = BitConverter.ToInt32(header, 0);
                var length = BitConverter.ToInt32(header, 4);
                var payload = Encoding.UTF8.GetString(ReadExact(length));

                switch (opcode)
                {
                    case OpPing:
                        WriteRaw(OpPong, payload);
                        continue;
                    case OpClose:
                        throw new IOException($"Discord closed the connection: {payload}");
                    default:
                        return payload;
                }
            }
        }

        private void WriteRaw(int opcode, string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var frame = new byte[8 + body.Length];
            BitConverter.GetBytes(opcode).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);
            pipe.Write(frame, 0, frame.Length);
            pipe.Flush();
        }

        private byte[] ReadExact(int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = pipe.ReadAsync(buffer, offset, count - offset);
                if (!read.Wait(ReadTimeout))
                {
                    throw new TimeoutException("Discord did not answer in time.");
                }

                if (read.Result == 0)
                {
                    throw new IOException("Discord pipe closed.");
                }

                offset += read.Result;
            }

            return buffer;
        }
    }
}

using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace BetterRichPresence
{
    internal sealed class ImgbbResponse
    {
        public class ImageData
        {
            [SerializationPropertyName("url")]
            public string Url { get; set; }
        }

        [SerializationPropertyName("data")]
        public ImageData Data { get; set; }
    }

    /// <summary>
    /// Discord only displays images reachable by URL, while Playnite stores icons and covers
    /// as local files. When the user opts in, this uploads them once to ImgBB and
    /// remembers the resulting public URL.
    /// </summary>
    internal sealed class ImageHost
    {
        private const string UploadEndpoint = "https://api.imgbb.com/1/upload";
        // Discord's image proxy can't fetch every host (catbox.moe fails), so only keep links from ImgBB.
        private const string HostedPrefix = "https://i.ibb.co/";
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly string cachePath;
        // Local file path → public URL.
        private readonly ConcurrentDictionary<string, string> urls;
        private readonly ConcurrentDictionary<string, Task<string>> pending = new ConcurrentDictionary<string, Task<string>>();

        public ImageHost(string dataPath)
        {
            cachePath = Path.Combine(dataPath, "uploaded-images.json");
            var cached = LoadCache().Where(p => p.Value.StartsWith(HostedPrefix, StringComparison.Ordinal));
            urls = new ConcurrentDictionary<string, string>(cached, StringComparer.OrdinalIgnoreCase);
        }

        public string TryGetUrl(string localPath)
        {
            return urls.TryGetValue(localPath, out var url) ? url : null;
        }

        /// <summary>Uploads the file unless already done; concurrent calls for the same file share one upload.</summary>
        public Task<string> UploadAsync(string localPath, string apiKey)
        {
            var known = TryGetUrl(localPath);
            if (known != null)
            {
                return Task.FromResult(known);
            }

            return pending.GetOrAdd(localPath, path => Task.Run(async () =>
            {
                try
                {
                    var url = await UploadFileAsync(path, apiKey).ConfigureAwait(false);
                    if (url != null)
                    {
                        urls[path] = url;
                        SaveCache();
                    }

                    return url;
                }
                finally
                {
                    pending.TryRemove(path, out _);
                }
            }));
        }

        private static async Task<string> UploadFileAsync(string path, string apiKey)
        {
            try
            {
                var png = ToPng(path);
                // Content-addressed name so the original file name (and user folders) never leak.
                var name = Hash(png);
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                using (var form = new MultipartFormDataContent())
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("BetterRichPresence/" + typeof(ImageHost).Assembly.GetName().Version);
                    http.DefaultRequestHeaders.ExpectContinue = false;
                    form.Add(new StringContent(name), "name");
                    form.Add(new ByteArrayContent(png), "image", name + ".png");
                    var response = await http.PostAsync(UploadEndpoint + "?key=" + Uri.EscapeDataString(apiKey), form).ConfigureAwait(false);
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var link = response.IsSuccessStatusCode ? Serialization.FromJson<ImgbbResponse>(body)?.Data?.Url : null;
                    if (link != null && link.StartsWith(HostedPrefix, StringComparison.Ordinal))
                    {
                        return link;
                    }

                    logger.Warn($"Image upload failed ({(int)response.StatusCode}): {body}");
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, $"Image upload failed for {path}");
            }

            return null;
        }

        /// <summary>Discord can't show .ico files; re-encodes the largest frame as PNG.</summary>
        private static byte[] ToPng(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadAllBytes(path);
            }

            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                using (var output = new MemoryStream())
                {
                    encoder.Save(output);
                    return output.ToArray();
                }
            }
        }

        private static string Hash(byte[] data)
        {
            using (var sha = SHA1.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private Dictionary<string, string> LoadCache()
        {
            try
            {
                if (File.Exists(cachePath))
                {
                    return Serialization.FromJsonFile<Dictionary<string, string>>(cachePath);
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Failed to read uploaded images cache");
            }

            return new Dictionary<string, string>();
        }

        private void SaveCache()
        {
            try
            {
                lock (urls)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                    File.WriteAllText(cachePath, Serialization.ToJson(urls.ToDictionary(p => p.Key, p => p.Value)));
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Failed to save uploaded images cache");
            }
        }
    }
}

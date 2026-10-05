using Avalonia.Media.Imaging;
using gamevault.UserControls;
using gamevault.Helper;
using GameVault.Core;
using gamevault.Models;
using gamevault.ViewModels;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    internal class CacheHelper
    {
        internal static async Task CreateOfflineCacheAsync(Game game)
        {
            try
            {
                string serializedObject = JsonSerializer.Serialize(game);
                string compressedObject = StringCompressor.CompressString(serializedObject);
                Preferences.Set(game.ID.ToString(), compressedObject, LoginManager.Instance.GetUserProfile().OfflineCache);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }

        internal static async Task LoadImageCacheToUIAsync(int identifier, int imageId, string cachePath, ImageCache cacheType, CacheImage img)
        {
            string cacheFile = Path.Combine(cachePath, $"{identifier}.{imageId}");
            try
            {
                if (imageId == -1)
                {
                    throw new Exception("image id does not exist");
                }
                if (File.Exists(cacheFile))
                {
                    if (cacheType == ImageCache.UserAvatar)
                    {
                        if (TaskQueue.Instance.IsAlreadyInProcess(imageId))
                        {
                            await TaskQueue.Instance.WaitForProcessToFinish(imageId);
                        }
                    }
                    //if file exists then return it directly
                    await img.SetImageFileAsync(cacheFile);
                }
                else
                {
                    if (!Directory.Exists(cachePath))
                    { Directory.CreateDirectory(cachePath); }
                    //Otherwise see if there are still cache images with the same identifier
                    string[] files = Directory.GetFiles(cachePath, $"{identifier}.*");
                    if (LoginManager.Instance.IsLoggedIn())
                    {
                        //when we are online we download the new image and delete an outdated one if available
                        if (files.Length > 0)
                        {
                            File.Delete(files[0]);
                        }
                        await TaskQueue.Instance.Enqueue(() => WebHelper.DownloadImageFromUrlAsync($"{SettingsViewModel.Instance.ServerUrl}/api/media/{imageId}", cacheFile), imageId);
                        await img.SetImageFileAsync(cacheFile);
                    }
                    else
                    {
                        if (files.Length > 0)
                        {
                            //if we are offline, we will try to load an old image with the same identifier
                            cacheFile = files[0];
                            await img.SetImageFileAsync(cacheFile);
                        }
                        else
                        {
                            //otherwise we load the 'No Boxart' image
                            throw new Exception();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (TaskQueue.Instance.IsAlreadyInProcess(imageId))
                    {
                        await TaskQueue.Instance.WaitForProcessToFinish(imageId);
                        await img.SetImageFileAsync(cacheFile);
                        return;
                    }
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
                img.SetReplacement();
            }
        }
        internal static async Task EnsureImageCacheForGame(Game game)
        {
            try
            {
                if (!LoginManager.Instance.IsLoggedIn())
                    return;
                // Games without metadata images (e.g. not matched yet) have nothing to cache
                await EnsureCachedMedia(game, "gbg", game.Metadata?.Background?.ID);
                await EnsureCachedMedia(game, "gbox", game.Metadata?.Cover?.ID);
            }
            catch (Exception ignored) { Log.Ignored(ignored); }
        }
        private static async Task EnsureCachedMedia(Game game, string folder, int? mediaId)
        {
            if (mediaId == null)
                return;
            if (TaskQueue.Instance.IsAlreadyInProcess(mediaId.Value))
            {
                await TaskQueue.Instance.WaitForProcessToFinish(mediaId.Value);
            }
            string directory = Path.Combine(LoginManager.Instance.GetUserProfile().ImageCacheDir, folder);
            Directory.CreateDirectory(directory);
            string cacheFile = Path.Combine(directory, $"{game.ID}.{mediaId}");
            if (!File.Exists(cacheFile))
            {
                //Not in que because its not loading images for the UI
                await WebHelper.DownloadImageFromUrlAsync($"{SettingsViewModel.Instance.ServerUrl}/api/media/{mediaId}", cacheFile);
            }
        }
        private static readonly System.Collections.Generic.Dictionary<ImageCache, Bitmap> replacementImages = new();

        internal static Bitmap GetReplacementImage(ImageCache cacheType)
        {
            if (replacementImages.TryGetValue(cacheType, out Bitmap? cached))
                return cached;
            Bitmap bitmap = LoadReplacementImage(cacheType);
            replacementImages[cacheType] = bitmap;
            return bitmap;
        }

        private static Bitmap LoadReplacementImage(ImageCache cacheType)
        {
            switch (cacheType)
            {
                case ImageCache.GameCover:
                    {
                        return BitmapHelper.GetBitmapImage("avares://gamevault/Resources/Images/library_NoGameCover.png");
                    }
                case ImageCache.UserAvatar:
                    {
                        return BitmapHelper.GetBitmapImage("avares://gamevault/Resources/Images/com_NoUserAvatar.png");

                    }
                default:
                    {
                        return BitmapHelper.GetBitmapImage("avares://gamevault/Resources/Images/gameView_NoBackground.jpg");
                    }
            }
        }

        internal static async Task OptimizeCache()
        {
            await Task.Run(() =>
            {
                try
                {
                    var screen = ScreenHelper.PrimaryScreenSize;
                    double maxHeight = screen.Height / 2;
                    string imageOptimizationMetadata = Path.Combine(LoginManager.Instance.GetUserProfile().ImageCacheDir, "optmetadata");

                    bool lastOptimizedSet = DateTime.TryParse(Preferences.Get(AppConfigKey.LastImageOptimization, imageOptimizationMetadata), out DateTime lastOptimized);
                    var files = Directory.GetFiles(LoginManager.Instance.GetUserProfile().ImageCacheDir, "*.*", SearchOption.AllDirectories);
                    foreach (string file in files)
                    {
                        if (file == imageOptimizationMetadata)
                            continue;
                        try
                        {
                            var image = new FileInfo(file);
                            if (!lastOptimizedSet || lastOptimized < image.LastWriteTime)
                            {
                                if (image.Length > 0)
                                {
                                    if (file.Contains("uico"))
                                    {
                                        if (GifHelper.IsGif(file))
                                        {
                                            uint maxGifHeightWidth = 400;
                                            GifHelper.OptimizeGIF(file, maxGifHeightWidth);
                                            image.Refresh();
                                            continue;
                                        }
                                    }
                                    ResizeImage(file, Convert.ToUInt32(maxHeight));
                                    image.Refresh();
                                }
                                else
                                {
                                    File.Delete(file);
                                }
                            }
                        }
                        catch (Exception ignored) { Log.Ignored(ignored); }
                    }
                    Preferences.Set(AppConfigKey.LastImageOptimization, DateTime.Now.ToString(), imageOptimizationMetadata);
                }
                catch (Exception ignored) { Log.Ignored(ignored); }
            });
        }
        internal static async Task<string> CreateHashAsync(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(input)))
            {
                byte[] bytes = await sha256.ComputeHashAsync(stream);
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                string hash = builder.ToString();
                return hash;
            }
        }
        internal static Dictionary<string, string> GetImageCacheForGame(Game game)
        {
            Dictionary<string, string> imageCache = new Dictionary<string, string>();
            string cachePath = LoginManager.Instance.GetUserProfile().ImageCacheDir;
            // The cache folders only exist once an image was cached (games without cover/background)
            string? FindCached(string folder) => Directory.Exists(Path.Combine(cachePath, folder))
                ? Directory.GetFiles(Path.Combine(cachePath, folder), $"{game.ID}.*").FirstOrDefault()
                : null;
            var boxArt = FindCached("gbox");
            var background = FindCached("gbg");
            imageCache.Add("gbox", boxArt);
            imageCache.Add("gbg", background);
            return imageCache;
        }
        internal static string GetUserProfileAvatarPath(UserProfile profile)
        {
            if (Directory.Exists(Path.Combine(profile.ImageCacheDir, "uico")))
            {
                if (int.TryParse(Preferences.Get(AppConfigKey.UserID, profile.UserConfigFile), out int userId))
                {
                    return Directory.GetFiles(Path.Combine(profile.ImageCacheDir, "uico"), $"{userId}.*", SearchOption.AllDirectories).FirstOrDefault() ?? "";
                }
            }
            return "";
        }
        private static void ResizeImage(string path, uint maxHeight)
        {
            using (var imageMagick = new MagickImage(path))
            {
                imageMagick.Format = MagickFormat.Jpeg;
                if (imageMagick.Width <= imageMagick.Height && imageMagick.Height > maxHeight)
                {
                    var size = new MagickGeometry(maxHeight);
                    size.IgnoreAspectRatio = false;
                    imageMagick.Resize(size);
                    imageMagick.Write(path);
                }
                else if (imageMagick.Height > ScreenHelper.PrimaryScreenSize.Height)
                {
                    var screen = ScreenHelper.PrimaryScreenSize;
                    var size = new MagickGeometry((uint)screen.Width, (uint)screen.Height);
                    size.IgnoreAspectRatio = false;
                    imageMagick.Resize(size);
                    imageMagick.Write(path);
                }
            }
        }
    }
}

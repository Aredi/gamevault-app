using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using gamevault.Windows;
using GameVault.Core;

[assembly: AvaloniaTestApplication(typeof(GameVault.UiTests.TestAppBuilder))]
// One app for all scenarios, like one session of a user
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
// The app has process-wide singletons (settings, login, downloads): the scenarios run one after the other
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GameVault.UiTests
{
    public class TestAppBuilder
    {
        // The showcase renders real frames with Skia; the scenarios only need the layout
        public static AppBuilder BuildAvaloniaApp() => ShowcaseScenarios.Enabled
            ? AppBuilder.Configure<gamevault.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            : AppBuilder.Configure<gamevault.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    internal static class IsolatedHome
    {
        public static string Root { get; } = Path.Combine(Path.GetTempPath(), $"gv-uitests-{Guid.NewGuid():N}");

        /// <summary>Before any app type is loaded: profiles, settings and games go to a temporary home.</summary>
        [ModuleInitializer]
        internal static void Redirect()
        {
            var folders = new Dictionary<string, string>
            {
                ["HOME"] = Root,
                ["XDG_CONFIG_HOME"] = Path.Combine(Root, ".config"),
                ["XDG_DATA_HOME"] = Path.Combine(Root, ".local", "share"),
                ["XDG_CACHE_HOME"] = Path.Combine(Root, ".cache"),
                ["APPDATA"] = Path.Combine(Root, "AppData", "Roaming"),
                ["LOCALAPPDATA"] = Path.Combine(Root, "AppData", "Local"),
            };
            foreach (var (variable, folder) in folders)
            {
                // GetFolderPath returns "" for a folder that does not exist yet
                Directory.CreateDirectory(folder);
                Environment.SetEnvironmentVariable(variable, folder);
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(Root, true); } catch { }
            };
        }
    }

    /// <summary>
    /// A logged-in client against the fake server, with the main window open. Created once: the app keeps
    /// its state in singletons, like a real session. Each scenario uses its own game ids.
    /// </summary>
    internal sealed class TestSession
    {
        private static TestSession? current;

        public FakeGameVaultServer Server { get; } = new();
        public string LibraryRoot { get; } = Path.Combine(IsolatedHome.Root, "Games");
        public MainWindow Window { get; private set; } = null!;

        public static async Task<TestSession> GetAsync()
        {
            if (current == null)
            {
                var session = new TestSession();
                await session.StartAsync();
                current = session;
            }
            return current;
        }

        private async Task StartAsync()
        {
            // The scenarios look for the English texts, whatever the language of the computer (the showcase is in French)
            if (!ShowcaseScenarios.Enabled)
                gamevault.Localization.Loc.Initialize("en");
            Directory.CreateDirectory(LibraryRoot);
            UserProfile profile = ProfileManager.CreateUserProfile(WebHelper.RemoveSpecialCharactersFromUrl(Server.Url));
            profile.ServerUrl = Server.Url;
            Preferences.Set(AppConfigKey.ServerUrl, Server.Url, profile.UserConfigFile, true);
            Preferences.Set(AppConfigKey.Username, "admin", profile.UserConfigFile);
            Preferences.Set(AppConfigKey.Password, "admin-password", profile.UserConfigFile, true);
            Preferences.Set(AppConfigKey.RootDirectories, LibraryRoot, profile.UserConfigFile);

            LoginState state = await LoginManager.Instance.Login(profile, "admin", "admin-password");
            Assert.True(state == LoginState.Success, $"Login: {state} {LoginManager.Instance.GetServerLoginResponseMessage()} | requests: {string.Join(", ", Server.Requests)}");
            LoginManager.Instance.SetUserProfile(profile);
            SettingsViewModel.Instance.Init();

            Window = new MainWindow();
            gamevault.App.Instance.MainWindow = Window;
            Window.Show();
            await WaitUntil(() => MainWindowViewModel.Instance.ActiveControlIndex >= 0, TimeSpan.FromSeconds(20), "the main window to load");
        }

        public string DownloadFolder(Game game) => Path.Combine(LibraryRoot, "GameVault", "Downloads", gamevault.UserControls.GameDownloadUserControl.GameFolderName(game));
        public string InstallFolder(Game game) => Path.Combine(LibraryRoot, "GameVault", "Installations", gamevault.UserControls.GameDownloadUserControl.GameFolderName(game));

        /// <summary>Waits while the UI thread keeps running (downloads report through it).</summary>
        public static async Task WaitUntil(Func<bool> condition, TimeSpan timeout, string what)
        {
            var end = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                    throw new TimeoutException($"Timed out waiting for {what}. Last app message: '{MainWindowViewModel.Instance.AppBarText}'");
                await Task.Delay(100);
            }
        }

        /// <summary>A Linux game as a .tar.gz with the given files; ".sh" files are executable.</summary>
        public static byte[] GameArchive(params (string Path, string Content)[] files)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
            {
                foreach (var (path, content) in files)
                {
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)) };
                    entry.Mode = path.EndsWith(".sh")
                        ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead
                        : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
                    tar.WriteEntry(entry);
                }
            }
            return output.ToArray();
        }

        /// <summary>
        /// A Linux game as a .tar.gz: its start script writes "started" next to itself, so a test can see that
        /// the game was really launched. <paramref name="padding"/> bytes of data make the download take a while.
        /// </summary>
        public static byte[] LinuxGameArchive(int padding = 0, string marker = "started")
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.NoCompression, leaveOpen: true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
            {
                byte[] script = System.Text.Encoding.UTF8.GetBytes($"#!/bin/sh\necho ok > \"$(dirname \"$0\")/{marker}\"\n");
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "start.sh")
                {
                    DataStream = new MemoryStream(script),
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                });
                // Random data: the gzip stream (no compression) keeps its size
                byte[] data = new byte[padding];
                Random.Shared.NextBytes(data);
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "data.bin") { DataStream = new MemoryStream(data) });
            }
            return output.ToArray();
        }
    }
}

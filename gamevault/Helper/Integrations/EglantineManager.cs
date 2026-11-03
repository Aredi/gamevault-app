using GameVault.Core;
using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using gamevault.Localization;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>Eglantine (PokeDinner's fork) for the Nintendo Switch 2 games: decrypted dumps, nothing to take from RomM.</summary>
    internal sealed class EglantineManager : SwitchEmulator
    {
        public override string Name => "Eglantine";
        public override string Core => "eglantine";
        public override string Console => "Switch 2";
        public override EglantineLayout Layout { get; } = new(Path.Combine(PlatformInfo.LocalDataDirectory, "eglantine"), PlatformInfo.IsWindows, PlatformInfo.IsMac);

        public override string FirmwareState => Loc.T("None needed: the Switch 2 games are decrypted dumps (NUP, NSP or folder)");

        protected override Task InstallFirmwareAsync(RetroPlatform platform, Action<string> status) => Task.CompletedTask;

        private bool updateOffered;

        /// <summary>
        /// A newer release than the installed one (each starts more games), offered once a session before a game: taken, it
        /// is installed over this one, the console's storage kept. No answer from the forge: the installed one is played.
        /// </summary>
        public async Task OfferUpdateAsync()
        {
            if (updateOffered || !Layout.IsInstalled)
                return;
            updateOffered = true;
            (string Tag, string Url)? newer;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                newer = Layout.NewerRelease(await HttpClients.Shared.GetStringAsync(EglantineLayout.ReleasesApi, timeout.Token));
            }
            catch (Exception ex) { Log.Ignored(ex); return; }
            if (newer is not { } release)
                return;
            var answer = await App.Instance.MainWindow.ShowMessageAsync(Loc.T("Eglantine update available"),
                Loc.F("A newer release of Eglantine ({0}) is available: each one starts more Switch 2 games. The saves and settings are kept. Update it now?", release.Tag),
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = Loc.T("Update"), NegativeButtonText = Loc.T("Later") });
            if (answer == MessageDialogResult.Affirmative)
                Layout.Update(release.Tag, release.Url);
        }
    }
}

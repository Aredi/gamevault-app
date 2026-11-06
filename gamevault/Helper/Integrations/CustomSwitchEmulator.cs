using GameVault.Core.Retro;
using gamevault.Helper.Platform;
using System;
using System.IO;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    /// <summary>
    /// A fork of Eden or Eglantine an administrator gave some games (see <see cref="GameEmulators"/>): installed at the first
    /// one in its own folder, it plays with the console's emulator's storage, players, settings and controller.
    /// </summary>
    internal sealed class CustomSwitchEmulator : SwitchEmulator
    {
        private readonly SwitchEmulator console;

        public CustomSwitchEmulator(GameEmulators.Emulator definition, SwitchEmulator console)
        {
            this.console = console;
            Name = definition.Name;
            Layout = new CustomSwitchLayout(Path.Combine(PlatformInfo.LocalDataDirectory, "emulators", definition.Id), console.Layout,
                definition.Windows, definition.Linux, definition.Mac, PlatformInfo.IsWindows, PlatformInfo.IsMac);
        }

        public override string Name { get; }
        public override string Core => console.Core;
        public override string Console => console.Console;
        public override CustomSwitchLayout Layout { get; }
        public override string FirmwareState => console.FirmwareState;

        /// <summary>The console's keys and firmware, where its emulator keeps them: in the shared "user" folder.</summary>
        protected internal override Task InstallFirmwareAsync(RetroPlatform platform, Action<string> status) => console.InstallFirmwareAsync(platform, status);

        internal override void PrepareLaunch() => console.PrepareLaunch();
    }
}

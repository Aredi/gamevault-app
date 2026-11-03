using GameVault.Core.Retro;
using gamevault.Localization;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace gamevault.Helper
{
    /// <summary>Eden or its fork Eglantine: each GameVault player is the console's user who plays, the controller chosen once in its window.</summary>
    internal abstract class SwitchEmulator : StandaloneEmulator
    {
        public abstract override SwitchLayout Layout { get; }

        public override string? ControllerHint => File.Exists(Layout.SettingsFiles.Single()) && SwitchLayout.HasController(File.ReadAllText(Layout.SettingsFiles.Single()))
            ? null
            : Loc.F("Controller: {0} plays with the keyboard until yours is chosen once in its window (Emulation > Configure > Controls > Input Device)", Name);

        /// <summary>The player becomes the console's first user, the one the game is started as.</summary>
        public override Process Launch(string player, string launchFile, string language)
        {
            byte[]? existing = File.Exists(Layout.ProfilesFile) ? File.ReadAllBytes(Layout.ProfilesFile) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(Layout.ProfilesFile)!);
            File.WriteAllBytes(Layout.ProfilesFile, SwitchLayout.Profiles(existing, player, LoginManager.Instance.GetCurrentUser()?.Username ?? "Player"));
            return base.Launch(player, launchFile, language);
        }
    }
}

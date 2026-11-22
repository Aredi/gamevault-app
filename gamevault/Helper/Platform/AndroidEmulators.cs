using GameVault.Core;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.Helper.Platform
{
    /// <summary>
    /// An Android emulator app that can play a console instead of the app's own player (as Argosy or ES-DE start them):
    /// its packages and activities, and how it is handed the game.
    /// </summary>
    /// <param name="Activities">"package/activity" (a leading dot: in its package), the first one installed is used.</param>
    /// <param name="GameExtra">The extra the game's address goes in; null: the intent's data.</param>
    internal sealed record AndroidEmulator(string Id, string Name, string[] Systems, string[] Activities, string? GameExtra,
        string? Action = null, string? Category = null, bool ClearTask = false, (string Key, bool Value)[]? Flags = null)
    {
        public IEnumerable<string> Packages => Activities.Select(a => a.Split('/')[0]).Distinct();
    }

    internal static class AndroidEmulators
    {
        /// <summary>The emulators known, for the consoles the app plays (the launch commands ES-DE documents).</summary>
        public static readonly AndroidEmulator[] All =
        {
            new("duckstation", "DuckStation", new[] { "psx" }, new[] { "com.github.stenzek.duckstation/.EmulationActivity" }, "bootPath",
                ClearTask: true, Flags: new[] { ("resumeState", false) }),
            new("nethersx2", "NetherSX2 / AetherSX2", new[] { "ps2" }, new[]
            {
                "xyz.aethersx2.android/.EmulationActivity",
                "xyz.aethersx2.tturnip/xyz.aethersx2.android.EmulationActivity",
                "xyz.aethersx2.cturnip/xyz.aethersx2.android.EmulationActivity",
            }, "bootPath", Action: "android.intent.action.MAIN", ClearTask: true),
            new("dolphin", "Dolphin", new[] { "ngc", "wii" }, new[] { "org.dolphinemu.dolphinemu/.ui.main.TvMainActivity" }, "AutoStartFile",
                Action: "android.intent.action.MAIN", Category: "android.intent.category.LEANBACK_LAUNCHER"),
            new("dolphin-mmjr", "Dolphin MMJR", new[] { "ngc", "wii" }, new[]
            {
                "org.mm.jr/org.dolphinemu.dolphinemu.ui.main.MainActivity",
                "org.dolphinemu.mmjr/org.dolphinemu.dolphinemu.ui.main.MainActivity",
            }, "AutoStartFile", Action: "android.intent.action.VIEW"),
            new("melonds", "melonDS", new[] { "nds" }, new[]
            {
                "me.magnum.melonds/.ui.emulator.EmulatorActivity",
                "me.magnum.melondualds/me.magnum.melonds.ui.emulator.EmulatorActivity",
            }, "uri", Action: "me.magnum.melonds.LAUNCH_ROM"),
            new("drastic", "DraStic", new[] { "nds" }, new[] { "com.dsemu.drastic/.DraSticActivity" }, null, ClearTask: true),
            new("pizzaboy-gba", "Pizza Boy GBA", new[] { "gba" }, new[]
            {
                "it.dbtecno.pizzaboygbapro/it.dbtecno.pizzaboygbapro.MainActivity",
                "it.dbtecno.pizzaboygba/it.dbtecno.pizzaboygba.MainActivity",
            }, "rom_uri", ClearTask: true),
            new("snes9x-explus", "Snes9x EX+", new[] { "snes" }, new[] { "com.explusalpha.Snes9xPlus/com.imagine.BaseActivity" }, null),
            new("azahar", "Azahar / Lime3DS", new[] { "3ds" }, new[]
            {
                "org.azahar_emu.azahar/org.citra.citra_emu.activities.EmulationActivity",
                "io.github.lime3ds.android/org.citra.citra_emu.activities.EmulationActivity",
                "io.github.lime3ds.android/.activities.EmulationActivity",
            }, null, ClearTask: true),
            new("citra", "Citra", new[] { "3ds" }, new[] { "org.citra.citra_emu/.activities.EmulationActivity" }, null, ClearTask: true),
        };

        /// <summary>The emulators of a console.</summary>
        public static IEnumerable<AndroidEmulator> Of(string system) => All.Where(e => e.Systems.Contains(system));

        /// <summary>The emulator chosen for each console ("psx=duckstation|ps2=nethersx2"); none: the app's own player.</summary>
        private static Dictionary<string, string> Chosen() =>
            Preferences.Get(AppConfigKey.AndroidEmulators, ProfileManager.ProfileConfigFile)
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
                .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Last()[1]);

        /// <summary>The emulator chosen for the console, when it is still installed.</summary>
        public static AndroidEmulator? ChosenFor(string system, IMobilePlatform mobile) =>
            Chosen().TryGetValue(system, out string? id) && All.FirstOrDefault(e => e.Id == id) is AndroidEmulator emulator
                && emulator.Systems.Contains(system) && mobile.IsInstalled(emulator) ? emulator : null;

        public static void Choose(string system, string? id)
        {
            var chosen = Chosen();
            if (id == null) chosen.Remove(system); else chosen[system] = id;
            Preferences.Set(AppConfigKey.AndroidEmulators, string.Join("|", chosen.Select(c => c.Key + "=" + c.Value)), ProfileManager.ProfileConfigFile);
        }
    }
}

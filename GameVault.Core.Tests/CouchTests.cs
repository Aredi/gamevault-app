using GameVault.Core.Couch;
using Xunit;

namespace GameVault.Core.Tests
{
    public class CouchTests
    {
        private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

        [Fact]
        public void AButton_IsOneCommand_AndADirectionHeld_GoesOn()
        {
            var pad = new CouchRepeater();
            Assert.Equal(new[] { CouchCommand.Accept }, pad.Next(CouchButtons.A, Ms(0)));
            // Held: nothing more
            Assert.Empty(pad.Next(CouchButtons.A, Ms(1000)));
            Assert.Empty(pad.Next(CouchButtons.None, Ms(1016)));

            Assert.Equal(new[] { CouchCommand.Right }, pad.Next(CouchButtons.Right, Ms(2000)));
            Assert.Empty(pad.Next(CouchButtons.Right, Ms(2300)));
            Assert.Equal(new[] { CouchCommand.Right }, pad.Next(CouchButtons.Right, Ms(2420)));
            Assert.Empty(pad.Next(CouchButtons.Right, Ms(2450)));
            Assert.Equal(new[] { CouchCommand.Right }, pad.Next(CouchButtons.Right, Ms(2515)));
            // Let go then pressed again: at once
            Assert.Empty(pad.Next(CouchButtons.None, Ms(2530)));
            Assert.Equal(new[] { CouchCommand.Right, CouchCommand.Back }, pad.Next(CouchButtons.Right | CouchButtons.B, Ms(2540)));
        }

        [Fact]
        public void WhatWasHeldWhileAGameRan_IsNotAPress_WhenTheMenuIsBack()
        {
            var pad = new CouchRepeater();
            pad.Reset(CouchButtons.A | CouchButtons.Down);
            Assert.Empty(pad.Next(CouchButtons.A | CouchButtons.Down, Ms(0)));
            Assert.Empty(pad.Next(CouchButtons.A | CouchButtons.Down, Ms(5000)));
            Assert.Equal(new[] { CouchCommand.Accept }, pad.Next(CouchButtons.None, Ms(5016)).Concat(pad.Next(CouchButtons.A, Ms(5032))));
        }

        private static CouchNavigator Navigator()
        {
            var navigator = new CouchNavigator { Columns = 3, ActionCount = 2, MenuCount = 3 };
            navigator.SetTabs(new[]
            {
                new[] { "1942", "Alan Wake", "Astro Bot", "Celeste", "Élan", "Hades", "Zelda" },
                new[] { "Mario" },
                Array.Empty<string>(),
            });
            return navigator;
        }

        [Fact]
        public void TheStrip_GoesLeftAndRight_AndEachTabKeepsItsGame()
        {
            CouchNavigator navigator = Navigator();
            Assert.Equal(0, navigator.Index);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Left));
            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.Right));
            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.Right));
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Down));
            Assert.Equal(2, navigator.Index);

            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.NextTab));
            Assert.Equal((1, 0), (navigator.Tab, navigator.Index));
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Right));
            navigator.Apply(CouchCommand.NextTab);
            // An empty tab: nothing to open
            Assert.Equal(-1, navigator.Index);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Accept));
            // Round: after the last tab, the first, with its game
            navigator.Apply(CouchCommand.NextTab);
            Assert.Equal((0, 2), (navigator.Tab, navigator.Index));
            navigator.Apply(CouchCommand.PreviousTab);
            Assert.Equal(2, navigator.Tab);
        }

        [Fact]
        public void TheList_ComesAfterTheGrid_AndGoesUpAndDown()
        {
            CouchNavigator navigator = Navigator();
            navigator.Apply(CouchCommand.View);
            navigator.Apply(CouchCommand.View);
            Assert.True(navigator.IsList);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Up));
            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.Down));
            Assert.Equal(1, navigator.Index);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Right));
            navigator.Apply(CouchCommand.View);
            Assert.Equal(CouchView.Strip, navigator.View);
        }

        [Fact]
        public void TheMenu_GoesBackToWhatItWasOpenedOver()
        {
            CouchNavigator navigator = Navigator();
            navigator.Apply(CouchCommand.Accept);
            navigator.Apply(CouchCommand.Menu);
            Assert.Equal(CouchScreen.Details, navigator.BeforeMenu);
            // Another menu opened from this one still goes back to the game
            navigator.OpenMenu();
            Assert.Equal(CouchEffect.ScreenChanged, navigator.Apply(CouchCommand.Back));
            Assert.Equal(CouchScreen.Details, navigator.Screen);
            navigator.Apply(CouchCommand.Back);
            navigator.Apply(CouchCommand.Menu);
            navigator.CloseMenu();
            Assert.Equal(CouchScreen.Home, navigator.Screen);
        }

        [Fact]
        public void TheGrid_GoesByRows_AndItsEndsLeadNowhere()
        {
            CouchNavigator navigator = Navigator();
            Assert.Equal(CouchEffect.ViewChanged, navigator.Apply(CouchCommand.View));
            Assert.True(navigator.IsGrid);
            navigator.Apply(CouchCommand.Right);
            navigator.Apply(CouchCommand.Right);
            // The end of the first row
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Right));
            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.Down));
            Assert.Equal(5, navigator.Index);
            // The last row has one game: under the third column, it is the one
            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.Down));
            Assert.Equal(6, navigator.Index);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Down));
            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.Up));
            Assert.Equal(3, navigator.Index);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Left));
        }

        [Fact]
        public void TheTriggers_JumpFromLetterToLetter()
        {
            CouchNavigator navigator = Navigator();
            Assert.Equal('#', CouchNavigator.Letter("1942"));
            Assert.Equal('E', CouchNavigator.Letter("Élan"));
            navigator.Apply(CouchCommand.NextLetter);
            Assert.Equal(1, navigator.Index);
            navigator.Apply(CouchCommand.NextLetter);
            Assert.Equal(3, navigator.Index);
            navigator.Select(0, 2);
            // In the middle of the A: its first game, then the letter before
            navigator.Apply(CouchCommand.PreviousLetter);
            Assert.Equal(1, navigator.Index);
            navigator.Apply(CouchCommand.PreviousLetter);
            Assert.Equal(0, navigator.Index);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.PreviousLetter));
            navigator.Select(0, 6);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.NextLetter));
        }

        [Fact]
        public void AGamesScreen_HasItsActions_AndTheMenuItsLines()
        {
            CouchNavigator navigator = Navigator();
            navigator.Apply(CouchCommand.Right);
            Assert.Equal(CouchEffect.ScreenChanged, navigator.Apply(CouchCommand.Accept));
            Assert.Equal(CouchScreen.Details, navigator.Screen);
            Assert.Equal(CouchEffect.Action, navigator.Apply(CouchCommand.Accept));
            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.Down));
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Down));
            Assert.Equal(1, navigator.Action);
            // The next game without leaving the screen
            Assert.Equal(CouchEffect.Moved, navigator.Apply(CouchCommand.NextTab));
            Assert.Equal((0, 2), (navigator.Tab, navigator.Index));

            Assert.Equal(CouchEffect.ScreenChanged, navigator.Apply(CouchCommand.Menu));
            Assert.Equal(CouchScreen.Menu, navigator.Screen);
            navigator.Apply(CouchCommand.Down);
            navigator.Apply(CouchCommand.Down);
            Assert.Equal(CouchEffect.None, navigator.Apply(CouchCommand.Down));
            Assert.Equal(2, navigator.MenuLine);
            Assert.Equal(CouchEffect.Action, navigator.Apply(CouchCommand.Accept));
            // Opened over the game's screen, the menu goes back to it
            Assert.Equal(CouchEffect.ScreenChanged, navigator.Apply(CouchCommand.Back));
            Assert.Equal(CouchScreen.Details, navigator.Screen);

            // The games change under the player: the tab's game is kept when it can, a game's screen left when its tab is empty
            navigator.SetTabs(new[] { Array.Empty<string>() });
            Assert.Equal(CouchScreen.Home, navigator.Screen);
            Assert.Equal(-1, navigator.Index);
        }
    }
}

namespace GameVault.Core.Tests
{
    public class CouchSoundTests
    {
        [Fact]
        public void EachSound_IsShortAndQuiet_AndStartsAndEndsWithoutAClick()
        {
            foreach (CouchSound sound in Enum.GetValues<CouchSound>())
            {
                short[] samples = CouchSounds.Samples(sound);
                // A tick for a move, about a second for a game that starts
                Assert.InRange(samples.Length, CouchSounds.Rate / 50, CouchSounds.Rate);
                Assert.InRange(samples.Max(s => Math.Abs((int)s)), short.MaxValue / 20, short.MaxValue / 4);
                Assert.InRange(Math.Abs((int)samples[0]), 0, 200);
                Assert.InRange(Math.Abs((int)samples[^1]), 0, 200);

                byte[] wave = CouchSounds.Wave(sound);
                Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
                Assert.Equal("WAVEfmt ", System.Text.Encoding.ASCII.GetString(wave, 8, 8));
                Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
                Assert.Equal(CouchSounds.Header + samples.Length * 2, wave.Length);
                Assert.Equal(samples.Length * 2, BitConverter.ToInt32(wave, 40));
            }
            // A sound by event, and the moves the shortest (they come many at a time)
            var all = Enum.GetValues<CouchSound>().Select(sound => CouchSounds.Samples(sound)).ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    Assert.NotEqual(all[i], all[j]);
            Assert.Equal(all.Min(a => a.Length), CouchSounds.Samples(CouchSound.Move).Length);
            // Made the same each time: the tests and the players hear the same thing
            Assert.Equal(CouchSounds.Samples(CouchSound.Tab), CouchSounds.Samples(CouchSound.Tab));
        }

        /// <summary>COUCH_SOUNDS_DIR: the sounds and the music written as WAV files, to listen to them.</summary>
        [Fact]
        public void TheSounds_CanBeWrittenToListenToThem()
        {
            if (Environment.GetEnvironmentVariable("COUCH_SOUNDS_DIR") is not { Length: > 0 } folder)
                return;
            Directory.CreateDirectory(folder);
            foreach (CouchSound sound in Enum.GetValues<CouchSound>())
                File.WriteAllBytes(Path.Combine(folder, $"{(int)sound:00}-{sound}.wav"), CouchSounds.Wave(sound));
            File.WriteAllBytes(Path.Combine(folder, "99-Music.wav"), CouchSounds.Wave(CouchMusic.Samples(), CouchMusic.Rate));
        }

        [Fact]
        public void TheMusic_LoopsWithoutASeam_AndStaysQuiet()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            short[] music = CouchMusic.Samples();
            // Made once when the mode opens, away from the screen: a few seconds at most
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString());
            Assert.Equal((int)(CouchMusic.Seconds * CouchMusic.Rate), music.Length);
            Assert.InRange(music.Max(s => Math.Abs((int)s)), short.MaxValue / 8, short.MaxValue / 5);
            // The end leads into the start as one sample into the next
            int jump = Math.Abs(music[^1] - music[0]);
            int usual = (int)Enumerable.Range(1, music.Length - 1).Average(i => Math.Abs(music[i] - music[i - 1]));
            Assert.InRange(jump, 0, Math.Max(40, usual * 6));
            // Never silent for long: a pad under everything
            for (int second = 0; second < (int)CouchMusic.Seconds; second++)
                Assert.True(music.Skip(second * CouchMusic.Rate).Take(CouchMusic.Rate).Max(s => Math.Abs((int)s)) > short.MaxValue / 60, $"second {second}");
        }
    }
}

namespace GameVault.Core.Tests
{
    public class CouchSettingsTests
    {
        [Fact]
        public void TheSettings_AreKept_AndAWrongFileGivesTheUsualOnes()
        {
            string file = Path.Combine(Path.GetTempPath(), "gv-couch-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.Equal(CouchSettings.Auto, CouchSettings.Load(file).Buttons);
                var settings = new CouchSettings { Buttons = CouchSettings.PlayStation, Accent = "#2F9BFF", ScreenSaverMinutes = 0, Sort = CouchSettings.ByTime, Genre = "RPG" };
                settings.Apps.Add(new CouchApp("Kodi", "C:\\Kodi\\kodi.exe", "-fs"));
                settings.Save(file);
                CouchSettings read = CouchSettings.Load(file);
                Assert.Equal((CouchSettings.PlayStation, "#2F9BFF", 0, CouchSettings.ByTime, "RPG"), (read.Buttons, read.Accent, read.ScreenSaverMinutes, read.Sort, read.Genre));
                Assert.Equal(new CouchApp("Kodi", "C:\\Kodi\\kodi.exe", "-fs"), Assert.Single(read.Apps));
                Assert.True(read.IsFiltered);

                File.WriteAllText(file, "{\"Buttons\":\"dreamcast\",\"Sort\":\"size\",\"ScreenSaverMinutes\":900,\"Apps\":null}");
                read = CouchSettings.Load(file);
                Assert.Equal((CouchSettings.Auto, CouchSettings.ByName, 60), (read.Buttons, read.Sort, read.ScreenSaverMinutes));
                // Kept before the buttons followed the gamepad, "xbox" was never chosen: it follows it now; chosen since, it stays
                File.WriteAllText(file, "{\"Buttons\":\"xbox\"}");
                read = CouchSettings.Load(file);
                Assert.Equal((CouchSettings.Auto, 2), (read.Buttons, read.Version));
                read.Buttons = CouchSettings.Xbox;
                read.Save(file);
                Assert.Equal(CouchSettings.Xbox, CouchSettings.Load(file).Buttons);
                Assert.Empty(read.Apps);
                // Chosen on a PC that had no settings yet (or after "back to the usual settings"): it stays too
                File.Delete(file);
                read = CouchSettings.Load(file);
                read.Buttons = CouchSettings.Xbox;
                read.Save(file);
                Assert.Equal(CouchSettings.Xbox, CouchSettings.Load(file).Buttons);
                var fresh = new CouchSettings().Reset();
                fresh.Buttons = CouchSettings.Xbox;
                fresh.Save(file);
                Assert.Equal(CouchSettings.Xbox, CouchSettings.Load(file).Buttons);
                File.WriteAllText(file, "not json");
                Assert.False(CouchSettings.Load(file).IsFiltered);
            }
            finally { File.Delete(file); }
            Assert.Equal("b", CouchSettings.Next(new[] { "a", "b" }, "a"));
            Assert.Equal("a", CouchSettings.Next(new[] { "a", "b" }, "b"));
            Assert.Equal("a", CouchSettings.Next(new[] { "a", "b" }, "unknown"));
        }

        [Fact]
        public void TheHints_NameTheButtonsOfThePlayersGamepad()
        {
            Assert.Equal("A", CouchGlyphs.Label(CouchSettings.Xbox, "A"));
            Assert.Equal("✕", CouchGlyphs.Label(CouchSettings.PlayStation, "A"));
            Assert.Equal("L1 R1", CouchGlyphs.Label(CouchSettings.PlayStation, "LB RB"));
            Assert.Equal("OPTIONS", CouchGlyphs.Label(CouchSettings.PlayStation, "START"));
            // The same place on a Switch gamepad has the other letter
            Assert.Equal("B", CouchGlyphs.Label(CouchSettings.Switch, "A"));
            Assert.Equal("ZL ZR", CouchGlyphs.Label(CouchSettings.Switch, "LT RT"));
            Assert.Equal("L3", CouchGlyphs.Label(CouchSettings.Switch, "L3"));
        }

        [Fact]
        public void TheButtonsNamed_FollowTheGamepadInThePlayersHands_UnlessChosen()
        {
            // SDL's types: PS3, PS4, PS5; the Switch Pro and Joy-Cons; the Xbox ones
            Assert.Equal(CouchSettings.PlayStation, CouchGlyphs.KindOf(4, "PS4 Controller"));
            Assert.Equal(CouchSettings.PlayStation, CouchGlyphs.KindOf(7, null));
            Assert.Equal(CouchSettings.Switch, CouchGlyphs.KindOf(5, null));
            Assert.Equal(CouchSettings.Xbox, CouchGlyphs.KindOf(2, "Xbox One Controller"));
            // An older SDL without types: the name says it
            Assert.Equal(CouchSettings.PlayStation, CouchGlyphs.KindOf(0, "Sony DualSense Wireless Controller"));
            Assert.Equal(CouchSettings.Switch, CouchGlyphs.KindOf(0, "Nintendo Switch Pro Controller"));
            Assert.Equal(CouchSettings.Xbox, CouchGlyphs.KindOf(0, "8BitDo SN30 Pro"));
            Assert.Equal(CouchSettings.Xbox, CouchGlyphs.KindOf(0, null));

            Assert.Equal(CouchSettings.PlayStation, CouchGlyphs.Resolve(CouchSettings.Auto, CouchSettings.PlayStation));
            Assert.Equal(CouchSettings.Xbox, CouchGlyphs.Resolve(CouchSettings.Auto, null));
            Assert.Equal(CouchSettings.Switch, CouchGlyphs.Resolve(CouchSettings.Switch, CouchSettings.PlayStation));
        }

        [Fact]
        public void TheGames_AreFilteredAndOrdered()
        {
            var games = new[]
            {
                new CouchEntry("Astral", true, 120, new DateTime(2026, 11, 1), new[] { "RPG" }),
                new CouchEntry("Cinder", false, 0, null, new[] { "Strategy", "RPG" }),
                new CouchEntry("Hollow", true, 900, new DateTime(2026, 10, 1), new[] { "Adventure" }),
                new CouchEntry("Iron", false, 30, new DateTime(2026, 11, 9), Array.Empty<string>()),
            };
            Assert.Equal(new[] { 0, 1, 2, 3 }, CouchFilter.Apply(games, new CouchSettings()));
            Assert.Equal(new[] { 0, 2 }, CouchFilter.Apply(games, new CouchSettings { InstalledOnly = true }));
            Assert.Equal(new[] { 1 }, CouchFilter.Apply(games, new CouchSettings { NeverPlayed = true }));
            Assert.Equal(new[] { 0, 1 }, CouchFilter.Apply(games, new CouchSettings { Genre = "rpg" }));
            Assert.Equal(new[] { 3, 0, 2, 1 }, CouchFilter.Apply(games, new CouchSettings { Sort = CouchSettings.ByRecent }));
            Assert.Equal(new[] { 2, 0 }, CouchFilter.Apply(games, new CouchSettings { Sort = CouchSettings.ByTime, InstalledOnly = true }));
            Assert.Equal(new[] { "Adventure", "RPG", "Strategy" }, CouchFilter.Genres(games));
        }

        [Fact]
        public void TheNewButtons_AreTheQuickAccessAndTheFilters()
        {
            var pad = new CouchRepeater();
            Assert.Equal(new[] { CouchCommand.Quick, CouchCommand.Filter }, pad.Next(CouchButtons.Back | CouchButtons.LeftStick, TimeSpan.Zero));
            // The right stick's click is the mouse's, not a command
            Assert.Empty(pad.Next(CouchButtons.RightStick, TimeSpan.FromSeconds(1)));
        }
    }
}

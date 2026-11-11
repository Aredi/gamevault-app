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
            Assert.Equal(CouchEffect.ScreenChanged, navigator.Apply(CouchCommand.Back));
            Assert.Equal(CouchScreen.Home, navigator.Screen);

            // The games change under the player: the tab's game is kept when it can, a game's screen left when its tab is empty
            navigator.Apply(CouchCommand.Accept);
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
                Assert.InRange(samples.Length, CouchSounds.Rate / 50, CouchSounds.Rate / 5);
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
            // Not the same sound for everything
            Assert.NotEqual(CouchSounds.Samples(CouchSound.Accept), CouchSounds.Samples(CouchSound.Back));
        }
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    public class LivingRoomScenarios
    {
        [AvaloniaFact]
        public async Task F11_OpensTheLivingRoom_InFullScreen_ArrowsMove_EscapeLeaves()
        {
            var session = await TestSession.GetAsync();
            session.Server.AddGame(970, "Living Room Game One", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(), "Living Room Game One (L_P).tar.gz");
            session.Server.AddGame(971, "Living Room Game Two", GameType.LINUX_PORTABLE, TestSession.LinuxGameArchive(), "Living Room Game Two (L_P).tar.gz");
            var before = session.Window.WindowState;
            var living = session.Window.FindControl<LivingRoomUserControl>("uiLivingRoom")!;

            session.Window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            await TestSession.WaitUntil(() => living.IsVisible && Model(living).Rows.Count > 0, TimeSpan.FromSeconds(10), "the living room rows");
            Assert.Equal(WindowState.FullScreen, session.Window.WindowState);

            var rows = Model(living).Rows;
            var allGames = rows.Last();
            Assert.Contains(allGames.Tiles, t => t.Game.ID == 970);
            // Down to the last row (the whole library), then right
            for (int i = 0; i < rows.Count; i++)
                living.Handle(GameVault.Core.Input.PadAction.Down);
            Assert.True(allGames.IsCurrent);
            Assert.True(allGames.Tiles[0].IsFocused);
            living.Handle(GameVault.Core.Input.PadAction.Right);
            Assert.True(allGames.Tiles[1].IsFocused);
            Assert.Equal(allGames.Tiles[1].Game.ID, Model(living).FocusedGame?.ID);

            session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(living.IsVisible);
            Assert.Equal(before == WindowState.FullScreen ? WindowState.Maximized : before, session.Window.WindowState);
        }

        private static LivingRoomViewModel Model(LivingRoomUserControl living) => (LivingRoomViewModel)living.DataContext!;
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using gamevault.Helper;
using gamevault.Models;
using gamevault.UserControls;
using gamevault.ViewModels;

namespace GameVault.UiTests
{
    /// <summary>LAN rooms: a game marked playable in LAN, a friend's room joined from its page, a room made for it, ZeroTier to set up.</summary>
    public class LanRoomScenarios
    {
        private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        private static Game NewGame(int id, string title) => new()
        {
            ID = id, Title = title, SortTitle = title.ToLowerInvariant(), Type = GameType.WINDOWS_PORTABLE, Path = $"/files/{title}.zip", Size = "1024", EntityVersion = 1,
            Metadata = new GameMetadata { Title = title }, Progresses = new List<Progress>(),
        };

        [Avalonia.Headless.XUnit.AvaloniaFact]
        public async Task AFriendsRoom_IsJoinedFromTheGamesPage_ThenARoomIsMadeForIt()
        {
            var session = await TestSession.GetAsync();
            var server = session.Server;
            session.Server.AddGame(NewGame(9801, "LAN Party Arena"), new byte[16]);
            server.LanGames[9801] = true;
            var friends = new FakeGameVaultServer.FakeRoom { Id = "c4576f0616000001", Code = "AB2CD3", Name = "Soirée Arena", OwnerId = 2, OwnerName = "Léa", GameId = 9801, GameTitle = "LAN Party Arena", Public = true };
            friends.Members.Add((2, "Léa", "bbbbbbbb02", "10.147.1.1"));
            server.Rooms[friends.Id] = friends;
            server.ZeroTierPeers["bbbbbbbb02"] = (18, true);
            SanctuaryService.Current = server.Url;
            ZeroTierAgent.ApiUrl = server.Url + "/zt";
            ZeroTierAgent.TestToken = "zt-test";
            try
            {
                // The game's page: its badge, its button, Léa's room, the administrator's box
                var page = new GameViewUserControl(new Game { ID = 9801 });
                MainWindowViewModel.Instance.SetActiveControl(page);
                await TestSession.WaitUntil(() => page.FindControl<Border>("uiLanRoom")!.IsVisible, TimeSpan.FromSeconds(30), "Léa's room on the game's page");
                Assert.True(page.FindControl<Border>("uiLanChip")!.IsVisible);
                Assert.True(page.FindControl<Button>("uiLanPlay")!.IsVisible);
                Assert.True(page.FindControl<CheckBox>("uiLanAdmin")!.IsChecked);
                Assert.Contains("Soirée Arena", page.FindControl<TextBlock>("uiLanRoomText")!.Text);

                // Joined: this PC in the room's network, then the room on its page with Léa reached straight
                Click(page.FindControl<Border>("uiLanRoom")!.GetLogicalDescendants().OfType<Button>().Single());
                var rooms = MainWindowViewModel.Instance.Rooms;
                await TestSession.WaitUntil(() => rooms.Selected?.Member == true && rooms.FindControl<ItemsControl>("uiMembers")!.ItemCount == 2, TimeSpan.FromSeconds(30), "the room joined");
                Assert.True(server.ZeroTierNetworks.ContainsKey(friends.Id));
                Assert.Contains(friends.Members, m => m.UserId == 1 && m.NodeId == "aaaaaaaa01");
                var members = rooms.FindControl<ItemsControl>("uiMembers")!.Items.Cast<RoomsUserControl.MemberRow>().ToList();
                Assert.Equal(gamevault.Localization.Loc.F("Direct · {0} ms", 18), members.Single(m => m.Name.StartsWith("Léa")).State);
                Assert.Equal(gamevault.Localization.Loc.T("This PC"), members.Single(m => m.Name.StartsWith("admin")).State);
                Assert.Contains("10.147.1.1", rooms.FindControl<SelectableTextBlock>("uiHostHint")!.Text);

                // Left: out of the network, Léa's room still there
                Click(rooms.FindControl<Button>("uiLeave")!);
                await TestSession.WaitUntil(() => !server.ZeroTierNetworks.ContainsKey(friends.Id) && rooms.Rooms.Any(r => r.Id == friends.Id && !r.Member), TimeSpan.FromSeconds(30), "the room left");

                // A room made for the game, from its button: the game already chosen, then shown with its code
                Click(page.FindControl<Button>("uiLanPlay")!);
                var popup = Assert.IsType<RoomJoinPopup>(MainWindowViewModel.Instance.Popup);
                await TestSession.WaitUntil(() => popup.FindControl<ComboBox>("uiGame")!.SelectedItem is Game { ID: 9801 }, TimeSpan.FromSeconds(30), "the game chosen for the room");
                Click(popup.FindControl<Button>("uiCreate")!);
                await TestSession.WaitUntil(() => MainWindowViewModel.Instance.Popup == null && rooms.Selected?.OwnerId == 1, TimeSpan.FromSeconds(30), "the room made");
                var made = server.Rooms.Values.Single(r => r.OwnerId == 1);
                Assert.Equal((9801, "LAN Party Arena"), (made.GameId, made.GameTitle));
                Assert.True(server.ZeroTierNetworks.ContainsKey(made.Id));
                Assert.Equal("K7Q-4MX", rooms.FindControl<TextBlock>("uiCode")!.Text);

                // The administrator unmarks it: no more LAN on its page
                var box = page.FindControl<CheckBox>("uiLanAdmin")!;
                box.IsChecked = false;
                box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await TestSession.WaitUntil(() => !page.FindControl<Border>("uiLanChip")!.IsVisible, TimeSpan.FromSeconds(30), "the badge gone");
                Assert.False(server.LanGames[9801]);

                // Without ZeroTier: its setup instead of the room
                ZeroTierAgent.TestToken = null;
                if (!ZeroTierAgent.IsInstalled)
                {
                    Click(rooms.FindControl<Button>("uiCreate")!);
                    Assert.IsType<ZeroTierSetupPopup>(MainWindowViewModel.Instance.Popup);
                }
            }
            finally
            {
                MainWindowViewModel.Instance.ClosePopup();
                SanctuaryService.Current = SanctuaryService.Url;
                ZeroTierAgent.ApiUrl = "http://127.0.0.1:9993";
                ZeroTierAgent.TestToken = null;
                server.Rooms.Clear();
                server.LanGames.Clear();
            }
        }
    }
}

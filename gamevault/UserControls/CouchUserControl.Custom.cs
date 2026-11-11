using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using GameVault.Core.Couch;
using gamevault.Localization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.UserControls
{
    /// <summary>
    /// The customization, as Aniki ReMake's: the look (color, font, corners, a game's screen), the lists (their numbers,
    /// the covers, the glow, the darkness), the backgrounds (the game's picture or one made here), the interface, the
    /// sounds and the start. Each setting shows at once behind its menu, docked at the left with what it does.
    /// </summary>
    public partial class CouchUserControl
    {
        /// <summary>The page of the settings shown (CouchMenu.Custom): "look", "library", "backgrounds"...</summary>
        internal string SettingsPage { get; private set; } = "look";

        private string shownPack = "";

        // --- Applying it ---

        /// <summary>Everything the customization changes, applied to the screen.</summary>
        internal void ApplyLook()
        {
            CouchSettings s = Settings;
            // The fonts: the titles' and the texts'
            const string fonts = "avares://gamevault/Resources/Fonts";
            Resources["Couch.Font"] = FontFamily.Parse(s.Font switch
            {
                "figtree" => $"{fonts}#Figtree, $Default",
                "system" => "$Default",
                _ => $"{fonts}#Sora, {fonts}#Figtree, $Default",
            });
            FontFamily = FontFamily.Parse(s.Font == "system" ? "$Default" : $"{fonts}#Figtree, $Default");

            // The corners
            (double inner, double glass) = s.Corners switch { "square" => (3.0, 6.0), "soft" => (26.0, 32.0), _ => (14.0, 22.0) };
            Resources["Couch.Radius"] = new CornerRadius(inner);
            Resources["Couch.Radius.Outer"] = new CornerRadius(inner + 3);
            Resources["Couch.Radius.Glass"] = new CornerRadius(glass);

            // The covers: their shape and size, the place each one takes, the strip's height
            (double width, double height) = s.CoverShape == "square" ? (216.0, 216.0) : (184.0, 252.0);
            double scale = s.CoverSize switch { "small" => 0.85, "large" => 1.15, _ => 1.0 };
            width = Math.Round(width * scale);
            height = Math.Round(height * scale);
            Resources["Couch.Card.Width"] = width;
            Resources["Couch.Card.Height"] = height;
            CardWidth = width + 24;
            CardHeight = height + 32;
            Resources["Couch.Slot.Width"] = CardWidth;
            Resources["Couch.Slot.Height"] = CardHeight;
            Resources["Couch.Strip.Height"] = CardHeight + 28;
            if (uiGridItems.Layout is Avalonia.Layout.UniformGridLayout grid)
            {
                grid.MinItemWidth = CardWidth;
                grid.MinItemHeight = CardHeight;
            }

            Classes.Set("noGlow", !s.Glow);
            uiShadeLeft.Opacity = uiShadeBottom.Opacity = s.Shade == "light" ? 0.55 : 1;
            uiShadeStrong.IsVisible = s.Shade == "strong";
            uiHints.IsVisible = s.Hints;
            uiClock.IsVisible = s.Clock;
            BuildPack();
            ApplyLayout();
            audio?.Configure(s.SoundPack, s.SoundVolume, s.MusicTheme, s.MusicVolume);
            Fit();
        }

        /// <summary>A game's screen: on its picture at the left, in a panel, or centered at the bottom.</summary>
        private void ApplyLayout()
        {
            string layout = Settings.Layout;
            bool centered = layout == "showcase";
            uiDetailCard.Classes.Set("couchGlass", layout == "panel");
            uiDetailCard.Padding = layout == "panel" ? new Thickness(38, 32) : default;
            uiDetailCard.Width = layout == "panel" ? 660 : double.NaN;
            Grid.SetColumnSpan(uiDetailCard, centered ? 2 : 1);
            uiDetailCard.HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            uiDetailCard.VerticalAlignment = centered ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            uiDetailCard.MaxWidth = centered ? 1100 : 680;
            uiDetailCard.Margin = centered ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 0, 40);
            HorizontalAlignment side = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            TextAlignment text = centered ? TextAlignment.Center : TextAlignment.Left;
            foreach (TextBlock block in new[] { uiDetailEyebrow, uiDetailTitle, uiDetailSub, uiDetailStatus, uiDescription })
            {
                block.TextAlignment = text;
                block.HorizontalAlignment = side;
            }
            uiDetailLogo.HorizontalAlignment = uiDetailStats.HorizontalAlignment = uiActions.HorizontalAlignment = side;
            uiActions.Padding = centered ? new Thickness(20, 0) : new Thickness(0, 0, 40, 0);
            // Centered, the trailer is above it, smaller
            Grid.SetColumn(uiTrailerBox, centered ? 0 : 1);
            Grid.SetColumnSpan(uiTrailerBox, centered ? 2 : 1);
            uiTrailerBox.HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Right;
            uiTrailerBox.Width = centered ? 720 : 880;
            uiTrailerBox.Height = centered ? 405 : 515;
            uiTrailerBox.Margin = centered ? new Thickness(0, 10, 0, 0) : new Thickness(36, 40, 0, 0);
            // Dark under the texts: at the left, or at the bottom
            uiDetailShade.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, centered ? 0 : 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(centered ? 0 : 1, centered ? 1 : 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(centered ? "#4D05070B" : "#F205070B"), 0),
                    new GradientStop(Color.Parse(centered ? "#6605070B" : "#C705070B"), centered ? 0.35 : 0.38),
                    new GradientStop(Color.Parse(centered ? "#E605070B" : "#4D05070B"), centered ? 0.7 : 0.62),
                    new GradientStop(Color.Parse(centered ? "#FA05070B" : "#1A05070B"), 1),
                },
            };
        }

        /// <summary>The game's picture behind the lists, or the background chosen; a game's screen keeps its own when asked.</summary>
        private void ShowBackground(bool detailsBehind)
        {
            string background = Settings.Background;
            bool game = background == "game" || (detailsBehind && Settings.DetailsBackground);
            uiBack.IsVisible = uiShots.IsVisible = game;
            uiPack.IsVisible = !game && background != "none";
        }

        /// <summary>The backgrounds made here (no picture to ship): an aurora, a starry night, a synthwave horizon, the color.</summary>
        private void BuildPack()
        {
            string pack = Settings.Background;
            if (pack == shownPack)
                return;
            shownPack = pack;
            uiPack.Children.Clear();
            Control? drawn = pack switch
            {
                "aurora" => Aurora(),
                "night" => Night(),
                "synthwave" => Synthwave(),
                "accent" => Accent(),
                _ => null,
            };
            if (drawn != null)
                uiPack.Children.Add(drawn);
        }

        private static Border Glow(string color, double x, double y, double radius, double opacity) => new()
        {
            Opacity = opacity,
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(x, y, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(radius * 1.4, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(color), 0), new GradientStop(Colors.Transparent, 1) },
            },
        };

        private static Panel Aurora() => new()
        {
            Background = new SolidColorBrush(Color.Parse("#070A12")),
            Children =
            {
                Glow("#1FB5A6", 0.25, 0.3, 0.55, 0.75),
                Glow("#7C4DFF", 0.75, 0.25, 0.5, 0.7),
                Glow("#2F6BFF", 0.55, 0.85, 0.6, 0.55),
            },
        };

        /// <summary>A canvas of the stage's size, scaled to the screen as the rest of the mode.</summary>
        private static Viewbox Scene(Canvas canvas, IBrush? background) => new()
        {
            Stretch = Stretch.UniformToFill,
            Child = new Panel { Width = 1920, Height = 1080, Background = background, Children = { canvas } },
        };

        private static Viewbox Night()
        {
            var canvas = new Canvas { Width = 1920, Height = 1080 };
            canvas.Children.Add(new Border { Width = 1920, Height = 1080, Child = Glow("#3A4FA0", 0.78, 0.18, 0.5, 0.6) });
            // The same stars every time
            var random = new Random(1111);
            for (int i = 0; i < 170; i++)
            {
                double size = random.NextDouble() < 0.1 ? 3 : random.NextDouble() < 0.4 ? 2 : 1.2;
                var star = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.25 + random.NextDouble() * 0.6 };
                Canvas.SetLeft(star, random.NextDouble() * 1920);
                Canvas.SetTop(star, random.NextDouble() * 760);
                canvas.Children.Add(star);
            }
            var sky = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#0B1430"), 0), new GradientStop(Color.Parse("#060912"), 0.7), new GradientStop(Color.Parse("#03050B"), 1) },
            };
            return Scene(canvas, sky);
        }

        private static Viewbox Synthwave()
        {
            const double horizon = 640, middle = 960;
            var canvas = new Canvas { Width = 1920, Height = 1080 };
            // The sun on the horizon, cut by bands (inside its circle only)
            var sun = new Canvas
            {
                Width = 520, Height = 520,
                Clip = new EllipseGeometry(new Rect(0, 0, 520, 520)),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#FFD36B"), 0), new GradientStop(Color.Parse("#FF5E8A"), 0.6), new GradientStop(Color.Parse("#C2307A"), 1) },
                },
                Opacity = 0.85,
            };
            for (int band = 0; band < 6; band++)
            {
                var cut = new Rectangle { Width = 520, Height = 4 + band * 3, Fill = new SolidColorBrush(Color.Parse("#2A0F45")) };
                Canvas.SetTop(cut, 230 + band * 26);
                sun.Children.Add(cut);
            }
            Canvas.SetLeft(sun, middle - 260);
            Canvas.SetTop(sun, horizon - 380);
            canvas.Children.Add(sun);
            // The floor: dark, its grid going to the horizon
            var floor = new Rectangle { Width = 1920, Height = 1080 - horizon, Fill = new SolidColorBrush(Color.Parse("#12061F")) };
            Canvas.SetTop(floor, horizon);
            canvas.Children.Add(floor);
            var grid = new SolidColorBrush(Color.Parse("#B03CC8"));
            for (int i = 1; i <= 14; i++)
            {
                double y = horizon + Math.Pow(i / 14.0, 2.2) * (1080 - horizon);
                canvas.Children.Add(new Line { StartPoint = new Point(0, y), EndPoint = new Point(1920, y), Stroke = grid, StrokeThickness = 1 + i / 7.0, Opacity = 0.35 + i / 30.0 });
            }
            for (int i = -14; i <= 14; i++)
                canvas.Children.Add(new Line { StartPoint = new Point(middle + i * 18, horizon), EndPoint = new Point(middle + i * 260, 1080), Stroke = grid, StrokeThickness = 1.4, Opacity = 0.5 });
            canvas.Children.Add(new Line { StartPoint = new Point(0, horizon), EndPoint = new Point(1920, horizon), Stroke = new SolidColorBrush(Color.Parse("#FF7AD9")), StrokeThickness = 2 });
            var sky = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, horizon / 1080, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#0A0418"), 0), new GradientStop(Color.Parse("#2A0F45"), 0.6), new GradientStop(Color.Parse("#7A1E6B"), 1) },
            };
            return Scene(canvas, sky);
        }

        /// <summary>The accent color itself (the selected game's cover when the color follows it), dark at the edges.</summary>
        private Panel Accent()
        {
            var color = new Border { Opacity = 0.55 };
            color.Bind(Border.BackgroundProperty, this.GetResourceObservable("Brush.Accent"));
            return new Panel
            {
                Background = new SolidColorBrush(Color.Parse("#05070B")),
                Children =
                {
                    color,
                    new Border
                    {
                        Background = new RadialGradientBrush
                        {
                            Center = new RelativePoint(0.6, 0.35, RelativeUnit.Relative),
                            GradientOrigin = new RelativePoint(0.6, 0.35, RelativeUnit.Relative),
                            RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
                            RadiusY = new RelativeScalar(1, RelativeUnit.Relative),
                            GradientStops = { new GradientStop(Color.Parse("#0005070B"), 0), new GradientStop(Color.Parse("#E605070B"), 1) },
                        },
                    },
                },
            };
        }

        // --- Its menus ---

        /// <summary>The pages of the settings, as Aniki's categories.</summary>
        private static readonly (string Page, string Name, string Help)[] Pages =
        {
            ("look", "Appearance", "The color, the font, the corners and how a game's screen is laid out."),
            ("library", "Library", "What the lists show of the selected game, and how the covers look."),
            ("backgrounds", "Backgrounds", "What is behind the lists and a game's screen, and how it moves."),
            ("details", "Game screen", "How a game's screen is laid out and what it shows."),
            ("interface", "Interface", "The bars and the names of the buttons."),
            ("sounds", "Sounds and music", "The sounds of the menus, their loudness, the music behind them."),
            ("start", "Start", "What the mode shows when it opens, and its screensaver."),
            ("system", "System", "The light mode, the menu over a game, the applications of the home."),
        };

        private List<CouchLine> SettingsPagesLines()
        {
            var lines = Pages.Select(p => new CouchLine
            {
                Text = Loc.T(p.Name),
                Help = Loc.T(p.Help),
                Run = () => { ShowSettingsPage(p.Page); return System.Threading.Tasks.Task.CompletedTask; },
            }).ToList();
            lines.Add(Line(Loc.T("Back"), () => ShowMenu(CouchMenu.Main)));
            return lines;
        }

        internal void ShowSettingsPage(string page)
        {
            SettingsPage = page;
            ShowMenu(CouchMenu.Custom);
        }

        private string PageName => Loc.T(Pages.FirstOrDefault(p => p.Page == SettingsPage).Name ?? "Settings");

        /// <summary>What each value is called on the screen.</summary>
        private static string ValueName(string value) => Loc.T(value switch
        {
            "signature" => "On its picture", "panel" => "In a panel", "showcase" => "Centered",
            "game" => "The selected game", "aurora" => "Aurora", "night" => "Night", "synthwave" => "Synthwave", "accent" => "The color", "none" => "Nothing",
            "sora" => "Sora", "figtree" => "Figtree", "system" => "The system's",
            "round" => "Rounded", "square" => "Square", "soft" => "Very rounded",
            "compact" => "Under its name",
            "normal" => "Normal", "light" => "Light", "strong" => "Strong",
            "portrait" => "Portrait", "small" => "Small", "large" => "Large",
            "retro" => "Retro", "crystal" => "Crystal",
            "calm" => "Calm",
            "home" => "Home", "library" => "Library", "installed" => "Installed games",
            _ => value,
        });

        /// <summary>A line that goes to the next value of a customization, applied at once.</summary>
        private CouchLine Choice(string label, string name, string help, bool games = false)
        {
            // "panel" is also the numbers' place at the screen's edge
            string value = Settings.Get(name);
            string shown = name == nameof(CouchSettings.InfoStyle) && value == "panel" ? Loc.T("At the screen's edge")
                : name == nameof(CouchSettings.SoundPack) && value == "soft" ? Loc.T("Soft") : ValueName(value);
            return Changed(Loc.F("{0}: {1}", Loc.T(label), shown), help, () => Settings.Set(name, CouchSettings.Next(CouchSettings.Choices[name], value)), games);
        }

        private CouchLine Toggle(string label, bool value, Action<bool> set, string help) =>
            Changed(Loc.F("{0}: {1}", Loc.T(label), YesNo(value)), help, () => set(!value));

        private CouchLine Volume(string label, int value, Action<int> set, string help) =>
            Changed(Loc.F("{0}: {1}", Loc.T(label), $"{value} %"), help, () => set(CouchSettings.Next(CouchSettings.Volumes, value)));

        /// <summary>A setting changed: kept, applied, its menu shown again on the same line.</summary>
        private CouchLine Changed(string text, string help, Action change, bool games = false) => new()
        {
            Text = text,
            Help = Loc.T(help),
            Run = () =>
            {
                int line = Navigator.MenuLine;
                change();
                SaveSettings();
                ApplyLook();
                if (games)
                    Reshow();
                BuildMenu();
                Navigator.SelectLine(line);
                ShowScreen(false);
                ShowHero(Current);
                return System.Threading.Tasks.Task.CompletedTask;
            },
        };

        private List<CouchLine> CustomLines()
        {
            CouchSettings s = Settings;
            var lines = SettingsPage switch
            {
                "look" => new List<CouchLine>
                {
                    Changed(Loc.F("Color: {0}", s.Accent.Length > 0 ? s.Accent : Loc.T("the theme's")), "The color of what is chosen. The selected game's cover gives it when the setting below is on.",
                        () => { s.Accent = CouchSettings.Next(CouchSettings.Accents, s.Accent); ShowAccent(shownBack); }),
                    Toggle("Color of the selected game", s.DynamicColors, v => { s.DynamicColors = v; ShowAccent(shownBack); }, "The color follows the cover of the game selected."),
                    Choice("Font", nameof(CouchSettings.Font), "The letters of the titles and of the texts."),
                    Choice("Corners", nameof(CouchSettings.Corners), "The corners of the covers, the buttons and the panels."),
                    Choice("Game screen", nameof(CouchSettings.Layout), "On its picture at the left, in a panel, or centered at the bottom under its trailer."),
                },
                "library" => new List<CouchLine>
                {
                    Toggle("Numbers of the selected game", s.ShowStats, v => s.ShowStats = v, "Its play time, its size, whether it is installed here."),
                    Choice("Where the numbers are", nameof(CouchSettings.InfoStyle), "At the screen's right edge, or on a line under the game's name."),
                    Toggle("Logo of the game instead of its name", s.Logos, v => s.Logos = v, "The game's title as drawn on its box, when the server has it."),
                    Choice("Cover shape", nameof(CouchSettings.CoverShape), "Tall as a box, or square."),
                    Choice("Cover size", nameof(CouchSettings.CoverSize), "Smaller covers show more games at once."),
                    Toggle("Glow of the chosen cover", s.Glow, v => s.Glow = v, "A soft light around what is chosen."),
                    Choice("Darkness under the texts", nameof(CouchSettings.Shade), "Stronger when bright pictures make the texts hard to read."),
                },
                "backgrounds" => new List<CouchLine>
                {
                    Choice("Behind the lists", nameof(CouchSettings.Background), "The selected game's picture, a background made by SanctuaryVault, the color, or nothing."),
                    Toggle("Game's picture in its screen", s.DetailsBackground, v => s.DetailsBackground = v, "A game's screen shows its own picture, whatever is behind the lists."),
                    Toggle("Moving background", s.Drift, v => s.Drift = v, "The picture behind drifts and zooms slowly."),
                    Toggle("Screenshots behind a game", s.Shots, v => s.Shots = v, "A game's screenshots come one after the other behind its screen."),
                    Toggle("Picture of a game that starts", s.Splash, v => s.Splash = v, "The game's picture, still, while it starts and runs."),
                },
                "details" => new List<CouchLine>
                {
                    Choice("Game screen", nameof(CouchSettings.Layout), "On its picture at the left, in a panel, or centered at the bottom under its trailer."),
                    Toggle("Trailer in a game's screen", s.Trailers, v => { s.Trailers = v; StopTrailer(); }, "The trailer plays after two seconds on a game's screen."),
                    Toggle("Animations", s.Animations, v => s.Animations = v, "A game's screen and the menus come in with a short move."),
                },
                "interface" => new List<CouchLine>
                {
                    Toggle("Help of the buttons", s.Hints, v => s.Hints = v, "What each button does, at the bottom of the screen."),
                    Toggle("Clock", s.Clock, v => s.Clock = v, "The time at the top right."),
                    ButtonsLine(),
                },
                "sounds" => new List<CouchLine>
                {
                    Changed(SoundsOn ? Loc.T("Sounds: on") : Loc.T("Sounds: off"), "The sounds of the menus.", () => SoundsOn = !SoundsOn),
                    Choice("Sounds", nameof(CouchSettings.SoundPack), "Soft mallets, an old console's square waves, or bells."),
                    Volume("Volume of the sounds", s.SoundVolume, v => s.SoundVolume = v, "Their loudness, under the TV's own volume."),
                    Toggle("Music in the menus", s.Music, v => s.Music = v, "Quiet music behind the menus, never over a game."),
                    Choice("Music", nameof(CouchSettings.MusicTheme), "Calm, or night: lower and darker."),
                    Volume("Volume of the music", s.MusicVolume, v => s.MusicVolume = v, "Its loudness, under the sounds."),
                },
                "start" => new List<CouchLine>
                {
                    Changed(StartsHere ? Loc.T("At start: the living-room mode") : Loc.T("At start: the desktop"), "SanctuaryVault opens in this mode (a PC plugged to the TV).", () => StartsHere = !StartsHere),
                    Toggle("Welcome", s.Welcome, v => s.Welcome = v, "The greeting when the mode opens with SanctuaryVault."),
                    Choice("Tab at start", nameof(CouchSettings.StartTab), "The tab shown when the mode opens."),
                    Changed(Loc.F("Screensaver: {0}", s.ScreenSaverMinutes == 0 ? Loc.T("never") : Loc.F("after {0} min", s.ScreenSaverMinutes)), "The games one after the other when nothing is touched.",
                        () => s.ScreenSaverMinutes = CouchSettings.Next(new[] { 5, 10, 20, 0 }, s.ScreenSaverMinutes)),
                },
                _ => SystemLines(),
            };
            lines.Add(Line(Loc.T("Back"), () => ShowMenu(CouchMenu.Settings)));
            return lines;
        }

        private CouchLine ButtonsLine()
        {
            static string Named(string style) => style switch { CouchSettings.PlayStation => "PlayStation", CouchSettings.Switch => "Switch", _ => "Xbox" };
            // Automatic: the gamepad in the player's hands, named
            string buttons = Settings.Buttons == CouchSettings.Auto ? Loc.F("as the gamepad used ({0})", Named(ButtonStyle)) : Named(Settings.Buttons);
            return Changed(Loc.F("Buttons shown: {0}", buttons), "The names of the buttons in the help: the gamepad's in your hands, or one chosen.",
                () => Settings.Buttons = CouchSettings.Next(new[] { CouchSettings.Auto, CouchSettings.Xbox, CouchSettings.PlayStation, CouchSettings.Switch }, Settings.Buttons));
        }

        private List<CouchLine> SystemLines()
        {
            CouchSettings s = Settings;
            var lines = new List<CouchLine>
            {
                Toggle("Light mode for a small PC", s.Performance, v => { s.Performance = v; ShowAccent(shownBack); }, "No moving background, no screenshots, no trailer, no animation."),
            };
            if (OperatingSystem.IsWindows())
                lines.Add(Toggle("Menu over a game (Start + Back)", s.Overlay, v => s.Overlay = v, "Start and Back together over a game show a menu to go back to it or quit it."));
            lines.Add(new CouchLine { Text = Loc.T("Add an application to the home..."), Help = Loc.T("A program started from the home: a video player, another launcher."), Run = AddAppAsync });
            foreach (CouchApp app in s.Apps.Take(4))
                lines.Add(Changed(Loc.F("Remove '{0}' from the home", app.Name), "", () => s.Apps.Remove(app), games: true));
            return lines;
        }

        /// <summary>The settings' pages are docked at the left, over a lighter shade: what changes shows beside them.</summary>
        private void PlaceMenu()
        {
            bool settings = menu is CouchMenu.Settings or CouchMenu.Custom;
            uiMenuBox.HorizontalAlignment = settings ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            uiMenuBox.Margin = settings ? new Thickness(72, 0, 0, 0) : default;
            uiMenuBox.Width = menu == CouchMenu.Achievements ? 900 : settings ? 680 : 560;
            uiMenu.Background = new SolidColorBrush(Color.Parse(settings ? "#3305070B" : "#CC05070B"));
            // Docked over the lists: nothing seen through it
            uiMenuBox.Background = settings ? new SolidColorBrush(Color.Parse("#F20B1018")) : null;
            if (!settings)
                uiMenuBox.ClearValue(Border.BackgroundProperty);
            uiMenuHelp.IsVisible = settings;
        }
    }
}

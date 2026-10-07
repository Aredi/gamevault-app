using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using gamevault.UserControls;
using gamevault.Localization;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace gamevault.Helper
{
    // Same shape as the MahApps dialog API the code base was written against.
    public enum MessageDialogResult
    {
        Canceled = -1,
        Negative = 0,
        Affirmative = 1,
        FirstAuxiliary = 2,
        SecondAuxiliary = 3,
    }

    public enum MessageDialogStyle
    {
        Affirmative,
        AffirmativeAndNegative,
        AffirmativeAndNegativeAndSingleAuxiliary,
        AffirmativeAndNegativeAndDoubleAuxiliary,
    }

    public class MetroDialogSettings
    {
        public string AffirmativeButtonText { get; set; } = "OK";
        public string NegativeButtonText { get; set; } = "Cancel";
        public string FirstAuxiliaryButtonText { get; set; } = "Cancel";
        public string SecondAuxiliaryButtonText { get; set; } = "Cancel";
        public string DefaultText { get; set; } = "";
        public bool AnimateHide { get; set; }
        public bool AnimateShow { get; set; }
        public double DialogMessageFontSize { get; set; } = double.NaN;
        public double DialogTitleFontSize { get; set; } = double.NaN;
    }

    /// <summary>
    /// Message / input dialogs rendered as an overlay inside the window (the "DialogLayer" panel of
    /// MainWindow and LoginWindow), or as a small modal window when no such layer is available.
    /// </summary>
    public static class DialogService
    {
        public static Task<MessageDialogResult> ShowMessageAsync(this Window? window, string title, string message,
            MessageDialogStyle style = MessageDialogStyle.Affirmative, MetroDialogSettings? settings = null)
        {
            return Run(window, title, message, style, settings ?? new MetroDialogSettings(), input: false)
                .ContinueWith(t => t.Result.Result, TaskScheduler.Default);
        }

        public static async Task<string?> ShowInputAsync(this Window? window, string title, string message, MetroDialogSettings? settings = null)
        {
            settings ??= new MetroDialogSettings();
            var result = await Run(window, title, message, MessageDialogStyle.AffirmativeAndNegative, settings, input: true);
            return result.Result == MessageDialogResult.Affirmative ? result.Input : null;
        }

        /// <summary>
        /// Replacement for System.Windows.MessageBox with a single OK button.
        /// </summary>
        public static Task ShowInfoAsync(string message, string title = "")
        {
            return ShowMessageAsync(App.Instance.MainWindow ?? App.Instance.ActiveWindow, title, message);
        }

        /// <summary>
        /// Replacement for a Yes/No System.Windows.MessageBox.
        /// </summary>
        public static async Task<bool> ConfirmAsync(string message, string title = "", string yes = "Yes", string no = "No")
        {
            var result = await ShowMessageAsync(App.Instance.MainWindow ?? App.Instance.ActiveWindow, title, message,
                MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings { AffirmativeButtonText = yes, NegativeButtonText = no });
            return result == MessageDialogResult.Affirmative;
        }

        private record DialogOutcome(MessageDialogResult Result, string? Input);

        private static async Task<DialogOutcome> Run(Window? window, string title, string message, MessageDialogStyle style, MetroDialogSettings settings, bool input)
        {
            // Texts of every dialog are translated here (formatted ones arrive translated through Loc.F)
            title = Loc.T(title ?? "");
            message = Loc.T(message ?? "");
            settings = new MetroDialogSettings
            {
                AffirmativeButtonText = Loc.T(settings.AffirmativeButtonText),
                NegativeButtonText = Loc.T(settings.NegativeButtonText),
                FirstAuxiliaryButtonText = Loc.T(settings.FirstAuxiliaryButtonText),
                SecondAuxiliaryButtonText = Loc.T(settings.SecondAuxiliaryButtonText),
                DefaultText = settings.DefaultText,
                AnimateHide = settings.AnimateHide,
                AnimateShow = settings.AnimateShow,
                DialogMessageFontSize = settings.DialogMessageFontSize,
                DialogTitleFontSize = settings.DialogTitleFontSize,
            };
            if (Dispatcher.UIThread.CheckAccess())
                return await ShowCore(window, title, message, style, settings, input);
            return await await Dispatcher.UIThread.InvokeAsync<Task<DialogOutcome>>(() => ShowCore(window, title, message, style, settings, input));
        }

        private static Task<DialogOutcome> ShowCore(Window? window, string title, string message, MessageDialogStyle style, MetroDialogSettings settings, bool input)
        {
            var tcs = new TaskCompletionSource<DialogOutcome>();

            TextBox? inputBox = input ? new TextBox { Text = settings.DefaultText, Margin = new Thickness(0, 15, 0, 0), MinWidth = 300 } : null;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 25, 0, 0) };

            var content = new StackPanel { MaxWidth = 900 };
            if (!string.IsNullOrEmpty(title))
            {
                content.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = double.IsNaN(settings.DialogTitleFontSize) ? 24 : settings.DialogTitleFontSize,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10),
                });
            }
            if (!string.IsNullOrEmpty(message))
            {
                content.Children.Add(new SelectableTextBlock
                {
                    Text = message,
                    FontSize = double.IsNaN(settings.DialogMessageFontSize) ? 15 : settings.DialogMessageFontSize,
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            if (inputBox != null)
                content.Children.Add(inputBox);
            content.Children.Add(buttons);

            var dialog = new Border
            {
                Background = Brush("Brush.Background2"),
                BorderBrush = Brush("Brush.Accent"),
                BorderThickness = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(40, 25),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Panel { Children = { new Border { Child = content, HorizontalAlignment = HorizontalAlignment.Center } } },
            };
            var overlay = new Panel
            {
                Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
                Children = { dialog },
                ZIndex = 1000,
            };

            Panel? layer = window?.IsVisible == true ? window.FindControl<Panel>("DialogLayer") : null;
            Window? standalone = null;

            void Close(MessageDialogResult result)
            {
                if (tcs.Task.IsCompleted)
                    return;
                if (layer != null)
                {
                    layer.Children.Remove(overlay);
                    layer.IsHitTestVisible = layer.Children.Count > 0;
                }
                standalone?.Close();
                tcs.TrySetResult(new DialogOutcome(result, inputBox?.Text));
            }

            void AddButton(string text, MessageDialogResult result, bool primary)
            {
                var button = new IconButton
                {
                    Text = text,
                    MinWidth = 100,
                    Height = 32,
                    Padding = new Thickness(12, 0),
                    FontSize = 15,
                    Kind = primary ? ButtonKind.Primary : ButtonKind.Skeleton,
                    BorderThickness = new Thickness(primary ? 0 : 1),
                };
                button.Click += (_, _) => Close(result);
                buttons.Children.Add(button);
            }

            AddButton(settings.AffirmativeButtonText, MessageDialogResult.Affirmative, true);
            if (style != MessageDialogStyle.Affirmative)
                AddButton(settings.NegativeButtonText, MessageDialogResult.Negative, false);
            if (style is MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary or MessageDialogStyle.AffirmativeAndNegativeAndDoubleAuxiliary)
                AddButton(settings.FirstAuxiliaryButtonText, MessageDialogResult.FirstAuxiliary, false);
            if (style == MessageDialogStyle.AffirmativeAndNegativeAndDoubleAuxiliary)
                AddButton(settings.SecondAuxiliaryButtonText, MessageDialogResult.SecondAuxiliary, false);

            overlay.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    Close(style == MessageDialogStyle.Affirmative ? MessageDialogResult.Affirmative : MessageDialogResult.Canceled);
                else if (e.Key == Key.Enter)
                    Close(MessageDialogResult.Affirmative);
            };

            if (layer != null)
            {
                layer.Children.Add(overlay);
                layer.IsHitTestVisible = true;
                Dispatcher.UIThread.Post(() => (inputBox as Control ?? buttons.Children.FirstOrDefault())?.Focus());
            }
            else
            {
                standalone = new Window
                {
                    Title = string.IsNullOrEmpty(title) ? "SanctuaryVault" : title,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    MaxWidth = 900,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new Border { Padding = new Thickness(25), Child = DetachContent(dialog) },
                };
                standalone.Closed += (_, _) => tcs.TrySetResult(new DialogOutcome(MessageDialogResult.Canceled, inputBox?.Text));
                standalone.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape) Close(MessageDialogResult.Canceled);
                };
                if (window != null && window.IsVisible)
                    standalone.ShowDialog(window);
                else
                    standalone.Show();
            }
            return tcs.Task;
        }

        private static Control DetachContent(Border dialog)
        {
            var child = dialog.Child!;
            dialog.Child = null;
            return child;
        }

        private static IBrush? Brush(string key)
        {
            return Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value) ? value as IBrush : null;
        }
    }
}

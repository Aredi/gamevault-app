using gamevault.Helper.Platform;
using Markdown.Avalonia;
using System;
using System.Windows.Input;

namespace gamevault.Helper
{
    /// <summary>
    /// Links in descriptions and news (written by editors, or brought by a metadata provider) open in the browser,
    /// never as a program or a file: the default command of the Markdown viewer hands any link to the shell.
    /// </summary>
    internal sealed class MarkdownLinks : ICommand
    {
        private static readonly MarkdownLinks instance = new();

        public static void KeepSafe(MarkdownScrollViewer viewer)
        {
            if (viewer.Engine is IMarkdownEngine engine)
                engine.HyperlinkCommand = instance;
            else
                GameVault.Core.Log.Info($"Markdown links not checked: unknown engine {viewer.Engine?.GetType().Name}");
        }

        /// <summary>The links of this viewer go through <see cref="PlatformInfo.OpenUrl"/>.</summary>
        public static bool IsSafe(MarkdownScrollViewer viewer) => viewer.Engine is IMarkdownEngine { HyperlinkCommand: MarkdownLinks };

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => PlatformInfo.OpenUrl(parameter?.ToString() ?? "");

        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}

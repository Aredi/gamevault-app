using Avalonia.Controls;
using Avalonia.VisualTree;
using GameVault.Core.Couch;
using gamevault.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace gamevault.UserControls
{
    internal sealed class CouchKey : ViewModelBase
    {
        private string text = "";
        public string Text { get => text; set { text = value; OnPropertyChanged(); } }
        private bool isSelected;
        public bool IsSelected { get => isSelected; set { if (isSelected != value) { isSelected = value; OnPropertyChanged(); } } }
    }

    /// <summary>
    /// The keyboard of the living-room mode, typed with a gamepad: the search's (letters and digits) and the one of the
    /// client's text boxes (both cases and a few signs).
    /// </summary>
    public partial class CouchKeyboard : UserControl
    {
        internal const string Erase = "⌫", Space = "␣", Done = "OK", Shift = "⇧";
        private const int Columns = 10;
        private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ", Digits = "0123456789", Signs = ".-_@";

        internal List<CouchKey> Keys { get; private set; } = new();
        /// <summary>A key was typed: a character, or <see cref="Erase"/>, <see cref="Space"/>, <see cref="Done"/>.</summary>
        internal event Action<string>? Typed;
        private int key;
        private bool upper = true;
        private bool cases;

        public CouchKeyboard()
        {
            InitializeComponent();
            // A mouse or a remote that points
            Tapped += (_, e) =>
            {
                if ((e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<CouchKey>().FirstOrDefault() is CouchKey pointed)
                {
                    Select(Keys.IndexOf(pointed));
                    Press(pointed.Text);
                    e.Handled = true;
                }
            };
        }

        /// <param name="text">Both cases and a few signs (a name, an address); else letters and digits (the search).</param>
        internal void Open(bool text)
        {
            cases = text;
            upper = !text;
            IEnumerable<string> keys = Letters.Select(c => (upper ? c : char.ToLowerInvariant(c)).ToString()).Concat(Digits.Select(c => c.ToString()));
            if (text)
                keys = keys.Concat(Signs.Select(c => c.ToString())).Append(Shift);
            Keys = keys.Concat(new[] { Space, Erase, Done }).Select(k => new CouchKey { Text = k }).ToList();
            uiKeys.ItemsSource = Keys;
            Select(0);
        }

        /// <summary>What is typed so far, or what to type while nothing is.</summary>
        internal void Show(string text, bool placeholder = false)
        {
            uiText.Text = text;
            uiText.Opacity = placeholder ? 0.5 : 1;
        }

        internal string Shown => uiText.Text ?? "";
        internal int Selected => key;

        private void Select(int to)
        {
            key = Math.Clamp(to, 0, Math.Max(0, Keys.Count - 1));
            for (int i = 0; i < Keys.Count; i++)
                Keys[i].IsSelected = i == key;
        }

        /// <summary>The cross moves over the keys, A types the one selected, X erases, Y is the space.</summary>
        internal void Handle(CouchCommand command)
        {
            switch (command)
            {
                case CouchCommand.Left when key % Columns > 0: Select(key - 1); break;
                case CouchCommand.Right when key % Columns < Columns - 1: Select(key + 1); break;
                case CouchCommand.Up when key >= Columns: Select(key - Columns); break;
                // The last row is shorter: under its end, its last key
                case CouchCommand.Down when key / Columns < (Keys.Count - 1) / Columns: Select(key + Columns); break;
                case CouchCommand.Accept: Press(Keys[key].Text); break;
                case CouchCommand.View: Typed?.Invoke(Erase); break;
                case CouchCommand.Search: Typed?.Invoke(Space); break;
            }
        }

        private void Press(string text)
        {
            if (text != Shift)
            {
                Typed?.Invoke(text);
                return;
            }
            upper = !upper;
            for (int i = 0; i < Letters.Length; i++)
                Keys[i].Text = (upper ? Letters[i] : char.ToLowerInvariant(Letters[i])).ToString();
        }
    }
}

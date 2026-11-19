using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace wSpot.Helpers
{
    /// <summary>
    /// Lets a TextBlock draw some of its letters in bold and in an accent colour, which
    /// is how a result row shows which letters of its name matched what was typed.
    ///
    /// A TextBlock's Text can't carry formatting, and its Inlines can't be bound, so this
    /// attached-property helper does the binding and rebuilds the Inlines itself:
    ///
    ///   &lt;TextBlock helpers:HighlightText.Text="{Binding Name}"
    ///              helpers:HighlightText.Indices="{Binding MatchedIndices}" /&gt;
    /// </summary>
    public static class HighlightText
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text", typeof(string), typeof(HighlightText),
                new PropertyMetadata(string.Empty, OnInputChanged));

        public static readonly DependencyProperty IndicesProperty =
            DependencyProperty.RegisterAttached(
                "Indices", typeof(IEnumerable<int>), typeof(HighlightText),
                new PropertyMetadata(null, OnInputChanged));

        public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
        public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

        public static IEnumerable<int>? GetIndices(DependencyObject element) => (IEnumerable<int>?)element.GetValue(IndicesProperty);
        public static void SetIndices(DependencyObject element, IEnumerable<int>? value) => element.SetValue(IndicesProperty, value);

        // Created once and frozen, so every row shares it and it can't be changed by accident.
        private static readonly Brush AccentBrush = CreateAccentBrush();

        private static Brush CreateAccentBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC6, 0x6D));
            brush.Freeze();
            return brush;
        }

        private static void OnInputChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            if (element is TextBlock block)
                Rebuild(block);
        }

        private static void Rebuild(TextBlock block)
        {
            string text = GetText(block) ?? string.Empty;
            IEnumerable<int>? indices = GetIndices(block);

            block.Inlines.Clear();

            if (indices is null || !indices.Any() || text.Length == 0)
            {
                block.Inlines.Add(new Run(text));
                return;
            }

            // Out-of-range positions are simply never looked up, so a stale index can't crash a row.
            var matched = new HashSet<int>(indices);

            // Walk the text once, grouping neighbouring letters of the same kind into one Run.
            var buffer = new StringBuilder();
            bool bufferIsMatch = false;

            for (int i = 0; i < text.Length; i++)
            {
                bool isMatch = matched.Contains(i);

                if (buffer.Length > 0 && isMatch != bufferIsMatch)
                {
                    block.Inlines.Add(CreateRun(buffer.ToString(), bufferIsMatch));
                    buffer.Clear();
                }

                bufferIsMatch = isMatch;
                buffer.Append(text[i]);
            }

            if (buffer.Length > 0)
                block.Inlines.Add(CreateRun(buffer.ToString(), bufferIsMatch));
        }

        private static Run CreateRun(string text, bool highlighted)
        {
            var run = new Run(text);

            if (highlighted)
            {
                run.FontWeight = FontWeights.Bold;
                run.Foreground = AccentBrush;
            }

            return run;
        }
    }
}

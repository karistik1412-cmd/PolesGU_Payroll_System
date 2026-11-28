using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;   // Typeface, FormattedText

namespace PayrollClient.Services
{
    // Подбор ширины кнопок по фактическому тексту.
    // Нужен для длинных подписей и для английского/белорусского интерфейса,
    // чтобы кнопка никогда не обрезала текст многоточием.
    public static class ButtonMetrics
    {
        private const double HorizontalPadding = 34; // внутренние отступы + рамка
        private const double MinWidth = 88;

        public static double Measure(Button button, double extra = HorizontalPadding)
        {
            if (button == null) return MinWidth;

            string text = button.Content == null ? string.Empty : button.Content.ToString();
            Typeface typeface = new Typeface(
                button.FontFamily ?? new System.Windows.Media.FontFamily("Segoe UI"),
                button.FontStyle,
                button.FontWeight,
                button.FontStretch);

            FormattedText formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                button.FontSize <= 0 ? 12 : button.FontSize,
                System.Windows.Media.Brushes.Black);

            return Math.Max(MinWidth, Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + extra));
        }

        // Выравнивает кнопки по фактической ширине подписи
        public static void AutoSize(IEnumerable<Button> buttons)
        {
            if (buttons == null) return;
            foreach (Button button in buttons.Where(b => b != null))
                button.MinWidth = Measure(button);
        }
    }
}

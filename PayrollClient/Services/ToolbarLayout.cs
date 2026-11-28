using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PayrollClient.Services
{
    // Раскладка кнопок в карточке-панели по фактической ширине текста.
    // Нужна, чтобы подписи вроде «Импорт табеля» или «Листок в Excel»
    // никогда не обрезались, в том числе после смены языка интерфейса.
    public static class ToolbarLayout
    {
        public const int DefaultPadding = 13;
        public const int MinGap = 8;
        private const int Edge = 10;

        // Ширина кнопки, при которой подпись гарантированно помещается целиком
        public static int MeasureButtonWidth(Control like, string text, int padding = DefaultPadding)
        {
            if (like == null) return 120;
            return TextRenderer.MeasureText(text ?? string.Empty, like.Font).Width + (padding * 2);
        }

        // Правый край блока выбора периода (месяц/год) — от него начинается ряд кнопок.
        public static int RightOf(params Control[] controls)
        {
            int right = 0;
            if (controls == null) return right;
            foreach (Control c in controls)
                if (c != null) right = Math.Max(right, c.Right);
            return right;
        }

        // Выравнивает кнопки в один ряд: слева направо, с одинаковым зазором.
        // rightAligned = true — прижимает ряд к правому краю (так удобнее для панели расчёта).
        public static void Arrange(Control host, IEnumerable<Control> controls, int y,
                                   int preferredGap = 14, int minGap = MinGap,
                                   bool rightAligned = false, int edge = Edge,
                                   int startX = -1)
        {
            if (host == null) return;

            var list = new List<Control>();
            foreach (Control c in controls)
                if (c != null) list.Add(c);
            if (list.Count == 0) return;

            int gap = preferredGap;
            // startX >= 0 — ряд начинается не от края панели, а от указанной позиции
            // (так кнопки встают ровно после выбора периода и не наезжают на него).
            int leftEdge = startX >= 0 ? startX : edge;
            int available = host.ClientSize.Width - leftEdge - edge;
            int needed = 0;

            foreach (Control c in list)
                needed += Math.Max(c.Width, MeasureButtonWidth(c, c.Text));

            // Если ряд не помещается — сначала ужимаем зазоры, затем слегка поджимаем отступы
            if (needed + (gap * (list.Count - 1)) > available)
            {
                int extra = needed + (gap * (list.Count - 1)) - available;
                int gapCut = Math.Min(gap - minGap, extra / Math.Max(1, list.Count - 1));
                gap -= Math.Max(0, gapCut);
            }

            // Финальная ширина каждой кнопки: не меньше, чем нужно для текста
            int total = gap * (list.Count - 1);
            var widths = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                widths[i] = Math.Max(list[i].Width, MeasureButtonWidth(list[i], list[i].Text));
                total += widths[i];
            }

            // Если всё равно не помещается, сжимаем отступы по 2 px, но не уже текста
            int over = total - available;
            while (over > 0)
            {
                bool changed = false;
                for (int i = 0; i < list.Count && over > 0; i++)
                {
                    int minWidth = TextRenderer.MeasureText(list[i].Text ?? string.Empty, list[i].Font).Width + 12;
                    if (widths[i] > minWidth)
                    {
                        int cut = Math.Min(over, Math.Max(2, (widths[i] - minWidth) / 2));
                        widths[i] -= cut;
                        total -= cut;
                        over -= cut;
                        changed = true;
                    }
                }
                if (!changed) break;
            }

            int x = rightAligned ? Math.Max(leftEdge, host.ClientSize.Width - edge - total) : leftEdge;
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Location = new Point(x, y);
                list[i].Size = new Size(widths[i], list[i].Height);
                x += widths[i] + gap;
            }
        }
    }
}

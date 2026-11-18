using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace PayrollClient.Services
{
    // Единая дизайн-система приложения в фирменных цветах ПолесГУ (зелёный и белый)
    public static class UiTheme
    {
        // --- Фирменная палитра ПолесГУ ---
        public static readonly Color SidebarBg = Color.FromArgb(18, 78, 43);          // Глубокий университетский зелёный
        public static readonly Color SidebarHeaderBg = Color.FromArgb(13, 61, 33);    // Тёмно-хвойный для шапки меню
        public static readonly Color SidebarDivider = Color.FromArgb(38, 108, 66);    // Тонкий разделитель в меню
        public static readonly Color SidebarHover = Color.FromArgb(26, 99, 56);       // Наведение пункта меню
        public static readonly Color SidebarActive = Color.FromArgb(33, 120, 69);     // Активный пункт меню
        public static readonly Color SidebarTextMuted = Color.FromArgb(174, 217, 190);// Светло-мятный подзаголовок

        public static readonly Color PrimaryGreen = Color.FromArgb(24, 119, 64);      // Основной зелёный ПолесГУ
        public static readonly Color PrimaryGreenHover = Color.FromArgb(18, 96, 51);  // Тёмно-зелёный при наведении
        public static readonly Color AccentGreen = Color.FromArgb(45, 156, 91);       // Изумрудный акцент
        public static readonly Color AccentGreenHover = Color.FromArgb(34, 132, 75);  // Изумрудный при наведении
        public static readonly Color MintLight = Color.FromArgb(156, 214, 179);       // Мягкий мятно-зелёный (для графиков)
        public static readonly Color MintPale = Color.FromArgb(232, 246, 237);        // Очень светлый мятный фон/hover
        public static readonly Color SelectionBg = Color.FromArgb(214, 240, 224);     // Выделение строки в таблице
        public static readonly Color SelectionFg = Color.FromArgb(15, 51, 30);        // Текст выделенной строки
        public static readonly Color EditableCellBg = Color.FromArgb(242, 250, 245);  // Редактируемые ячейки табеля
        public static readonly Color AltRowBg = Color.FromArgb(248, 252, 249);        // Чередование строк таблицы

        public static readonly Color PageBg = Color.FromArgb(242, 247, 244);          // Светлый фон рабочей области
        public static readonly Color CardBg = Color.White;                            // Чисто белые карточки и панели
        public static readonly Color BorderColor = Color.FromArgb(212, 229, 218);     // Аккуратная рамка карточек
        public static readonly Color GridLineColor = Color.FromArgb(228, 239, 232);   // Линии таблицы и сетки графиков

        public static readonly Color TextPrimary = Color.FromArgb(22, 46, 32);        // Основной тёмный текст
        public static readonly Color TextSecondary = Color.FromArgb(94, 117, 103);    // Вторичный приглушённый текст

        public static readonly Color DangerText = Color.FromArgb(184, 50, 44);        // Аккуратный красный для удаления
        public static readonly Color DangerBorder = Color.FromArgb(225, 175, 172);
        public static readonly Color DangerHover = Color.FromArgb(253, 240, 239);

        public static readonly Color DisabledBg = Color.FromArgb(236, 242, 238);
        public static readonly Color DisabledBorder = Color.FromArgb(214, 224, 218);
        public static readonly Color DisabledFg = Color.FromArgb(148, 165, 154);

        // Включение двойной буферизации для плавной отрисовки без мерцания
        public static void EnableDoubleBuffering(Control control)
        {
            if (control == null || SystemInformation.TerminalServerSession) return;
            try
            {
                PropertyInfo prop = control.GetType().GetProperty(
                    "DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop != null) prop.SetValue(control, true, null);
            }
            catch
            {
                // Игнорируем, если свойство недоступно
            }
        }

        // Белая карточка с тонкой рамкой и опциональной зелёной акцентной полосой
        public static Panel CreateCardPanel(bool leftAccent = false, bool topAccent = false)
        {
            Panel panel = new Panel
            {
                BackColor = CardBg
            };
            EnableDoubleBuffering(panel);

            panel.Paint += (s, e) =>
            {
                Rectangle rect = panel.ClientRectangle;
                if (rect.Width <= 1 || rect.Height <= 1) return;

                using (Pen borderPen = new Pen(BorderColor, 1))
                {
                    e.Graphics.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);
                }

                if (leftAccent)
                {
                    using (SolidBrush accentBrush = new SolidBrush(PrimaryGreen))
                    {
                        e.Graphics.FillRectangle(accentBrush, 0, 0, 4, rect.Height);
                    }
                }

                if (topAccent)
                {
                    using (SolidBrush accentBrush = new SolidBrush(PrimaryGreen))
                    {
                        e.Graphics.FillRectangle(accentBrush, 0, 0, rect.Width, 3);
                    }
                }
            };

            panel.Resize += (s, e) => panel.Invalidate();
            return panel;
        }

        // Верхняя белая шапка раздела
        public static Panel CreatePageHeader(Control parent, string title, string subtitle)
        {
            Panel header = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(parent.ClientSize.Width, 62),
                BackColor = CardBg,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            EnableDoubleBuffering(header);

            header.Paint += (s, e) =>
            {
                using (Pen p = new Pen(BorderColor, 1))
                {
                    e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
                }
            };
            header.Resize += (s, e) => header.Invalidate();

            Label lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(22, string.IsNullOrEmpty(subtitle) ? 16 : 9),
                AutoSize = true
            };
            header.Controls.Add(lblTitle);

            if (!string.IsNullOrEmpty(subtitle))
            {
                Label lblSub = new Label
                {
                    Text = subtitle,
                    Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                    ForeColor = TextSecondary,
                    Location = new Point(24, 37),
                    AutoSize = true
                };
                header.Controls.Add(lblSub);
            }

            parent.Controls.Add(header);
            return header;
        }

        // Нижняя информационная панель со сводными показателями
        public static Panel CreateSummaryFooter(Control parent, int margin, out Label lblSummary)
        {
            int footerHeight = 42;
            Panel footer = CreateCardPanel(leftAccent: true, topAccent: false);
            footer.Location = new Point(margin, parent.ClientSize.Height - footerHeight - 14);
            footer.Size = new Size(parent.ClientSize.Width - (margin * 2), footerHeight);
            footer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            lblSummary = new Label
            {
                Text = string.Empty,
                Location = new Point(16, 11),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TextPrimary
            };
            footer.Controls.Add(lblSummary);

            parent.Controls.Add(footer);
            return footer;
        }

        // Основная зелёная кнопка (главное действие)
        public static Button CreatePrimaryButton(string text, int x, int y, int width, int height = 36)
        {
            return CreateStyledButton(
                text, x, y, width, height,
                PrimaryGreen, PrimaryGreenHover, Color.White, PrimaryGreen, 0);
        }

        // Вторичная изумрудная кнопка
        public static Button CreateAccentButton(string text, int x, int y, int width, int height = 36)
        {
            return CreateStyledButton(
                text, x, y, width, height,
                AccentGreen, AccentGreenHover, Color.White, AccentGreen, 0);
        }

        // Белая контурная кнопка с зелёной обводкой ПолесГУ
        public static Button CreateOutlineButton(string text, int x, int y, int width, int height = 36)
        {
            return CreateStyledButton(
                text, x, y, width, height,
                Color.White, MintPale, PrimaryGreen, PrimaryGreen, 1);
        }

        // Аккуратная белая контурная кнопка для опасных действий (удаление / сброс)
        public static Button CreateDangerButton(string text, int x, int y, int width, int height = 36)
        {
            return CreateStyledButton(
                text, x, y, width, height,
                Color.White, DangerHover, DangerText, DangerBorder, 1);
        }

        private static Button CreateStyledButton(
            string text,
            int x,
            int y,
            int width,
            int height,
            Color normalBg,
            Color hoverBg,
            Color normalFg,
            Color borderColor,
            int borderSize)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                BackColor = normalBg,
                ForeColor = normalFg,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false
            };

            btn.FlatAppearance.BorderSize = borderSize;
            btn.FlatAppearance.BorderColor = borderColor;
            btn.FlatAppearance.MouseOverBackColor = hoverBg;
            btn.FlatAppearance.MouseDownBackColor = hoverBg;

            btn.MouseEnter += (s, e) =>
            {
                if (btn.Enabled) btn.BackColor = hoverBg;
            };
            btn.MouseLeave += (s, e) =>
            {
                if (btn.Enabled) btn.BackColor = normalBg;
            };
            btn.EnabledChanged += (s, e) =>
            {
                if (btn.Enabled)
                {
                    btn.BackColor = normalBg;
                    btn.ForeColor = normalFg;
                    btn.FlatAppearance.BorderColor = borderColor;
                    btn.FlatAppearance.BorderSize = borderSize;
                }
                else
                {
                    btn.BackColor = DisabledBg;
                    btn.ForeColor = DisabledFg;
                    btn.FlatAppearance.BorderColor = DisabledBorder;
                    btn.FlatAppearance.BorderSize = 1;
                }
            };

            return btn;
        }

        // Единый профессиональный стиль таблицы DataGridView
        public static DataGridView CreateStyledGrid(Control parent, int x, int y, int width, int height, bool readOnly = true)
        {
            Panel container = CreateCardPanel(leftAccent: false, topAccent: false);
            container.Location = new Point(x, y);
            container.Size = new Size(width, height);
            container.Padding = new Padding(1);
            container.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            parent.Controls.Add(container);

            DataGridView dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = CardBg,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                GridColor = GridLineColor,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = readOnly,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 40
            };

            dgv.RowTemplate.Height = 36;

            // Шапка таблицы в зелёном цвете ПолесГУ
            dgv.ColumnHeadersDefaultCellStyle.BackColor = PrimaryGreen;
            dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = PrimaryGreen;
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            dgv.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);

            // Строки таблицы — белый фон с мягким мятным чередованием и нежно-зелёным выделением
            dgv.DefaultCellStyle.BackColor = CardBg;
            dgv.DefaultCellStyle.ForeColor = TextPrimary;
            dgv.DefaultCellStyle.SelectionBackColor = SelectionBg;
            dgv.DefaultCellStyle.SelectionForeColor = SelectionFg;
            dgv.DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            dgv.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);

            dgv.AlternatingRowsDefaultCellStyle.BackColor = AltRowBg;
            dgv.AlternatingRowsDefaultCellStyle.SelectionBackColor = SelectionBg;
            dgv.AlternatingRowsDefaultCellStyle.SelectionForeColor = SelectionFg;

            EnableDoubleBuffering(dgv);
            container.Controls.Add(dgv);
            return dgv;
        }

        // Белая карточка с заголовком и настроенным компонентом Chart
        public static Chart CreateChartCard(Control parent, string title, out Label lblSubtitle)
        {
            Panel card = CreateCardPanel(leftAccent: false, topAccent: true);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(6);
            card.Padding = new Padding(12, 9, 12, 8);

            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = CardBg
            };

            Label lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(2, 2),
                AutoSize = true
            };
            headerPanel.Controls.Add(lblTitle);

            Label sub = new Label
            {
                Text = string.Empty,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = TextSecondary,
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = 140,
                TextAlign = ContentAlignment.MiddleRight
            };
            headerPanel.Controls.Add(sub);
            lblSubtitle = sub;

            Chart chart = new Chart
            {
                Dock = DockStyle.Fill,
                BackColor = CardBg,
                BorderlineWidth = 0,
                AntiAliasing = AntiAliasingStyles.All,
                TextAntiAliasingQuality = TextAntiAliasingQuality.High
            };

            ChartArea area = new ChartArea("Main")
            {
                BackColor = CardBg,
                BorderWidth = 0
            };

            area.AxisX.LineColor = BorderColor;
            area.AxisY.LineColor = BorderColor;
            area.AxisX.MajorGrid.Enabled = false;
            area.AxisY.MajorGrid.LineColor = GridLineColor;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisX.MajorTickMark.Enabled = false;
            area.AxisY.MajorTickMark.Enabled = false;
            area.AxisX.LabelStyle.Font = new Font("Segoe UI", 8.25f);
            area.AxisX.LabelStyle.ForeColor = TextSecondary;
            area.AxisY.LabelStyle.Font = new Font("Segoe UI", 8.25f);
            area.AxisY.LabelStyle.ForeColor = TextSecondary;
            chart.ChartAreas.Add(area);

            Legend legend = new Legend("MainLegend")
            {
                Docking = Docking.Bottom,
                Alignment = StringAlignment.Center,
                BackColor = CardBg,
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = TextPrimary
            };
            chart.Legends.Add(legend);

            // Важен порядок добавления Dock: Fill первым, Top вторым (или BringToFront для Chart)
            card.Controls.Add(chart);
            card.Controls.Add(headerPanel);
            chart.BringToFront();

            parent.Controls.Add(card);
            return chart;
        }

        // Стильное поле ввода с аккуратной рамкой, подсвечивающейся зелёным при фокусе
        public static Panel WrapTextBox(TextBox textBox, int x, int y, int width, int height = 34)
        {
            bool focused = false;
            Panel wrapper = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.White,
                Padding = new Padding(8, 6, 8, 4)
            };

            textBox.BorderStyle = BorderStyle.None;
            textBox.BackColor = Color.White;
            textBox.ForeColor = TextPrimary;
            textBox.Dock = DockStyle.Fill;

            wrapper.Paint += (s, e) =>
            {
                Color c = focused ? PrimaryGreen : BorderColor;
                int thickness = focused ? 2 : 1;
                using (Pen p = new Pen(c, thickness))
                {
                    int offset = thickness == 2 ? 1 : 0;
                    e.Graphics.DrawRectangle(p, offset, offset, wrapper.Width - thickness - offset, wrapper.Height - thickness - offset);
                }
            };

            textBox.Enter += (s, e) => { focused = true; wrapper.Invalidate(); };
            textBox.Leave += (s, e) => { focused = false; wrapper.Invalidate(); };
            wrapper.Click += (s, e) => textBox.Focus();

            wrapper.Controls.Add(textBox);
            return wrapper;
        }

        // Сокращение «Харитонюк Евгения Николаевна» -> «Харитонюк Е.Н.» для компактных подписей графиков
        public static string FormatShortName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
            string[] parts = fullName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0];
            if (parts.Length == 2) return $"{parts[0]} {parts[1][0]}.";
            return $"{parts[0]} {parts[1][0]}.{parts[2][0]}.";
        }
    }
}

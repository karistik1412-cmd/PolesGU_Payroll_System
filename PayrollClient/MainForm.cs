using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using ClosedXML.Excel;
using Newtonsoft.Json;
using PayrollClient.Network;
using PayrollClient.Services;

namespace PayrollClient
{
    public partial class MainForm : Form
    {
        private readonly string _userRole;
        private readonly int _currentUserId = -1;
        private readonly string _currentUserLogin = string.Empty;

        private readonly Panel _contentPanel;
        private Button _activeButton;

        // Кэш последнего расчёта и выбранный расчётный период
        private DataTable _payrollCache = null;
        private int _payrollCacheMonth = -1;
        private int _payrollCacheYear = -1;
        private int _payrollCacheHalfMode = 2;

        private int _selectedMonth = DateTime.Now.Month;
        private int _selectedYear = DateTime.Now.Year >= 2020 && DateTime.Now.Year <= 2035 ? DateTime.Now.Year : 2026;
        private int _selectedHalfMode = 2; // 1 — 1-я половина (аванс), 2 — итог месяца (1-я + 2-я половина)

        private readonly string[] _months = ExcelTimesheetService.MonthNames;
        private readonly string[] _shortMonths =
        {
            "Янв", "Фев", "Мар", "Апр", "Май", "Июн",
            "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"
        };

        private const int SidebarWidth = 240;
        private const int ContentMargin = 20;

        public MainForm(string authData)
        {
            // Поддерживаем формат "Роль|UserID|Логин" и прежний "Роль"
            string[] parts = (authData ?? string.Empty).Split('|');
            _userRole = parts.Length > 0 ? parts[0] : "Экономист";
            if (parts.Length > 1) int.TryParse(parts[1], out _currentUserId);
            if (parts.Length > 2) _currentUserLogin = parts[2];

            // Если сейчас середина месяца (15–24 число), по умолчанию ставим расчёт 1-й половины (аванс)
            int dayToday = DateTime.Today.Day;
            if (dayToday >= 15 && dayToday < 25)
                _selectedHalfMode = 1;

            string userCaption = !string.IsNullOrEmpty(_currentUserLogin)
                ? $"{_currentUserLogin} ({_userRole})"
                : _userRole;

            this.Text = $"ПолесГУ — Система начисления заработной платы | {userCaption}";
            this.Size = new Size(1320, 750);
            this.MinimumSize = new Size(1285, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = UiTheme.PageBg;
            this.Font = new Font("Segoe UI", 9.75f);
            UiTheme.EnableDoubleBuffering(this);

            // --- ЛЕВАЯ НАВИГАЦИОННАЯ ПАНЕЛЬ (ФИРМЕННЫЙ ЗЕЛЁНЫЙ ПОЛЕСГУ) ---
            Panel sidebar = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(SidebarWidth, this.ClientSize.Height),
                BackColor = UiTheme.SidebarBg,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            UiTheme.EnableDoubleBuffering(sidebar);
            this.Controls.Add(sidebar);

            // --- ПРАВАЯ РАБОЧАЯ ОБЛАСТЬ ---
            _contentPanel = new Panel
            {
                Location = new Point(SidebarWidth, 0),
                Size = new Size(this.ClientSize.Width - SidebarWidth, this.ClientSize.Height),
                BackColor = UiTheme.PageBg,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            UiTheme.EnableDoubleBuffering(_contentPanel);
            this.Controls.Add(_contentPanel);

            // --- ШАПКА БОКОВОГО МЕНЮ ---
            Panel brandPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = UiTheme.SidebarHeaderBg
            };
            brandPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UiTheme.SidebarDivider, 1))
                {
                    e.Graphics.DrawLine(p, 0, brandPanel.Height - 1, brandPanel.Width, brandPanel.Height - 1);
                }
            };

            Label lblBrand = new Label
            {
                Text = "ПОЛЕСГУ",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 17.5f, FontStyle.Bold),
                Location = new Point(22, 18),
                AutoSize = true
            };
            Label lblBrandSub = new Label
            {
                Text = "Расчёт заработной платы",
                ForeColor = UiTheme.SidebarTextMuted,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(24, 54),
                AutoSize = true
            };
            brandPanel.Controls.Add(lblBrand);
            brandPanel.Controls.Add(lblBrandSub);
            sidebar.Controls.Add(brandPanel);

            Panel menuSpacer = new Panel
            {
                Dock = DockStyle.Top,
                Height = 12,
                BackColor = UiTheme.SidebarBg
            };
            sidebar.Controls.Add(menuSpacer);
            menuSpacer.BringToFront();

            // --- ПУНКТЫ МЕНЮ ---
            Button btnHome = AddMenuButton(sidebar, "Главная", (s, e) => ShowWelcomeScreen());
            AddMenuButton(sidebar, "Сотрудники", (s, e) => LoadEmployeesScreen());

            if (_userRole == "Бухгалтер" || _userRole == "Администратор")
            {
                AddMenuButton(sidebar, "Табель", (s, e) => LoadTimesheetScreen());
                AddMenuButton(sidebar, "Расчет зарплаты", (s, e) => LoadPayrollScreen());
            }
            if (_userRole == "Администратор")
            {
                AddMenuButton(sidebar, "Пользователи", (s, e) => LoadUsersScreen());
            }

            // --- НИЖНИЙ БЛОК ПРОФИЛЯ И ВЫХОДА ---
            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 86,
                BackColor = UiTheme.SidebarHeaderBg
            };
            bottomPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UiTheme.SidebarDivider, 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, bottomPanel.Width, 0);
                }
            };

            string profileText = !string.IsNullOrEmpty(_currentUserLogin)
                ? $"{_currentUserLogin} · {_userRole}"
                : $"Роль: {_userRole}";

            Label lblRoleInfo = new Label
            {
                Text = profileText,
                ForeColor = UiTheme.SidebarTextMuted,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(22, 12),
                AutoSize = true
            };
            bottomPanel.Controls.Add(lblRoleInfo);

            Button btnExit = new Button
            {
                Text = "Выход из системы",
                Dock = DockStyle.Bottom,
                Height = 44,
                FlatStyle = FlatStyle.Flat,
                BackColor = UiTheme.SidebarHeaderBg,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(20, 0, 0, 0)
            };
            btnExit.FlatAppearance.BorderSize = 0;
            btnExit.FlatAppearance.MouseOverBackColor = UiTheme.SidebarHover;
            btnExit.Click += (s, e) => Application.Exit();
            bottomPanel.Controls.Add(btnExit);
            sidebar.Controls.Add(bottomPanel);

            SetActiveButton(btnHome);
            ShowWelcomeScreen();
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ МЕНЮ И ПАНЕЛЕЙ ---
        private void SetActiveButton(Button btn)
        {
            if (_activeButton != null)
            {
                _activeButton.BackColor = UiTheme.SidebarBg;
                _activeButton.ForeColor = Color.FromArgb(230, 244, 235);
                _activeButton.Font = new Font("Segoe UI", 10.5f, FontStyle.Regular);
                _activeButton.Invalidate();
            }

            _activeButton = btn;
            if (_activeButton != null)
            {
                _activeButton.BackColor = UiTheme.SidebarActive;
                _activeButton.ForeColor = Color.White;
                _activeButton.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
                _activeButton.Invalidate();
            }
        }

        private Button AddMenuButton(Panel sidebar, string text, EventHandler onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Height = 46,
                Dock = DockStyle.Top,
                FlatStyle = FlatStyle.Flat,
                BackColor = UiTheme.SidebarBg,
                ForeColor = Color.FromArgb(230, 244, 235),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(22, 0, 0, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = UiTheme.SidebarHover;
            btn.FlatAppearance.MouseDownBackColor = UiTheme.SidebarActive;

            btn.Paint += (s, e) =>
            {
                if (btn == _activeButton)
                {
                    using (SolidBrush indicator = new SolidBrush(Color.White))
                    {
                        e.Graphics.FillRectangle(indicator, 0, 7, 4, btn.Height - 14);
                    }
                }
            };

            btn.MouseEnter += (s, e) => { if (btn != _activeButton) btn.BackColor = UiTheme.SidebarHover; };
            btn.MouseLeave += (s, e) => { if (btn != _activeButton) btn.BackColor = UiTheme.SidebarBg; };
            btn.Click += (s, e) => { SetActiveButton(btn); onClick(s, e); };

            sidebar.Controls.Add(btn);
            btn.BringToFront();
            return btn;
        }

        private Panel CreateToolbarCard(int top = 76)
        {
            Panel toolbar = UiTheme.CreateCardPanel(leftAccent: false, topAccent: false);
            toolbar.Location = new Point(ContentMargin, top);
            toolbar.Size = new Size(_contentPanel.ClientSize.Width - (ContentMargin * 2), 52);
            toolbar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(toolbar);
            return toolbar;
        }

        private void AddPeriodSelectors(Panel toolbar, out ComboBox cmbMonth, out NumericUpDown nudYear, bool compact = false)
        {
            Label lblMonth = new Label
            {
                Text = "Месяц:",
                Location = new Point(compact ? 10 : 14, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = UiTheme.TextSecondary
            };
            toolbar.Controls.Add(lblMonth);

            int monthX = compact ? 60 : 68;
            int monthW = compact ? 104 : 118;
            cmbMonth = new ComboBox
            {
                Location = new Point(monthX, 12),
                Size = new Size(monthW, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };
            cmbMonth.Items.AddRange(_months);
            cmbMonth.SelectedIndex = _selectedMonth - 1;
            toolbar.Controls.Add(cmbMonth);

            int yearLblX = monthX + monthW + (compact ? 8 : 12);
            Label lblYear = new Label
            {
                Text = "Год:",
                Location = new Point(yearLblX, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = UiTheme.TextSecondary
            };
            toolbar.Controls.Add(lblYear);

            nudYear = new NumericUpDown
            {
                Location = new Point(yearLblX + 34, 13),
                Size = new Size(compact ? 64 : 70, 28),
                Minimum = 2020,
                Maximum = 2035,
                Value = _selectedYear,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            toolbar.Controls.Add(nudYear);
        }

        private static int CountWorkingDaysLocal(int year, int month)
        {
            int total = DateTime.DaysInMonth(year, month);
            int wd = 0;
            for (int d = 1; d <= total; d++)
            {
                DayOfWeek dow = new DateTime(year, month, d).DayOfWeek;
                if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday) wd++;
            }
            return wd;
        }

        // =========================================================================
        // 1. ГЛАВНАЯ СТРАНИЦА (БЕЗ НИЖНЕЙ СТРОКИ И БЕЗ ДУБЛИРУЮЩИХ КНОПОК МЕНЮ)
        // =========================================================================
        private void ShowWelcomeScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                $"Добро пожаловать, {_userRole}!",
                "Учреждение образования «Полесский государственный университет» — сводная панель системы");
            _contentPanel.AutoScroll = true;

            string empCount = "—";
            string userCount = "—";
            string deptCount = "—";
            string fundText = "0,00";

            try
            {
                var response = new ServerConnection().SendRequest(new NetworkMessage { Command = "GET_DASHBOARD" });
                if (response.IsSuccess && !string.IsNullOrEmpty(response.Data))
                {
                    string[] stats = response.Data.Split('|');
                    if (stats.Length > 0) empCount = stats[0];
                    if (stats.Length > 1) userCount = stats[1];
                    if (stats.Length > 2)
                    {
                        decimal totalFund;
                        if (decimal.TryParse(stats[2].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out totalFund))
                            fundText = Math.Round(totalFund, 2).ToString("N2");
                    }
                    if (stats.Length > 3) deptCount = stats[3];
                }
            }
            catch (Exception)
            {
                // Если сервер недоступен, отображаем аккуратные прочерки
            }

            // 4 карточки ключевых показателей
            TableLayoutPanel kpiGrid = new TableLayoutPanel
            {
                Location = new Point(ContentMargin - 6, 80),
                Size = new Size(_contentPanel.ClientSize.Width - (ContentMargin * 2) + 12, 132),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            for (int i = 0; i < 4; i++)
                kpiGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            kpiGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _contentPanel.Controls.Add(kpiGrid);

            kpiGrid.Controls.Add(CreateStatCard("СОТРУДНИКОВ В ШТАТЕ", empCount, "Физических лиц в базе"), 0, 0);
            kpiGrid.Controls.Add(CreateStatCard("КАФЕДР", deptCount, "Учебные кафедры ПолесГУ"), 1, 0);
            kpiGrid.Controls.Add(CreateStatCard("ПОЛЬЗОВАТЕЛЕЙ", userCount, "Учётные записи системы"), 2, 0);
            kpiGrid.Controls.Add(CreateStatCard("ОБЩИЙ ФОНД ВЫПЛАТ", $"{fundText} р.", "Сумма сохранённых расчётов"), 3, 0);

            // Справочная карточка текущего расчётного периода (без дублирующих кнопок)
            DateTime today = DateTime.Today;
            int curWorkDays = CountWorkingDaysLocal(today.Year, today.Month);
            decimal curTeacherHours = Math.Round(curWorkDays * 7.2m, 1);

            string payoutStage;
            if (today.Day < 15)
                payoutStage = "Идёт 1-я половина месяца (расчёт аванса открывается с 15-го числа)";
            else if (today.Day < 25)
                payoutStage = "Доступен расчёт 1-й половины (аванс); окончательный расчёт — с 25-го числа";
            else
                payoutStage = "Доступен окончательный расчёт за весь месяц (1-я и 2-я половины)";

            Panel infoCard = UiTheme.CreateCardPanel(leftAccent: true, topAccent: false);
            infoCard.Location = new Point(ContentMargin, 232);
            infoCard.Size = new Size(_contentPanel.ClientSize.Width - (ContentMargin * 2), 148);
            infoCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(infoCard);

            Label lblInfoTitle = new Label
            {
                Text = $"Справка по текущему периоду — {_months[today.Month - 1]} {today.Year} г. (сегодня {today:dd.MM.yyyy})",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(22, 15),
                AutoSize = true
            };
            Label lblLine1 = new Label
            {
                Text = $"• Плановая норма месяца (на 1,0 ставку ППС):  {curWorkDays} раб. дн.  /  {curTeacherHours:0.#} ч.",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Regular),
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(22, 48),
                AutoSize = true
            };
            Label lblLine2 = new Label
            {
                Text = $"• График выплаты зарплаты:  {payoutStage}",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Regular),
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(22, 76),
                AutoSize = true
            };
            Label lblLine3 = new Label
            {
                Text = "• Учёт при расчёте:  суммирование ставок по совместительству, больничные (Б) и отпуска (О), налоговые льготы (ст. 209 НК РБ)",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Regular),
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(22, 104),
                AutoSize = true
            };

            infoCard.Controls.AddRange(new Control[] { lblInfoTitle, lblLine1, lblLine2, lblLine3 });
            if (_userRole == "Бухгалтер" || _userRole == "Администратор" || _userRole == "Экономист")
                LoadChartsScreen(true);
        }

        private Panel CreateStatCard(string title, string value, string caption)
        {
            Panel card = UiTheme.CreateCardPanel(leftAccent: false, topAccent: true);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(6);

            Label lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(16, 15),
                AutoSize = true
            };
            card.Controls.Add(lblTitle);

            Label lblValue = new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = UiTheme.PrimaryGreen,
                Location = new Point(14, 38),
                AutoSize = true
            };
            card.Controls.Add(lblValue);

            Label lblCap = new Label
            {
                Text = caption,
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(16, 86),
                AutoSize = true
            };
            card.Controls.Add(lblCap);

            return card;
        }

        // =========================================================================
        // 2. ЭКРАН «СОТРУДНИКИ» (СОВМЕСТИТЕЛЬСТВО, ЛЬГОТЫ, КАФЕДРЫ БЕЗ СЛОВА «КАФЕДРА»)
        // =========================================================================
        private void LoadEmployeesScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                "Сотрудники университета",
                "Учёт профессорско-преподавательского состава кафедр, совместительства и налоговых льгот");

            bool canManage = _userRole == "Администратор" || _userRole == "Бухгалтер";
            Panel toolbar = CreateToolbarCard();

            Button btnAddEmp = UiTheme.CreatePrimaryButton("+ Принять на работу", 12, 8, 172);
            Button btnEditEmp = UiTheme.CreateOutlineButton("Редактировать", 192, 8, 135);
            Button btnDetails = UiTheme.CreateOutlineButton("Карточка сотрудника", canManage ? 335 : 12, 8, 166);
            Button btnDeleteEmp = UiTheme.CreateDangerButton("Уволить", 509, 8, 106);

            if (canManage)
            {
                toolbar.Controls.Add(btnAddEmp);
                toolbar.Controls.Add(btnEditEmp);
                toolbar.Controls.Add(btnDeleteEmp);
            }
            toolbar.Controls.Add(btnDetails);

            Label lblSearchEmp = new Label { Text = "Поиск:", Font = new Font("Segoe UI", 9.5f), ForeColor = UiTheme.TextSecondary, Location = new Point(630, 16), AutoSize = true };
            TextBox txtSearchEmp = new TextBox { Location = new Point(682, 12), Width = 210, Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.FixedSingle };
            toolbar.Controls.Add(lblSearchEmp);
            toolbar.Controls.Add(txtSearchEmp);

            Label lblFooter;
            Panel footer = UiTheme.CreateSummaryFooter(_contentPanel, ContentMargin, out lblFooter);

            int gridTop = 140;
            int gridHeight = footer.Top - gridTop - 12;
            int innerWidth = _contentPanel.ClientSize.Width - (ContentMargin * 2);

            DataGridView dgv = UiTheme.CreateStyledGrid(_contentPanel, ContentMargin, gridTop, innerWidth, gridHeight, readOnly: true);
            txtSearchEmp.TextChanged += (s, e) =>
            {
                DataTable dt = dgv.DataSource as DataTable;
                if (dt != null && dt.Columns.Contains("ФИО"))
                    dt.DefaultView.RowFilter = string.IsNullOrWhiteSpace(txtSearchEmp.Text)
                        ? ""
                        : $"[ФИО] LIKE '%{txtSearchEmp.Text.Trim().Replace("'", "''")}%'";
            };
            ContextMenuStrip cmsEmp = new ContextMenuStrip();
            if (canManage) cmsEmp.Items.Add("Редактировать", null, (s, e) => btnEditEmp.PerformClick());
            if (canManage) cmsEmp.Items.Add("Изменить стаж (лет)", null, (s, e) =>
            {
                DataGridViewRow sel = dgv.CurrentRow ?? (dgv.SelectedRows.Count > 0 ? dgv.SelectedRows[0] : null);
                if (sel == null) return;
                int empId = Convert.ToInt32(sel.Cells["EmployeeID"].Value);
                string empName = sel.Cells["ФИО"].Value.ToString();
                using (Form f = new Form { Text = "Изменение стажа", Size = new Size(320, 160), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
                {
                    Label lbl = new Label { Text = $"Стаж (лет) для: {empName}", Location = new Point(16, 16), AutoSize = true };
                    decimal curExp = 1;
                    if (dgv.Columns["Стаж, лет"] != null && sel.Cells["Стаж, лет"].Value != null)
                        decimal.TryParse(sel.Cells["Стаж, лет"].Value.ToString(), out curExp);
                    NumericUpDown nud = new NumericUpDown { Location = new Point(16, 44), Width = 120, Minimum = 0, Maximum = 50, Value = Math.Min(50, Math.Max(0, curExp)) };
                    Button ok = UiTheme.CreatePrimaryButton("Сохранить", 160, 42, 120, 30);
                    ok.Click += (s2, e2) => { f.DialogResult = DialogResult.OK; f.Close(); };
                    f.Controls.AddRange(new Control[] { lbl, nud, ok });
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        new ServerConnection().SendRequest(new NetworkMessage { Command = "UPDATE_EXPERIENCE", Data = $"{empId}|{(int)nud.Value}" });
                        LoadEmployeesScreen();
                    }
                }
            });
            cmsEmp.Items.Add("Карточка сотрудника", null, (s, e) => btnDetails.PerformClick());
            if (canManage) cmsEmp.Items.Add("Уволить", null, (s, e) => btnDeleteEmp.PerformClick());
            dgv.ContextMenuStrip = cmsEmp;

            Action loadData = () =>
            {
                try
                {
                    var response = new ServerConnection().SendRequest(new NetworkMessage { Command = "GET_EMPLOYEES" });
                    if (response.IsSuccess)
                    {
                        DataTable dt = JsonConvert.DeserializeObject<DataTable>(response.Data);

                        // На случай старых данных убираем префикс «Кафедра »
                        if (dt.Columns.Contains("Кафедра"))
                        {
                            foreach (DataRow r in dt.Rows)
                            {
                                if (r["Кафедра"] != DBNull.Value)
                                    r["Кафедра"] = UiTheme.FormatDepartmentName(r["Кафедра"].ToString());
                            }
                        }

                        dgv.DataSource = dt;

                        string[] hiddenCols = { "EmployeeID", "PositionID", "DepartmentID", "BenefitCategory", "ChildrenBenefit" };
                        foreach (string hc in hiddenCols)
                        {
                            if (dgv.Columns[hc] != null) dgv.Columns[hc].Visible = false;
                        }

                        if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].FillWeight = 155;
                        if (dgv.Columns["Кафедра"] != null) dgv.Columns["Кафедра"].FillWeight = 155;
                        if (dgv.Columns["Должность"] != null) dgv.Columns["Должность"].FillWeight = 115;
                        if (dgv.Columns["Стаж, лет"] != null)
                        {
                            dgv.Columns["Стаж, лет"].FillWeight = 62;
                            dgv.Columns["Стаж, лет"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                            dgv.Columns["Стаж, лет"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        }
                        if (dgv.Columns["Ставка"] != null)
                        {
                            dgv.Columns["Ставка"].FillWeight = 52;
                            dgv.Columns["Ставка"].DefaultCellStyle.Format = "0.##";
                            dgv.Columns["Ставка"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                            dgv.Columns["Ставка"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        }
                        if (dgv.Columns["Тип занятости"] != null) dgv.Columns["Тип занятости"].FillWeight = 115;
                        if (dgv.Columns["Льготы"] != null) dgv.Columns["Льготы"].Visible = false;
                        if (dgv.Columns["Дата приёма"] != null)
                        {
                            dgv.Columns["Дата приёма"].FillWeight = 82;
                            dgv.Columns["Дата приёма"].DefaultCellStyle.Format = "dd.MM.yyyy";
                            dgv.Columns["Дата приёма"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                            dgv.Columns["Дата приёма"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        }

                        // Сводные показатели в нижней панели
                        var uniquePeople = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var peopleWithBenefits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var departments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        decimal sumRates = 0m;
                        double sumExpYears = 0.0;
                        int expCount = 0;

                        string hireCol = dt.Columns.Contains("Дата приёма") ? "Дата приёма" : "Дата приема";

                        foreach (DataRow row in dt.Rows)
                        {
                            string fn = row["ФИО"].ToString().Trim();
                            bool isFirstRecordOfPerson = uniquePeople.Add(fn);

                            if (dt.Columns.Contains("Кафедра") && row["Кафедра"] != DBNull.Value)
                                departments.Add(row["Кафедра"].ToString());

                            if (dt.Columns.Contains("Ставка") && row["Ставка"] != DBNull.Value)
                                sumRates += Convert.ToDecimal(row["Ставка"]);
                            else
                                sumRates += 1.0m;

                            if (dt.Columns.Contains("Льготы") && row["Льготы"] != DBNull.Value)
                            {
                                string ben = row["Льготы"].ToString();
                                if (!string.IsNullOrEmpty(ben) && ben != "Нет")
                                    peopleWithBenefits.Add(fn);
                            }

                            if (isFirstRecordOfPerson && dt.Columns.Contains(hireCol) && row[hireCol] != DBNull.Value)
                            {
                                DateTime hd = Convert.ToDateTime(row[hireCol]);
                                sumExpYears += Math.Max(0.0, (DateTime.Today - hd).TotalDays / 365.25);
                                expCount++;
                            }
                        }

                        double avgExp = expCount > 0 ? sumExpYears / expCount : 0.0;
                        string peopleText = dt.Rows.Count > uniquePeople.Count
                            ? $"Сотрудников: {uniquePeople.Count} (записей: {dt.Rows.Count})"
                            : $"Сотрудников: {uniquePeople.Count}";

                        lblFooter.Text =
                            $"{peopleText}  |  " +
                            $"Кафедр: {departments.Count}  |  " +
                            $"Суммарно ставок: {sumRates:0.##}  |  " +
                            $"С льготами: {peopleWithBenefits.Count}  |  " +
                            $"Средний стаж: {avgExp:N1} г.";
                    }
                    else
                    {
                        MessageBox.Show("Ошибка: " + response.ErrorMessage);
                    }
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
            };
            loadData();

            btnAddEmp.Click += (s, e) =>
            {
                using (var form = new AddEmployeeForm())
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        _payrollCache = null;
                        loadData();
                    }
                }
            };

            btnEditEmp.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите сотрудника!"); return; }
                DataGridViewRow sel = dgv.SelectedRows[0];
                int empId = Convert.ToInt32(sel.Cells["EmployeeID"].Value);
                string fullName = sel.Cells["ФИО"].Value.ToString();
                int posId = Convert.ToInt32(sel.Cells["PositionID"].Value);
                int deptId = Convert.ToInt32(sel.Cells["DepartmentID"].Value);

                decimal? rate = null;
                if (dgv.Columns["Ставка"] != null && sel.Cells["Ставка"].Value != null && sel.Cells["Ставка"].Value != DBNull.Value)
                    rate = Convert.ToDecimal(sel.Cells["Ставка"].Value);

                string empType = null;
                if (dgv.Columns["Тип занятости"] != null && sel.Cells["Тип занятости"].Value != null && sel.Cells["Тип занятости"].Value != DBNull.Value)
                    empType = sel.Cells["Тип занятости"].Value.ToString();

                int benCat = dgv.Columns["BenefitCategory"] != null && sel.Cells["BenefitCategory"].Value != DBNull.Value
                    ? Convert.ToInt32(sel.Cells["BenefitCategory"].Value) : 0;
                int childBen = dgv.Columns["ChildrenBenefit"] != null && sel.Cells["ChildrenBenefit"].Value != DBNull.Value
                    ? Convert.ToInt32(sel.Cells["ChildrenBenefit"].Value) : 0;

                using (var form = new AddEmployeeForm(empId, fullName, posId, deptId, rate, empType, benCat, childBen))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        _payrollCache = null;
                        loadData();
                    }
                }
            };

            btnDeleteEmp.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите запись сотрудника!"); return; }
                DataGridViewRow sel = dgv.SelectedRows[0];
                int empId = Convert.ToInt32(sel.Cells["EmployeeID"].Value);
                string fullName = sel.Cells["ФИО"].Value.ToString();
                string empType = dgv.Columns["Тип занятости"] != null ? sel.Cells["Тип занятости"].Value.ToString() : "Основное";

                if (MessageBox.Show(
                    $"Удалить запись сотрудника «{fullName}» ({empType})?",
                    "Увольнение / снятие совместительства",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    var res = new ServerConnection().SendRequest(new NetworkMessage { Command = "DELETE_EMPLOYEE", Data = empId.ToString() });
                    if (res.IsSuccess)
                    {
                        _payrollCache = null;
                        loadData();
                        MessageBox.Show("Запись сотрудника удалена.", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Ошибка: " + res.ErrorMessage);
                    }
                }
            };

            btnDetails.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите сотрудника!"); return; }
                DataGridViewRow sel = dgv.SelectedRows[0];
                string fullName = sel.Cells["ФИО"].Value.ToString();
                string position = sel.Cells["Должность"].Value.ToString();
                string department = sel.Cells["Кафедра"].Value.ToString();
                string hireCol = dgv.Columns["Дата приёма"] != null ? "Дата приёма" : "Дата приема";
                DateTime hireDate = Convert.ToDateTime(sel.Cells[hireCol].Value);
                string empType = dgv.Columns["Тип занятости"] != null ? sel.Cells["Тип занятости"].Value.ToString() : "Основное";
                string benefits = dgv.Columns["Льготы"] != null ? sel.Cells["Льготы"].Value.ToString() : "Нет";

                // Считаем суммарную ставку этого сотрудника по всем его записям (основное + совместительство)
                decimal totalRate = 0m;
                DataTable dt = dgv.DataSource as DataTable;
                if (dt != null && dt.Columns.Contains("Ставка"))
                {
                    foreach (DataRow r in dt.Rows)
                    {
                        if (string.Equals(r["ФИО"].ToString().Trim(), fullName.Trim(), StringComparison.OrdinalIgnoreCase))
                            totalRate += Convert.ToDecimal(r["Ставка"]);
                    }
                }

                ShowEmployeeDetails(fullName, position, department, empType, totalRate, benefits, hireDate);
            };
        }

        // =========================================================================
        // 3. ЭКРАН «ТАБЕЛЬ» (БЕЗ ДОЛЖНОСТИ И СТАВКИ, С УЧЁТОМ Б/О И ЕДИНИЦАМИ ИЗМЕРЕНИЯ)
        // =========================================================================
        private void LoadTimesheetScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                "Табель учёта рабочего времени",
                null);

            Panel toolbar = CreateToolbarCard();

            ComboBox cmbMonth;
            NumericUpDown nudYear;
            AddPeriodSelectors(toolbar, out cmbMonth, out nudYear);

            Button btnImport = UiTheme.CreatePrimaryButton("Импорт из Excel", 322, 8, 155);
            Button btnSaveManual = UiTheme.CreateOutlineButton("Сохранить правки", 487, 8, 158);
            Button btnClear = UiTheme.CreateDangerButton("Сбросить", 655, 8, 108);
            Label lblSearchTs = new Label { Text = "Поиск:", Font = new Font("Segoe UI", 9.5f), ForeColor = UiTheme.TextSecondary, Location = new Point(776, 16), AutoSize = true };
            TextBox txtSearchTs = new TextBox { Location = new Point(828, 12), Width = 180, Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.FixedSingle };

            toolbar.Controls.Add(btnImport);
            toolbar.Controls.Add(btnSaveManual);
            toolbar.Controls.Add(btnClear);
            toolbar.Controls.Add(lblSearchTs);
            toolbar.Controls.Add(txtSearchTs);

            Label lblFooter;
            Panel footer = UiTheme.CreateSummaryFooter(_contentPanel, ContentMargin, out lblFooter);

            int gridTop = 140;
            int gridHeight = footer.Top - gridTop - 12;
            int innerWidth = _contentPanel.ClientSize.Width - (ContentMargin * 2);

            DataGridView dgv = UiTheme.CreateStyledGrid(_contentPanel, ContentMargin, gridTop, innerWidth, gridHeight, readOnly: false);
            txtSearchTs.TextChanged += (s, e) =>
            {
                if (dgv.DataSource == null || dgv.Columns["ФИО"] == null) return;
                string q = txtSearchTs.Text.Trim().ToLower();
                CurrencyManager cm = (CurrencyManager)BindingContext[dgv.DataSource];
                cm.SuspendBinding();
                foreach (DataGridViewRow row in dgv.Rows)
                {
                    if (row.IsNewRow) continue;
                    string fio = (row.Cells["ФИО"].Value ?? "").ToString().ToLower();
                    row.Visible = string.IsNullOrEmpty(q) || fio.Contains(q);
                }
                cm.ResumeBinding();
            };
            dgv.DataError += (s, ev) =>
            {
                MessageBox.Show("Введите корректное числовое значение для дней или часов.",
                    "Некорректное значение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ev.Cancel = true;
            };

            bool suppressEvents = false;

            Action<DataTable> updateTimesheetFooter = (DataTable table) =>
            {
                if (table == null || table.Rows.Count == 0)
                {
                    lblFooter.Text = "Записей: 0  |  Нет данных для отображения.";
                    return;
                }

                int totalRows = table.Rows.Count;
                int loadedCount = 0;
                int sumFactDays = 0;
                int sumNormDays = 0;
                decimal sumFactHours = 0m;
                decimal sumNormHours = 0m;
                int sumSick = 0;
                int sumVac = 0;

                foreach (DataRow row in table.Rows)
                {
                    if (table.Columns.Contains("Статус") && row["Статус"] != DBNull.Value
                        && row["Статус"].ToString() == "Из табеля")
                    {
                        loadedCount++;
                    }
                    if (table.Columns.Contains("Отработано, дн.") && row["Отработано, дн."] != DBNull.Value)
                        sumFactDays += Convert.ToInt32(row["Отработано, дн."]);
                    if (table.Columns.Contains("Норма, дн.") && row["Норма, дн."] != DBNull.Value)
                        sumNormDays += Convert.ToInt32(row["Норма, дн."]);
                    if (table.Columns.Contains("Отработано, ч.") && row["Отработано, ч."] != DBNull.Value)
                        sumFactHours += Convert.ToDecimal(row["Отработано, ч."]);
                    if (table.Columns.Contains("Норма, ч.") && row["Норма, ч."] != DBNull.Value)
                        sumNormHours += Convert.ToDecimal(row["Норма, ч."]);
                    if (table.Columns.Contains("Больничный (Б), дн.") && row["Больничный (Б), дн."] != DBNull.Value)
                        sumSick += Convert.ToInt32(row["Больничный (Б), дн."]);
                    if (table.Columns.Contains("Отпуск (О), дн.") && row["Отпуск (О), дн."] != DBNull.Value)
                        sumVac += Convert.ToInt32(row["Отпуск (О), дн."]);
                }

                decimal pct = sumNormHours > 0m ? Math.Round((sumFactHours / sumNormHours) * 100m, 1) : 0m;
                lblFooter.Text =
                    $"Записей: {totalRows}  |  " +
                    $"Из табеля: {loadedCount}/{totalRows}  |  " +
                    $"Дни: {sumFactDays}/{sumNormDays} дн.  |  " +
                    $"Часы: {sumFactHours:0.#}/{sumNormHours:0.#} ч. ({pct:0.#}%)  |  " +
                    $"Больничный (Б): {sumSick} дн.  |  " +
                    $"Отпуск (О): {sumVac} дн.";
            };

            string[] editableCols =
            {
                "Отработано, дн.", "Норма, дн.",
                "Отработано, ч.", "Норма, ч.",
                "Больничный (Б), дн.", "Отпуск (О), дн."
            };

            Action loadTimesheets = () =>
            {
                try
                {
                    int m = cmbMonth.SelectedIndex + 1;
                    int y = (int)nudYear.Value;
                    _selectedMonth = m;
                    _selectedYear = y;

                    var req = new NetworkMessage { Command = "GET_TIMESHEETS", Data = $"{m}|{y}" };
                    var res = new ServerConnection().SendRequest(req);

                    if (!res.IsSuccess)
                    {
                        MessageBox.Show("Ошибка загрузки табеля: " + res.ErrorMessage, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    DataTable dt = JsonConvert.DeserializeObject<DataTable>(res.Data);
                    dgv.DataSource = dt;

                    // Скрываем служебные поля (Должность и Ставка в табеле не отображаются)
                    string[] hideCols = { "EmployeeID", "Category", "Rate", "RawFullName", "Должность", "Кафедра", "Ставка" };
                    foreach (string hc in hideCols)
                    {
                        if (dgv.Columns[hc] != null) dgv.Columns[hc].Visible = false;
                    }

                    foreach (DataGridViewColumn col in dgv.Columns)
                    {
                        bool isEditable = editableCols.Contains(col.Name);
                        col.ReadOnly = !isEditable;
                        if (isEditable)
                        {
                            col.DefaultCellStyle.BackColor = UiTheme.EditableCellBg;
                            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                            col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                            col.FillWeight = 82;
                        }
                    }

                    if (dgv.Columns["Отработано, ч."] != null) dgv.Columns["Отработано, ч."].DefaultCellStyle.Format = "0.#";
                    if (dgv.Columns["Норма, ч."] != null) dgv.Columns["Норма, ч."].DefaultCellStyle.Format = "0.#";
                    if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].FillWeight = 175;
                    if (dgv.Columns["Статус"] != null)
                    {
                        dgv.Columns["Статус"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        dgv.Columns["Статус"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        dgv.Columns["Статус"].FillWeight = 78;
                    }

                    foreach (DataGridViewRow row in dgv.Rows)
                    {
                        if (dgv.Columns["Статус"] != null && row.Cells["Статус"].Value != null)
                        {
                            string st = row.Cells["Статус"].Value.ToString();
                            bool fromTs = st == "Из табеля";
                            row.Cells["Статус"].Style.ForeColor = fromTs ? UiTheme.PrimaryGreen : UiTheme.TextSecondary;
                            row.Cells["Статус"].Style.SelectionForeColor = fromTs ? UiTheme.PrimaryGreen : UiTheme.SelectionFg;
                            row.Cells["Статус"].Style.Font = new Font("Segoe UI", 9.25f,
                                fromTs ? FontStyle.Bold : FontStyle.Regular);
                        }
                    }

                    updateTimesheetFooter(dt);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка сети: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            dgv.CellEndEdit += (s, e) => updateTimesheetFooter(dgv.DataSource as DataTable);
            cmbMonth.SelectedIndexChanged += (s, e) => { if (!suppressEvents) loadTimesheets(); };
            nudYear.ValueChanged += (s, e) => { if (!suppressEvents) loadTimesheets(); };

            btnImport.Click += (s, e) =>
            {
                int m = cmbMonth.SelectedIndex + 1;
                int y = (int)nudYear.Value;

                if (RunExcelTimesheetImport(ref m, ref y))
                {
                    suppressEvents = true;
                    cmbMonth.SelectedIndex = m - 1;
                    if (y >= (int)nudYear.Minimum && y <= (int)nudYear.Maximum) nudYear.Value = y;
                    suppressEvents = false;
                    loadTimesheets();
                }
            };

            btnSaveManual.Click += (s, e) =>
            {
                try
                {
                    dgv.EndEdit();
                    DataTable dt = dgv.DataSource as DataTable;
                    if (dt == null || dt.Rows.Count == 0) return;

                    int m = cmbMonth.SelectedIndex + 1;
                    int y = (int)nudYear.Value;
                    var entries = new List<TimesheetEntryDto>();

                    foreach (DataRow row in dt.Rows)
                    {
                        int days = Convert.ToInt32(row["Отработано, дн."]);
                        int normD = Convert.ToInt32(row["Норма, дн."]);
                        decimal hours = Convert.ToDecimal(row["Отработано, ч."]);
                        decimal normH = Convert.ToDecimal(row["Норма, ч."]);
                        int sickD = dt.Columns.Contains("Больничный (Б), дн.") ? Convert.ToInt32(row["Больничный (Б), дн."]) : 0;
                        int vacD = dt.Columns.Contains("Отпуск (О), дн.") ? Convert.ToInt32(row["Отпуск (О), дн."]) : 0;

                        if (days < 0 || days > 31 || normD <= 0 || normD > 31 ||
                            hours < 0m || normH <= 0m || sickD < 0 || sickD > 31 || vacD < 0 || vacD > 31)
                        {
                            MessageBox.Show($"Проверьте корректность дней, часов и Б/О у сотрудника «{row["ФИО"]}».",
                                "Ошибка валидации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        string rawName = dt.Columns.Contains("RawFullName") && row["RawFullName"] != DBNull.Value
                            ? row["RawFullName"].ToString()
                            : row["ФИО"].ToString();
                        decimal? rateVal = dt.Columns.Contains("Rate") && row["Rate"] != DBNull.Value
                            ? (decimal?)Convert.ToDecimal(row["Rate"])
                            : null;

                        entries.Add(new TimesheetEntryDto
                        {
                            EmployeeId = Convert.ToInt32(row["EmployeeID"]),
                            FullName = rawName,
                            Rate = rateVal,
                            DaysWorked = days,
                            NormDays = normD,
                            HoursWorked = hours,
                            NormHours = normH,
                            HasHours = true,
                            SickDays = sickD,
                            VacationDays = vacD
                        });
                    }

                    var payload = new TimesheetImportRequestDto { Month = m, Year = y, Entries = entries };
                    var req = new NetworkMessage
                    {
                        Command = "IMPORT_TIMESHEETS",
                        Data = JsonConvert.SerializeObject(payload)
                    };
                    var res = new ServerConnection().SendRequest(req);
                    if (res.IsSuccess)
                    {
                        _payrollCache = null;
                        loadTimesheets();
                        MessageBox.Show("Данные табеля сохранены в БД.",
                            "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Ошибка сохранения: " + res.ErrorMessage, "Ошибка",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка сохранения табеля: " + ex.Message, "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            btnClear.Click += (s, e) =>
            {
                int m = cmbMonth.SelectedIndex + 1;
                int y = (int)nudYear.Value;
                if (MessageBox.Show($"Сбросить загруженный табель за {_months[m - 1]} {y} г. к стандартной норме?",
                    "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        var res = new ServerConnection().SendRequest(
                            new NetworkMessage { Command = "CLEAR_TIMESHEETS", Data = $"{m}|{y}" });
                        if (res.IsSuccess)
                        {
                            _payrollCache = null;
                            loadTimesheets();
                            MessageBox.Show(res.Data, "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("Ошибка: " + res.ErrorMessage);
                        }
                    }
                    catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
                }
            };

            loadTimesheets();
        }

        private bool RunExcelTimesheetImport(ref int month, ref int year)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Выберите файл табеля учёта рабочего времени";
                ofd.Filter = "Табель Excel (*.xlsx)|*.xlsx";

                if (ofd.ShowDialog() != DialogResult.OK)
                    return false;

                try
                {
                    ExcelParseResult parsed = ExcelTimesheetService.ParseTimesheetFile(ofd.FileName, month, year);

                    int fileMonth = parsed.DetectedMonth ?? month;
                    int fileYear = parsed.DetectedYear ?? year;
                    if (!parsed.IsIndividualPlan && (fileMonth != month || fileYear != year))
                    {
                        DialogResult choice = MessageBox.Show(
                            $"В файле табеля определён период: {_months[fileMonth - 1]} {fileYear} г.\n" +
                            $"Сейчас выбран период: {_months[month - 1]} {year} г.\n\n" +
                            $"Загрузить табель за {_months[fileMonth - 1]} {fileYear} г.?\n" +
                            $"«Да» — за {_months[fileMonth - 1]} {fileYear} г., «Нет» — за {_months[month - 1]} {year} г.",
                            "Выбор расчётного периода",
                            MessageBoxButtons.YesNoCancel,
                            MessageBoxIcon.Question);

                        if (choice == DialogResult.Cancel) return false;
                        if (choice == DialogResult.Yes)
                        {
                            month = fileMonth;
                            year = fileYear;
                        }
                    }

                    var importPayload = new TimesheetImportRequestDto
                    {
                        Month = month,
                        Year = year,
                        Entries = parsed.Entries
                    };

                    var req = new NetworkMessage
                    {
                        Command = "IMPORT_TIMESHEETS",
                        Data = JsonConvert.SerializeObject(importPayload)
                    };

                    var res = new ServerConnection().SendRequest(req);
                    if (!res.IsSuccess)
                    {
                        MessageBox.Show("Ошибка сервера при импорте: " + res.ErrorMessage,
                            "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    var importResult = JsonConvert.DeserializeObject<TimesheetImportResultDto>(res.Data);
                    _selectedMonth = month;
                    _selectedYear = year;
                    _payrollCache = null;

                    int totalSick = parsed.Entries.Sum(x => x.SickDays);
                    int totalVac = parsed.Entries.Sum(x => x.VacationDays);
                    string absenceNote = (totalSick > 0 || totalVac > 0)
                        ? $"\nУчтены неявки: больничный (Б) — {totalSick} дн., отпуск (О) — {totalVac} дн."
                        : string.Empty;

                    string extra = !string.IsNullOrEmpty(parsed.InfoMessage) ? parsed.InfoMessage + "\n\n" : string.Empty;
                    string tip = (importResult.SavedCount == 0 && importResult.UnmatchedNames != null && importResult.UnmatchedNames.Count > 0)
                        ? "\n\nПроверьте совпадение ФИО в Excel-файле со списком во вкладке «Сотрудники»."
                        : string.Empty;

                    MessageBox.Show(
                        $"{extra}Период: {_months[month - 1]} {year} г. (лист «{parsed.SheetName}»)\n" +
                        importResult.Message + absenceNote + tip,
                        "Импорт табеля завершён",
                        MessageBoxButtons.OK,
                        importResult.SavedCount > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                    return importResult.SavedCount > 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка чтения или импорта табеля:\n" + ex.Message,
                        "Ошибка импорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
        }

        // =========================================================================
        // 4. ЭКРАН «РАСЧЕТ ЗАРПЛАТЫ» (2 ПОЛОВИНЫ МЕСЯЦА, Б/О, ЛЬГОТЫ, СУММА СТАВОК)
        // =========================================================================
        private void LoadPayrollScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                "Расчёт заработной платы",
                "Расчёт по двум половинам месяца (аванс и окончательная выплата) с учётом совместительства, Б/О и налоговых льгот");

            Panel toolbar = CreateToolbarCard();

            ComboBox cmbMonth;
            NumericUpDown nudYear;
            AddPeriodSelectors(toolbar, out cmbMonth, out nudYear, compact: true);

            // Выбор этапа выплаты: 1-я половина (середина месяца) или итог месяца (1-я + 2-я половина)
            ComboBox cmbHalf = new ComboBox
            {
                Location = new Point(276, 12),
                Size = new Size(202, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };
            cmbHalf.Items.Add("1-я половина (аванс)");
            cmbHalf.Items.Add("2-я половина (окончательно)");
            cmbHalf.Items.Add("Весь месяц (1-я + 2-я пол.)");
            cmbHalf.SelectedIndex = _selectedHalfMode == 1 ? 0 : (_selectedHalfMode == 3 ? 2 : 1);
            toolbar.Controls.Add(cmbHalf);

            Button btnCalculate = UiTheme.CreatePrimaryButton("Рассчитать", 520, 8, 92);
            Button btnImportTs = UiTheme.CreateAccentButton("Импорт табеля", 618, 8, 112);
            Button btnSave = UiTheme.CreateOutlineButton("Сохранить в БД", 736, 8, 108);
            Button btnExport = UiTheme.CreateOutlineButton("В Excel", 850, 8, 72);
            Button btnExportOne = UiTheme.CreateOutlineButton("Листок в Excel", 928, 8, 108);

            btnSave.Enabled = false;
            btnExport.Enabled = false;
            btnExportOne.Enabled = false;

            toolbar.Controls.AddRange(new Control[] { btnCalculate, btnImportTs, btnSave, btnExport, btnExportOne });
            TextBox txtSearchPay = new TextBox { Location = new Point(1042, 12), Width = 100, Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.FixedSingle };
            toolbar.Controls.Add(txtSearchPay);

            Label lblTotals;
            Panel footer = UiTheme.CreateSummaryFooter(_contentPanel, ContentMargin, out lblTotals);
            lblTotals.Text = $"Период: {_months[_selectedMonth - 1]} {_selectedYear} г.  |  Выберите этап выплаты и нажмите «Рассчитать».";

            int gridTop = 140;
            int gridHeight = footer.Top - gridTop - 12;
            int innerWidth = _contentPanel.ClientSize.Width - (ContentMargin * 2);

            DataGridView dgv = UiTheme.CreateStyledGrid(_contentPanel, ContentMargin, gridTop, innerWidth, gridHeight, readOnly: true);
            txtSearchPay.TextChanged += (s, e) =>
            {
                if (dgv.DataSource == null || dgv.Columns["ФИО"] == null) return;
                string q = txtSearchPay.Text.Trim().ToLower();
                CurrencyManager cm = (CurrencyManager)BindingContext[dgv.DataSource];
                cm.SuspendBinding();
                foreach (DataGridViewRow row in dgv.Rows)
                {
                    if (row.IsNewRow) continue;
                    string fio = (row.Cells["ФИО"].Value ?? "").ToString().ToLower();
                    row.Visible = string.IsNullOrEmpty(q) || fio.Contains(q);
                }
                cm.ResumeBinding();
            };
            dgv.CellMouseDown += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    dgv.ClearSelection();
                    dgv.CurrentCell = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex];
                    dgv.Rows[e.RowIndex].Selected = true;
                }
            };
            ContextMenuStrip cmsPay = new ContextMenuStrip();
            cmsPay.Items.Add("Листок в Excel", null, (s, e) => btnExportOne.PerformClick());
            dgv.ContextMenuStrip = cmsPay;
            dgv.ShowCellToolTips = true;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
            dgv.DefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            var headerTooltips = new Dictionary<string, string>
            {
                { "Ставка", "Суммарная ставка сотрудника (основное место + внутреннее совместительство)" },
                { "Дни, дн.", "Фактически отработанные дни / плановая норма дней" },
                { "Часы, ч.", "Фактически отработанные часы / норма часов (суммарно по всем ставкам)" },
                { "Б / О, дн.", "Количество дней больничного (Б) / оплачиваемого отпуска (О)" },
                { "Оклад, руб.", "Начисленный оклад за фактически отработанное время, руб." },
                { "Надбавка, руб.", "Надбавка за стаж работы в бюджетной организации, руб." },
                { "Б/О, руб.", "Начисления по больничному листу (Б) и отпускные (О), руб." },
                { "Вычет, руб.", "Применённый стандартный налоговый вычет (льготы по ст. 209 НК РБ), руб." },
                { "НДФЛ, руб.", "Подоходный налог 13% с учётом налоговых льгот, руб." },
                { "ФСЗН, руб.", "Обязательный страховой взнос в ФСЗН (1%), руб." },
                { "Профс., руб.", "Профсоюзный взнос (1%), руб." },
                { "1-я пол., руб.", "Выплата за 1-ю половину месяца (аванс в середине месяца), руб." },
                { "2-я пол., руб.", "Выплата за 2-ю половину месяца (окончательный расчёт в конце месяца), руб." },
                { "К выплате, руб.", "Итоговая чистая зарплата за месяц (1-я половина + 2-я половина), руб." }
            };

            string[] centerCols =
            {
                "Ставка", "Дни, дн.", "Часы, ч.", "Б / О, дн.",
                "Дни (1-я пол.), дн.", "Часы (1-я пол.), ч.", "Этап выплаты"
            };

            Action<DataTable, int> applyTableFormatting = (DataTable dt, int mode) =>
            {
                dgv.DataSource = dt;
                if (dgv.Columns["EmployeeID"] != null) dgv.Columns["EmployeeID"].Visible = false;

                foreach (DataGridViewColumn col in dgv.Columns)
                {
                    string tip;
                    if (headerTooltips.TryGetValue(col.Name, out tip))
                        col.ToolTipText = tip;

                    if (col.Name == "ФИО")
                    {
                        col.FillWeight = mode == 1 ? 180 : 155;
                    }
                    else if (centerCols.Contains(col.Name))
                    {
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        if (col.Name == "Ставка")
                        {
                            col.DefaultCellStyle.Format = "0.##";
                            col.FillWeight = 52;
                        }
                        else if (col.Name == "Часы (1-я пол.), ч.")
                        {
                            col.DefaultCellStyle.Format = "0.#";
                            col.FillWeight = 90;
                        }
                        else if (col.Name == "Этап выплаты")
                        {
                            col.FillWeight = 135;
                        }
                        else
                        {
                            col.FillWeight = col.Name == "Часы, ч." ? 78 : 62;
                        }
                    }
                    else
                    {
                        col.DefaultCellStyle.Format = "N2";
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                        col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
                        col.FillWeight = 76;
                    }
                }

                string mainNetCol = mode == 1 ? "Аванс (1-я пол.), руб." : "К выплате, руб.";
                if (dgv.Columns[mainNetCol] != null)
                {
                    dgv.Columns[mainNetCol].FillWeight = mode == 1 ? 115 : 88;
                    dgv.Columns[mainNetCol].DefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                    dgv.Columns[mainNetCol].DefaultCellStyle.ForeColor = UiTheme.PrimaryGreen;
                    dgv.Columns[mainNetCol].DefaultCellStyle.SelectionForeColor = UiTheme.PrimaryGreen;
                }

                if (mode == 1)
                {
                    decimal sumAdv = 0m;
                    foreach (DataRow row in dt.Rows)
                        sumAdv += Convert.ToDecimal(row["Аванс (1-я пол.), руб."]);

                    lblTotals.Text =
                        $"Сотрудников: {dt.Rows.Count}  |  " +
                        $"Этап: 1-я половина месяца (середина месяца)  |  " +
                        $"Итого аванс к выплате: {sumAdv:N2} руб.  |  " +
                        "Окончательный расчёт и удержания — в конце месяца";

                    btnSave.Enabled = dt.Rows.Count > 0;
                    btnExport.Enabled = dt.Rows.Count > 0;
                    btnExportOne.Enabled = dt.Rows.Count > 0;
                }
                else
                {
                    decimal sumGross = 0m;
                    decimal sumDed = 0m;
                    decimal sumHalf1 = 0m;
                    decimal sumHalf2 = 0m;
                    decimal sumNet = 0m;

                    foreach (DataRow row in dt.Rows)
                    {
                        decimal b = Convert.ToDecimal(row["Оклад, руб."]);
                        decimal bon = Convert.ToDecimal(row["Надбавка, руб."]);
                        decimal bo = dt.Columns.Contains("Б/О, руб.") ? Convert.ToDecimal(row["Б/О, руб."]) : 0m;
                        decimal h1 = dt.Columns.Contains("1-я пол., руб.") ? Convert.ToDecimal(row["1-я пол., руб."]) : 0m;
                        decimal h2 = dt.Columns.Contains("2-я пол., руб.") ? Convert.ToDecimal(row["2-я пол., руб."]) : 0m;
                        decimal net = Convert.ToDecimal(row["К выплате, руб."]);

                        decimal g = b + bon + bo;
                        sumGross += g;
                        sumDed += (g - net);
                        sumHalf1 += h1;
                        sumHalf2 += h2;
                        sumNet += net;
                    }

                    lblTotals.Text =
                        $"Сотрудников: {dt.Rows.Count}  |  " +
                        $"Начислено: {sumGross:N2} р.  |  " +
                        $"Удержано: {sumDed:N2} р.  |  " +
                        $"1-я пол.: {sumHalf1:N2} р.  |  " +
                        $"2-я пол.: {sumHalf2:N2} р.  |  " +
                        $"Итого к выплате: {sumNet:N2} р.";

                    btnSave.Enabled = dt.Rows.Count > 0;
                    btnExport.Enabled = dt.Rows.Count > 0;
                    btnExportOne.Enabled = dt.Rows.Count > 0;
                    if (dgv.Columns["К выплате, руб."] != null)
                    {
                        dgv.Columns["К выплате, руб."].Visible = (mode != 2);
                        if (mode != 2) dgv.Columns["К выплате, руб."].DisplayIndex = dgv.Columns.Count - 1;
                    }
                    dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                    dgv.PerformLayout();
                }
            };

            bool suppressEvents = false;

            Action<bool> executeCalculation = (bool fromImport) =>
            {
                int m = cmbMonth.SelectedIndex + 1;
                int y = (int)nudYear.Value;
                int mode = cmbHalf.SelectedIndex == 0 ? 1 : (cmbHalf.SelectedIndex == 2 ? 3 : 2);

                // Проверка календарной даты (нельзя считать будущий месяц или весь месяц 1-го числа)
                DateTime today = DateTime.Today;
                DateTime periodFirst = new DateTime(y, m, 1);
                DateTime currentFirst = new DateTime(today.Year, today.Month, 1);

                if (periodFirst > currentFirst)
                {
                    MessageBox.Show(
                        $"Месяц «{_months[m - 1]} {y} г.» ещё не наступил (сегодня {today:dd.MM.yyyy}).\n" +
                        "Расчёт заработной платы за будущий период невозможен.",
                        "Период ещё не наступил",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (y == today.Year && m == today.Month)
                {
                    if (today.Day < 15 && !fromImport)
                    {
                        MessageBox.Show(
                            $"Сегодня {today:dd.MM.yyyy} — первая половина месяца «{_months[m - 1]}» ещё не завершена.\n\n" +
                            "Зарплата рассчитывается и выплачивается в два этапа:\n" +
                            "• 1-я половина (аванс) — в середине месяца (с 15-го числа);\n" +
                            "• 2-я половина (окончательный расчёт) — в конце месяца (с 25-го числа).",
                            "Рано выполнять расчёт",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    if (today.Day < 25 && (mode == 2 || mode == 3) && !fromImport)
                    {
                        string noText = mode == 3
                            ? "• Нажмите «Нет», чтобы выполнить предварительный расчёт за весь месяц по текущему табелю;\n"
                            : "• Нажмите «Нет», чтобы выполнить расчёт за 2-ю половину по текущему табелю;\n";
                        DialogResult ans = MessageBox.Show(
                            $"Месяц «{_months[m - 1]} {y} г.» ещё не завершён (сегодня {today:dd.MM.yyyy}).\n" +
                            "В середине месяца выплачивается 1-я половина (аванс), а окончательный расчёт за 2-ю половину выполняется с 25-го числа.\n\n" +
                            "• Нажмите «Да», чтобы рассчитать 1-ю половину (аванс);\n" +
                            noText +
                            "• Нажмите «Отмена» для отмены.",
                            "Выбор этапа расчёта",
                            MessageBoxButtons.YesNoCancel,
                            MessageBoxIcon.Question);

                        if (ans == DialogResult.Cancel) return;
                        if (ans == DialogResult.Yes)
                        {
                            suppressEvents = true;
                            cmbHalf.SelectedIndex = 0;
                            suppressEvents = false;
                            mode = 1;
                        }
                    }
                }

                try
                {
                    btnCalculate.Text = "Расчёт...";
                    btnCalculate.Enabled = false;

                    _selectedMonth = m;
                    _selectedYear = y;
                    _selectedHalfMode = mode;

                    var request = new NetworkMessage
                    {
                        Command = "CALCULATE_PAYROLL",
                        Data = $"{m}|{y}|{mode}"
                    };
                    var response = new ServerConnection().SendRequest(request);

                    if (response.IsSuccess)
                    {
                        DataTable dt = JsonConvert.DeserializeObject<DataTable>(response.Data);
                        _payrollCache = dt;
                        _payrollCacheMonth = m;
                        _payrollCacheYear = y;
                        _payrollCacheHalfMode = mode;
                        applyTableFormatting(dt, mode);
                    }
                    else
                    {
                        MessageBox.Show(response.ErrorMessage, "Ошибка расчёта",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
                finally { btnCalculate.Text = "Рассчитать"; btnCalculate.Enabled = true; }
            };

            Action onSelectorsChanged = () =>
            {
                _selectedMonth = cmbMonth.SelectedIndex + 1;
                _selectedYear = (int)nudYear.Value;
                _selectedHalfMode = cmbHalf.SelectedIndex == 0 ? 1 : (cmbHalf.SelectedIndex == 2 ? 3 : 2);

                if (_payrollCache != null
                    && _payrollCacheMonth == _selectedMonth
                    && _payrollCacheYear == _selectedYear
                    && (_payrollCacheHalfMode == _selectedHalfMode || (_payrollCacheHalfMode >= 2 && _selectedHalfMode >= 2)))
                {
                    applyTableFormatting(_payrollCache, _selectedHalfMode);
                }
                else
                {
                    dgv.DataSource = null;
                    btnSave.Enabled = false;
                    btnExport.Enabled = false;
                    string modeLabel = _selectedHalfMode == 1 ? "1-я половина (аванс)" : "Весь месяц (1-я + 2-я пол.)";
                    lblTotals.Text = $"Период: {_months[_selectedMonth - 1]} {_selectedYear} г. ({modeLabel})  |  Нажмите «Рассчитать».";
                }
            };

            cmbMonth.SelectedIndexChanged += (s, e) => { if (!suppressEvents) onSelectorsChanged(); };
            nudYear.ValueChanged += (s, e) => { if (!suppressEvents) onSelectorsChanged(); };
            cmbHalf.SelectedIndexChanged += (s, e) => { if (!suppressEvents) onSelectorsChanged(); };

            if (_payrollCache != null
                && _payrollCacheMonth == _selectedMonth
                && _payrollCacheYear == _selectedYear
                && _payrollCacheHalfMode == _selectedHalfMode)
            {
                applyTableFormatting(_payrollCache, _selectedHalfMode);
            }

            btnImportTs.Click += (s, e) =>
            {
                int m = cmbMonth.SelectedIndex + 1;
                int y = (int)nudYear.Value;
                if (RunExcelTimesheetImport(ref m, ref y))
                {
                    suppressEvents = true;
                    cmbMonth.SelectedIndex = m - 1;
                    if (y >= (int)nudYear.Minimum && y <= (int)nudYear.Maximum) nudYear.Value = y;
                    cmbHalf.SelectedIndex = 1; // После импорта полного табеля показываем расчёт с обеими половинами
                    suppressEvents = false;

                    // Если импортирован табель за уже наступивший месяц — сразу рассчитываем
                    if (new DateTime(y, m, 1) <= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))
                        executeCalculation(true);
                    else
                        onSelectorsChanged();
                }
            };

            btnCalculate.Click += (s, e) => executeCalculation(false);

            btnSave.Click += (s, e) =>
            {
                try
                {
                    DataTable currentTable = dgv.DataSource as DataTable;
                    if (currentTable == null || currentTable.Rows.Count == 0) return;

                    int selectedMonth = cmbMonth.SelectedIndex + 1;
                    int selectedYear = (int)nudYear.Value;
                    string jsonData = JsonConvert.SerializeObject(currentTable);
                    var request = new NetworkMessage
                    {
                        Command = "SAVE_PAYROLL",
                        Data = $"{selectedMonth}|{selectedYear}|{jsonData}"
                    };
                    var response = new ServerConnection().SendRequest(request);

                    if (response.IsSuccess)
                        MessageBox.Show(response.Data, "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show("Ошибка: " + response.ErrorMessage, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
            };

            btnExportOne.Click += (s, e) =>
            {
                DataGridViewRow sel = dgv.CurrentRow ?? (dgv.SelectedRows.Count > 0 ? dgv.SelectedRows[0] : null);
                if (sel == null)
                {
                    MessageBox.Show("Выберите строку сотрудника в таблице!", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string empName = sel.Cells["ФИО"] != null && sel.Cells["ФИО"].Value != null ? sel.Cells["ФИО"].Value.ToString() : "Сотрудник";
                int y = (int)nudYear.Value;
                int mode = cmbHalf.SelectedIndex == 0 ? 1 : 2;
                string prefix = mode == 1 ? "Листок_аванс" : "Расчетный_листок";

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Excel Workbook (*.xlsx)|*.xlsx";
                    sfd.FileName = $"{prefix}_{empName}_{cmbMonth.Text}_{y}.xlsx";
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            using (var wb = new XLWorkbook())
                            {
                                var ws = wb.Worksheets.Add("Расчётный листок");
                                Func<string, decimal> getVal = (colName) =>
                                {
                                    if (dgv.Columns[colName] == null) return 0m;
                                    object v = sel.Cells[dgv.Columns[colName].Index].Value;
                                    decimal d;
                                    return (v != null && decimal.TryParse(v.ToString(), out d)) ? d : 0m;
                                };
                                Func<string, string> getStr = (colName) =>
                                    dgv.Columns[colName] != null && sel.Cells[dgv.Columns[colName].Index].Value != null
                                        ? sel.Cells[dgv.Columns[colName].Index].Value.ToString() : "—";

                                decimal oklad = getVal("Оклад, руб.");
                                decimal nadb = getVal("Надбавка, руб.");
                                decimal bo = getVal("Б/О, руб.");
                                decimal gross = oklad + nadb + bo;
                                decimal vychet = getVal("Вычет, руб.");
                                decimal ndfl = getVal("НДФЛ, руб.");
                                decimal fszn = getVal("ФСЗН, руб.");
                                decimal prof = getVal("Профс., руб.");
                                decimal avans = getVal("1-я пол., руб.");
                                if (avans == 0m) avans = getVal("Аванс (1-я пол.), руб.");
                                decimal doplat = getVal("2-я пол., руб.");
                                decimal itogo = getVal("К выплате, руб.");
                                if (itogo == 0m) itogo = avans + doplat;
                                decimal totalDed = avans + fszn + ndfl + prof;

                                string modeTitle = cmbHalf.SelectedIndex == 0 ? "1-я половина (аванс)" : (cmbHalf.SelectedIndex == 1 ? "2-я половина (окончательно)" : "Весь месяц");
                                ws.Cell(1, 1).Value = $"Расчетный листок — {modeTitle}";
                                ws.Range(1, 1, 1, 6).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                ws.Cell(1, 1).Style.Font.Bold = true;
                                ws.Cell(1, 1).Style.Font.FontSize = 14;

                                ws.Cell(2, 1).Value = $"{empName}   |   {cmbMonth.Text} {y} г.";
                                ws.Range(2, 1, 2, 6).Merge().Style.Font.Bold = true;

                                ws.Cell(3, 1).Value = "УО «Полесский государственный университет»";
                                ws.Range(3, 1, 3, 6).Merge();

                                ws.Cell(4, 1).Value = $"Ставка: {getStr("Ставка")}   |   Дни: {getStr("Дни, дн.")}   |   Часы: {getStr("Часы, ч.")}";
                                ws.Range(4, 1, 4, 6).Merge().Style.Font.Italic = true;

                                ws.Cell(6, 1).Value = "Начислено";
                                ws.Range(6, 1, 6, 3).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                ws.Cell(6, 4).Value = "Удержано";
                                ws.Range(6, 4, 6, 6).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                ws.Range(6, 1, 6, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D4F");
                                ws.Range(6, 1, 6, 6).Style.Font.FontColor = XLColor.White;
                                ws.Range(6, 1, 6, 6).Style.Font.Bold = true;

                                string[] hdrs = { "Мес", "Вид оплаты", "Сумма, руб", "Мес", "Вид удержания", "Сумма, руб" };
                                for (int c = 0; c < 6; c++) ws.Cell(7, c + 1).Value = hdrs[c];
                                ws.Range(7, 1, 7, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF4EE");
                                ws.Range(7, 1, 7, 6).Style.Font.Bold = true;

                                int halfIdx = cmbHalf.SelectedIndex;
                                var accr = halfIdx == 0
                                    ? new[] { ("Аванс (1-я половина)", avans) }
                                    : (halfIdx == 1
                                        ? new[] { ("Начислено за 2-ю половину (за вычетом аванса)", Math.Max(0m, gross - avans)) }
                                        : new[] { ("Оклад", oklad), ("Надбавка", nadb), ("Больничный / Отпуск (Б/О)", bo) });
                                var deds = halfIdx == 0
                                    ? new (string, decimal)[0]
                                    : new[] { ("Пенсионный взнос (ФСЗН 1%)", fszn), ("Подоходный налог (13%)", ndfl), ("Профсоюзный взнос (1%)", prof) };
                                int maxRows = Math.Max(accr.Length, deds.Length);
                                for (int i = 0; i < maxRows; i++)
                                {
                                    int r = 8 + i;
                                    if (i < accr.Length)
                                    {
                                        ws.Cell(r, 1).Value = cmbMonth.SelectedIndex + 1;
                                        ws.Cell(r, 2).Value = accr[i].Item1;
                                        ws.Cell(r, 3).Value = accr[i].Item2;
                                        ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                                    }
                                    if (i < deds.Length)
                                    {
                                        ws.Cell(r, 4).Value = cmbMonth.SelectedIndex + 1;
                                        ws.Cell(r, 5).Value = deds[i].Item1;
                                        ws.Cell(r, 6).Value = deds[i].Item2;
                                        ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";
                                    }
                                }

                                decimal dispGross = halfIdx == 0 ? avans : gross;
                                decimal dispDed = halfIdx == 0 ? 0m : (halfIdx == 1 ? (avans + fszn + ndfl + prof) : (fszn + ndfl + prof));
                                int itogRow = 8 + maxRows;
                                ws.Cell(itogRow, 2).Value = "Итого начислено";
                                ws.Cell(itogRow, 3).Value = dispGross;
                                ws.Cell(itogRow, 3).Style.NumberFormat.Format = "#,##0.00";
                                ws.Cell(itogRow, 5).Value = "Итого удержано";
                                ws.Cell(itogRow, 6).Value = dispDed;
                                ws.Cell(itogRow, 6).Style.NumberFormat.Format = "#,##0.00";
                                ws.Range(itogRow, 1, itogRow, 6).Style.Font.Bold = true;

                                int payRow = itogRow + 1;
                                if (halfIdx == 0)
                                {
                                    ws.Cell(payRow, 2).Value = "К выплате (1-я половина)";
                                    ws.Cell(payRow, 3).Value = avans;
                                    ws.Cell(payRow, 3).Style.NumberFormat.Format = "#,##0.00";
                                }
                                else if (halfIdx == 1)
                                {
                                    ws.Cell(payRow, 2).Value = "К выплате (2-я половина)";
                                    ws.Cell(payRow, 3).Value = doplat;
                                    ws.Cell(payRow, 3).Style.NumberFormat.Format = "#,##0.00";
                                }
                                else
                                {
                                    ws.Cell(payRow, 2).Value = "Итого выплат за месяц";
                                    ws.Cell(payRow, 3).Value = itogo;
                                    ws.Cell(payRow, 3).Style.NumberFormat.Format = "#,##0.00";
                                }
                                ws.Cell(payRow, 6).Style.NumberFormat.Format = "#,##0.00";
                                ws.Range(payRow, 1, payRow, 6).Style.Font.Bold = true;

                                ws.Range(6, 1, payRow, 6).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                                ws.Range(6, 1, payRow, 6).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                                int taxRow = payRow + 2;
                                ws.Cell(taxRow, 1).Value = $"Для расчета подоходного налога за {cmbMonth.Text} {y}:";
                                ws.Range(taxRow, 1, taxRow, 4).Merge().Style.Font.Bold = true;
                                ws.Cell(taxRow + 1, 1).Value = "Начислено для подоходного"; ws.Cell(taxRow + 1, 3).Value = gross;
                                ws.Cell(taxRow + 2, 1).Value = "Вычеты (налоговые льготы)"; ws.Cell(taxRow + 2, 3).Value = vychet;
                                ws.Cell(taxRow + 3, 1).Value = "Облагаемая сумма"; ws.Cell(taxRow + 3, 3).Value = Math.Max(0m, gross - vychet);
                                ws.Cell(taxRow + 4, 1).Value = "Удержано подоходного налога"; ws.Cell(taxRow + 4, 3).Value = ndfl;
                                ws.Range(taxRow + 1, 3, taxRow + 4, 3).Style.NumberFormat.Format = "#,##0.00";

                                ws.Columns().AdjustToContents();
                                wb.SaveAs(sfd.FileName);
                            }
                            MessageBox.Show("Расчётный листок сотрудника сохранён!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Ошибка экспорта: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            };

            btnExport.Click += (s, e) =>
            {
                int y = (int)nudYear.Value;
                int mode = cmbHalf.SelectedIndex == 0 ? 1 : 2;
                string prefix = mode == 1 ? "Аванс" : "Ведомость";

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Excel Workbook (*.xlsx)|*.xlsx";
                    sfd.FileName = $"{prefix}_{cmbMonth.Text}_{y}.xlsx";

                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            DataTable dt = dgv.DataSource as DataTable;
                            if (dt == null) return;

                            DataTable exportDt = dt.Copy();
                            if (exportDt.Columns.Contains("EmployeeID")) exportDt.Columns.Remove("EmployeeID");

                            using (var workbook = new XLWorkbook())
                            {
                                string sheetTitle = mode == 1 ? "Аванс (1-я пол.)" : "Расчёт зарплаты";
                                IXLWorksheet worksheet = workbook.Worksheets.Add(exportDt, sheetTitle);
                                IXLRange headerRange = worksheet.Range(1, 1, 1, exportDt.Columns.Count);
                                headerRange.Style.Font.Bold = true;
                                headerRange.Style.Font.FontColor = XLColor.White;
                                headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(24, 119, 64);

                                int totalRow = exportDt.Rows.Count + 2;
                                worksheet.Cell(totalRow, 1).Value = "ИТОГО:";
                                worksheet.Cell(totalRow, 1).Style.Font.Bold = true;

                                for (int c = 0; c < exportDt.Columns.Count; c++)
                                {
                                    string colName = exportDt.Columns[c].ColumnName;
                                    if (colName.EndsWith("руб."))
                                    {
                                        decimal colSum = 0m;
                                        foreach (DataRow r in exportDt.Rows)
                                            colSum += Convert.ToDecimal(r[c]);
                                        worksheet.Cell(totalRow, c + 1).Value = Math.Round(colSum, 2);
                                        worksheet.Cell(totalRow, c + 1).Style.Font.Bold = true;
                                        worksheet.Column(c + 1).Style.NumberFormat.Format = "#,##0.00";
                                    }
                                }

                                IXLRange totalRange = worksheet.Range(totalRow, 1, totalRow, exportDt.Columns.Count);
                                totalRange.Style.Fill.BackgroundColor = XLColor.FromArgb(232, 246, 237);

                                worksheet.Columns().AdjustToContents();
                                workbook.SaveAs(sfd.FileName);
                            }

                            MessageBox.Show("Excel-ведомость успешно сохранена!", "Успех",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
                    }
                }
            };
        }

        // =========================================================================
        // 5. ЭКРАН «ГРАФИКИ» (СПОКОЙНЫЕ ОТТЕНКИ, ОБЕ ПОДПИСИ ФОТ, ПЕЧАТЬ ОТЧЁТА)
        // =========================================================================
        private void LoadChartsScreen(bool append = false)
        {
            if (!append)
            {
                _contentPanel.Controls.Clear();
                UiTheme.CreatePageHeader(
                    _contentPanel,
                    "Графики и аналитика",
                    "Фонд оплаты труда по кафедрам, структура удержаний, годовая динамика и отработка часов");
            }
            _contentPanel.AutoScroll = true;

            Panel toolbar = CreateToolbarCard();
            if (append) toolbar.Top = 430;

            ComboBox cmbMonths;
            NumericUpDown nudYear;
            AddPeriodSelectors(toolbar, out cmbMonths, out nudYear);

            Button btnRefresh = UiTheme.CreatePrimaryButton("Обновить графики", 322, 8, 160);
            Button btnPrint = UiTheme.CreateOutlineButton("Печать графиков", 492, 8, 158);
            toolbar.Controls.Add(btnRefresh);
            toolbar.Controls.Add(btnPrint);

            Label lblFooter;
            Panel footer = UiTheme.CreateSummaryFooter(_contentPanel, ContentMargin, out lblFooter);

            int gridTop = 136;
            int gridHeight = footer.Top - gridTop - 8;
            int innerWidth = _contentPanel.ClientSize.Width - (ContentMargin * 2) + 12;

            TableLayoutPanel chartsGrid = new TableLayoutPanel
            {
                Location = new Point(ContentMargin - 6, gridTop),
                Size = new Size(innerWidth, gridHeight),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            UiTheme.EnableDoubleBuffering(chartsGrid);
            chartsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            chartsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            chartsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            chartsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            if (append)
            {
                toolbar.Top = 375;
                chartsGrid.Top = 435;
                chartsGrid.Height = 620;
                chartsGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            }
            _contentPanel.Controls.Add(chartsGrid);

            string title1 = "1. Фонд оплаты труда по кафедрам, руб.";
            string title2 = "2. Структура фонда оплаты труда";
            string title3 = "3. Динамика фонда зарплаты за год, руб.";
            string title4 = "4. Отработка часов (Факт / Норма), ч.";

            Label lblSub1, lblSub2, lblSub3, lblSub4;
            Chart chartDept = UiTheme.CreateChartCard(chartsGrid, title1, out lblSub1);
            Chart chartStructure = UiTheme.CreateChartCard(chartsGrid, title2, out lblSub2);
            Chart chartTrend = UiTheme.CreateChartCard(chartsGrid, title3, out lblSub3);
            Chart chartHours = UiTheme.CreateChartCard(chartsGrid, title4, out lblSub4);

            chartsGrid.SetCellPosition(chartDept.Parent, new TableLayoutPanelCellPosition(0, 0));
            chartsGrid.SetCellPosition(chartStructure.Parent, new TableLayoutPanelCellPosition(1, 0));
            chartsGrid.SetCellPosition(chartTrend.Parent, new TableLayoutPanelCellPosition(0, 1));
            chartsGrid.SetCellPosition(chartHours.Parent, new TableLayoutPanelCellPosition(1, 1));

            Action updateCharts = () =>
            {
                try
                {
                    int m = cmbMonths.SelectedIndex + 1;
                    int y = (int)nudYear.Value;
                    _selectedMonth = m;
                    _selectedYear = y;

                    var req = new NetworkMessage { Command = "GET_ANALYTICS_FULL", Data = $"{m}|{y}" };
                    var res = new ServerConnection().SendRequest(req);

                    if (!res.IsSuccess)
                    {
                        MessageBox.Show("Ошибка загрузки аналитики: " + res.ErrorMessage,
                            "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    AnalyticsDataDto data = JsonConvert.DeserializeObject<AnalyticsDataDto>(res.Data);
                    if (data == null) return;

                    string periodLabel = $"{_months[m - 1]} {y} г.";
                    lblSub1.Text = periodLabel;
                    lblSub3.Text = $"{y} год";
                    lblSub4.Text = periodLabel;

                    RenderDepartmentChart(chartDept, data.DepartmentPayroll);
                    RenderStructureChart(chartStructure, data, lblSub2);
                    RenderMonthlyTrendChart(chartTrend, data.MonthlyTrend, m);
                    RenderEmployeeHoursChart(chartHours, data.EmployeeHours);

                    decimal totalGross = data.TotalBaseSalary + data.TotalBonus + data.TotalSickVacationPay;
                    decimal totalDed = data.TotalTax + data.TotalFszen + data.TotalUnion;
                    decimal totalNet = data.TotalNetSalary;

                    if (data.PaidEmployeesCount > 0)
                    {
                        decimal avgNet = Math.Round(totalNet / data.PaidEmployeesCount, 2);
                        int deptCnt = data.DepartmentPayroll != null ? data.DepartmentPayroll.Rows.Count : 0;
                        lblFooter.Text =
                            $"Период: {periodLabel}  |  " +
                            $"Кафедр: {deptCnt}  |  " +
                            $"Начислено: {totalGross:N2} р.  |  " +
                            $"Удержано: {totalDed:N2} р.  |  " +
                            $"К выплате: {totalNet:N2} р.  |  " +
                            $"Средняя: {avgNet:N2} р.";
                    }
                    else
                    {
                        int empCnt = data.EmployeeHours != null ? data.EmployeeHours.Rows.Count : 0;
                        decimal sumH = 0m;
                        decimal sumNH = 0m;
                        if (data.EmployeeHours != null)
                        {
                            foreach (DataRow r in data.EmployeeHours.Rows)
                            {
                                sumH += Convert.ToDecimal(r["ЧасыФакт"]);
                                sumNH += Convert.ToDecimal(r["ЧасыНорма"]);
                            }
                        }
                        lblFooter.Text =
                            $"Период: {periodLabel}  |  " +
                            $"Сотрудников: {empCnt}  |  " +
                            $"Часы по табелю: {sumH:0.#}/{sumNH:0.#} ч.  |  " +
                            "Расчёт зарплаты за этот месяц ещё не сохранён в БД";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка сети: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            btnPrint.Click += (s, e) =>
            {
                string period = $"{_months[_selectedMonth - 1]} {_selectedYear} г.";
                PrintChartsReport(
                    period,
                    lblFooter.Text,
                    new[] { chartDept, chartStructure, chartTrend, chartHours },
                    new[] { title1, title2, title3, title4 },
                    new[] { lblSub1.Text, lblSub2.Text, lblSub3.Text, lblSub4.Text });
            };

            cmbMonths.SelectedIndexChanged += (s, e) => updateCharts();
            nudYear.ValueChanged += (s, e) => updateCharts();
            btnRefresh.Click += (s, e) => updateCharts();

            updateCharts();
        }

        // Печать всех 4 графиков как единого аналитического отчёта (альбомный лист A4 / экспорт в PDF)
        private void PrintChartsReport(
            string periodLabel,
            string summaryLine,
            Chart[] charts,
            string[] titles,
            string[] subtitles)
        {
            try
            {
                using (PrintDocument doc = new PrintDocument())
                {
                    doc.DocumentName = $"Графики_ФОТ_ПолесГУ_{periodLabel}";
                    doc.DefaultPageSettings.Landscape = true;
                    // Явная квалификация: System.Drawing.Printing.Margins не должен конфликтовать
                    // с System.Windows.Forms.DataVisualization.Charting.Margins (CS0104)
                    doc.DefaultPageSettings.Margins =
                        new System.Drawing.Printing.Margins(36, 36, 32, 32);

                    doc.PrintPage += (s, e) =>
                    {
                        Graphics g = e.Graphics;
                        Rectangle bounds = e.MarginBounds;

                        using (Font orgFont = new Font("Segoe UI", 9f, FontStyle.Regular))
                        using (Font reportTitleFont = new Font("Segoe UI", 13.5f, FontStyle.Bold))
                        using (Font metaFont = new Font("Segoe UI", 8.75f, FontStyle.Regular))
                        using (Font cardTitleFont = new Font("Segoe UI", 9.25f, FontStyle.Bold))
                        using (Font cardSubFont = new Font("Segoe UI", 8f, FontStyle.Regular))
                        using (Font footerFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                        using (Brush primaryBrush = new SolidBrush(UiTheme.TextPrimary))
                        using (Brush mutedBrush = new SolidBrush(UiTheme.TextSecondary))
                        using (Brush accentBrush = new SolidBrush(UiTheme.ChartPrimary))
                        using (Brush footerBg = new SolidBrush(Color.FromArgb(244, 248, 245)))
                        using (Pen borderPen = new Pen(UiTheme.BorderColor, 1f))
                        using (Pen accentPen = new Pen(UiTheme.ChartPrimary, 2f))
                        {
                            // 1. Шапка печатного отчёта
                            g.DrawString("Учреждение образования «Полесский государственный университет»",
                                orgFont, mutedBrush, bounds.Left, bounds.Top);
                            g.DrawString("АНАЛИТИЧЕСКИЙ ОТЧЁТ ПО ГРАФИКАМ ФОНДА ОПЛАТЫ ТРУДА",
                                reportTitleFont, accentBrush, bounds.Left, bounds.Top + 17);

                            string metaRight = $"Период: {periodLabel}\nДата печати: {DateTime.Now:dd.MM.yyyy HH:mm}";
                            StringFormat rightFmt = new StringFormat { Alignment = StringAlignment.Far };
                            g.DrawString(metaRight, metaFont, mutedBrush,
                                new RectangleF(bounds.Right - 260, bounds.Top + 2, 260, 38), rightFmt);

                            int lineY = bounds.Top + 48;
                            g.DrawLine(accentPen, bounds.Left, lineY, bounds.Right, lineY);

                            // 2. Сетка 2x2 из 4 графиков
                            int footerAreaHeight = 62;
                            int gridTop = lineY + 10;
                            int gridHeight = bounds.Bottom - footerAreaHeight - gridTop;
                            int gap = 12;
                            int cellW = (bounds.Width - gap) / 2;
                            int cellH = (gridHeight - gap) / 2;

                            StringFormat centerFmt = new StringFormat
                            {
                                Alignment = StringAlignment.Center,
                                LineAlignment = StringAlignment.Center
                            };

                            for (int i = 0; i < 4; i++)
                            {
                                int col = i % 2;
                                int row = i / 2;
                                int cx = bounds.Left + col * (cellW + gap);
                                int cy = gridTop + row * (cellH + gap);

                                Rectangle cardRect = new Rectangle(cx, cy, cellW, cellH);
                                g.FillRectangle(Brushes.White, cardRect);
                                g.DrawRectangle(borderPen, cardRect);
                                g.FillRectangle(accentBrush, cx, cy, cellW, 3);

                                g.DrawString(titles[i], cardTitleFont, primaryBrush, cx + 10, cy + 7);
                                if (!string.IsNullOrEmpty(subtitles[i]))
                                {
                                    g.DrawString(subtitles[i], cardSubFont, mutedBrush,
                                        new RectangleF(cx + cellW - 150, cy + 8, 140, 18), rightFmt);
                                }

                                Rectangle chartRect = new Rectangle(cx + 6, cy + 28, cellW - 12, cellH - 34);
                                if (charts[i].Series.Count > 0 && charts[i].ChartAreas[0].Visible)
                                {
                                    charts[i].Printing.PrintPaint(g, chartRect);
                                }
                                else
                                {
                                    g.DrawString("Нет сохранённых данных за выбранный период",
                                        metaFont, mutedBrush, chartRect, centerFmt);
                                }
                            }

                            // 3. Нижняя сводка и строка подписи
                            Rectangle sumRect = new Rectangle(bounds.Left, bounds.Bottom - 52, bounds.Width, 28);
                            g.FillRectangle(footerBg, sumRect);
                            g.DrawRectangle(borderPen, sumRect);
                            g.FillRectangle(accentBrush, sumRect.Left, sumRect.Top, 4, sumRect.Height);
                            g.DrawString(summaryLine, footerFont, primaryBrush, sumRect.Left + 12, sumRect.Top + 6);

                            g.DrawString("Ответственный (бухгалтер): _______________________ / _______________________ /",
                                metaFont, mutedBrush, bounds.Left, bounds.Bottom - 17);

                            rightFmt.Dispose();
                            centerFmt.Dispose();
                        }

                        e.HasMorePages = false;
                    };

                    using (PrintPreviewDialog preview = new PrintPreviewDialog())
                    {
                        preview.Document = doc;
                        preview.Text = $"Предварительный просмотр отчёта — {periodLabel}";
                        preview.StartPosition = FormStartPosition.CenterParent;
                        preview.Width = 1150;
                        preview.Height = 760;
                        preview.PrintPreviewControl.Zoom = 0.85;
                        preview.ShowDialog(this);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось подготовить отчёт к печати. Проверьте наличие принтера (например, Microsoft Print to PDF).\n" + ex.Message,
                    "Ошибка печати", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // График 1: Горизонтальная диаграмма ФОТ по кафедрам (подписи сумм у ОБЕИХ полос!)
        private void RenderDepartmentChart(Chart chart, DataTable dt)
        {
            chart.Series.Clear();
            chart.Titles.Clear();
            ChartArea area = chart.ChartAreas[0];

            if (dt == null || dt.Rows.Count == 0)
            {
                SetEmptyChartState(chart, "Нет сохранённых расчётов за выбранный месяц.\nСохраните расчёт во вкладке «Расчет зарплаты».");
                return;
            }

            area.Visible = true;
            chart.Legends[0].Enabled = true;
            chart.Legends[0].Docking = Docking.Bottom;

            area.AxisX.MajorGrid.Enabled = false;
            area.AxisX.Interval = 1;
            area.AxisY.MajorGrid.Enabled = true;
            area.AxisY.MajorGrid.LineColor = UiTheme.GridLineColor;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisY.Minimum = 0;
            area.AxisY.LabelStyle.Format = "N0";

            // Включены подписи сумм и у верхней полосы («Начислено»), и у нижней («К выплате»)
            Series sGross = new Series("Начислено")
            {
                ChartType = SeriesChartType.Bar,
                Color = UiTheme.ChartMutedBar,
                BorderColor = UiTheme.ChartMutedBorder,
                BorderWidth = 1,
                IsValueShownAsLabel = true,
                LabelFormat = "N0",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                LabelForeColor = UiTheme.TextSecondary
            };
            sGross["PointWidth"] = "0.68";
            sGross.SmartLabelStyle.Enabled = false;

            Series sNet = new Series("К выплате")
            {
                ChartType = SeriesChartType.Bar,
                Color = UiTheme.ChartPrimary,
                IsValueShownAsLabel = true,
                LabelFormat = "N0",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                LabelForeColor = UiTheme.TextPrimary
            };
            sNet["PointWidth"] = "0.68";
            sNet.SmartLabelStyle.Enabled = false;

            double maxVal = 0.0;
            foreach (DataRow row in dt.Rows)
            {
                string dept = UiTheme.FormatDepartmentName(row["Кафедра"].ToString());
                string shortDept = dept.Length > 26 ? dept.Substring(0, 24) + "…" : dept;
                double gross = Convert.ToDouble(row["Начислено"]);
                double net = Convert.ToDouble(row["КВыплате"]);
                int cnt = Convert.ToInt32(row["Сотрудников"]);
                if (gross > maxVal) maxVal = gross;

                int i1 = sGross.Points.AddXY(shortDept, gross);
                sGross.Points[i1].ToolTip = $"Кафедра: {dept}\nНачислено: {gross:N2} руб. (сотрудников: {cnt})";

                int i2 = sNet.Points.AddXY(shortDept, net);
                sNet.Points[i2].ToolTip = $"Кафедра: {dept}\nК выплате: {net:N2} руб. (сотрудников: {cnt})";
            }

            area.AxisY.Maximum = maxVal > 0 ? Math.Ceiling(maxVal * 1.25) : double.NaN;
            chart.Series.Add(sNet);
            chart.Series.Add(sGross);
        }

        // График 2: Кольцевая диаграмма (Doughnut) в мягких шалфейно-зелёных оттенках
        private void RenderStructureChart(Chart chart, AnalyticsDataDto data, Label lblSub)
        {
            chart.Series.Clear();
            chart.Titles.Clear();
            ChartArea area = chart.ChartAreas[0];

            decimal gross = data.TotalBaseSalary + data.TotalBonus + data.TotalSickVacationPay;
            if (data.PaidEmployeesCount == 0 || gross <= 0m)
            {
                lblSub.Text = "Нет данных";
                SetEmptyChartState(chart, "Структура появится после сохранения\nрасчёта зарплаты за выбранный месяц.");
                return;
            }

            lblSub.Text = $"Всего: {gross:N2} р.";
            area.Visible = true;
            chart.Legends[0].Enabled = true;
            chart.Legends[0].Docking = Docking.Right;
            chart.Legends[0].Alignment = StringAlignment.Center;
            chart.Legends[0].IsTextAutoFit = false;

            Series s = new Series("Структура")
            {
                ChartType = SeriesChartType.Doughnut,
                BorderColor = Color.White,
                BorderWidth = 2
            };
            s["DoughnutRadius"] = "56";
            s["PieLabelStyle"] = "Disabled";

            var slices = new[]
            {
                new { Name = "К выплате", Amount = data.TotalNetSalary, Color = UiTheme.ChartPrimary },
                new { Name = "НДФЛ (13%)", Amount = data.TotalTax, Color = UiTheme.ChartSecondary },
                new { Name = "ФСЗН (1%)", Amount = data.TotalFszen, Color = UiTheme.ChartTertiary },
                new { Name = "Профсоюз (1%)", Amount = data.TotalUnion, Color = UiTheme.ChartQuaternary }
            };

            foreach (var sl in slices)
            {
                if (sl.Amount <= 0m) continue;
                decimal pct = Math.Round((sl.Amount / gross) * 100m, 1);
                int idx = s.Points.AddXY(sl.Name, Convert.ToDouble(sl.Amount));
                DataPoint pt = s.Points[idx];
                pt.Color = sl.Color;
                pt.LegendText = $"{sl.Name}: {sl.Amount:N2} р. ({pct:0.#}%)";
                string extraTip = string.Empty;
                if (sl.Name == "К выплате" && data.TotalSickVacationPay > 0m)
                    extraTip = $"\nВкл. выплаты по Б/О: {data.TotalSickVacationPay:N2} руб.";
                else if (sl.Name == "НДФЛ (13%)" && data.TotalTaxDeduction > 0m)
                    extraTip = $"\nУчтено налоговых льгот: {data.TotalTaxDeduction:N2} руб.";
                pt.ToolTip = $"{sl.Name}\nСумма: {sl.Amount:N2} руб.\nДоля от начисленного: {pct:0.#}%{extraTip}";
            }

            chart.Series.Add(s);
        }

        // График 3: Плавная годовая динамика фонда зарплаты в спокойных тонах
        private void RenderMonthlyTrendChart(Chart chart, DataTable dt, int selectedMonth)
        {
            chart.Series.Clear();
            chart.Titles.Clear();
            ChartArea area = chart.ChartAreas[0];

            double[] grossByMonth = new double[12];
            double[] netByMonth = new double[12];
            double maxVal = 0.0;
            int filledMonths = 0;

            if (dt != null)
            {
                foreach (DataRow row in dt.Rows)
                {
                    int m = Convert.ToInt32(row["Month"]);
                    if (m >= 1 && m <= 12)
                    {
                        double g = Convert.ToDouble(row["Начислено"]);
                        double n = Convert.ToDouble(row["КВыплате"]);
                        grossByMonth[m - 1] = g;
                        netByMonth[m - 1] = n;
                        if (g > maxVal) maxVal = g;
                        if (g > 0 || n > 0) filledMonths++;
                    }
                }
            }

            if (filledMonths == 0)
            {
                SetEmptyChartState(chart, "За выбранный год пока нет сохранённых расчётов.\nСохраните расчёт хотя бы за один месяц.");
                return;
            }

            area.Visible = true;
            chart.Legends[0].Enabled = true;
            chart.Legends[0].Docking = Docking.Bottom;
            area.AxisX.Interval = 1;
            area.AxisX.MajorGrid.Enabled = false;
            area.AxisY.MajorGrid.Enabled = true;
            area.AxisY.MajorGrid.LineColor = UiTheme.GridLineColor;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisY.Minimum = 0;
            area.AxisY.Maximum = Math.Ceiling(maxVal * 1.25);
            area.AxisY.LabelStyle.Format = "N0";

            Series sArea = new Series("К выплате")
            {
                ChartType = SeriesChartType.SplineArea,
                Color = UiTheme.ChartAreaFill,
                BorderColor = UiTheme.ChartPrimary,
                BorderWidth = 2,
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                LabelForeColor = UiTheme.ChartPrimary
            };
            sArea["LineTension"] = "0.35";

            Series sGrossLine = new Series("Начислено")
            {
                ChartType = SeriesChartType.Spline,
                Color = UiTheme.ChartSecondary,
                BorderWidth = 2,
                BorderDashStyle = ChartDashStyle.Dash
            };
            sGrossLine["LineTension"] = "0.35";

            for (int i = 0; i < 12; i++)
            {
                double g = grossByMonth[i];
                double n = netByMonth[i];
                bool hasData = n > 0.0 || g > 0.0;
                bool isCurrent = (i + 1) == selectedMonth;

                int idxNet = sArea.Points.AddXY(_shortMonths[i], n);
                DataPoint pNet = sArea.Points[idxNet];
                if (hasData)
                {
                    pNet.MarkerStyle = MarkerStyle.Circle;
                    pNet.MarkerSize = isCurrent ? 8 : 6;
                    pNet.MarkerColor = isCurrent ? UiTheme.ChartPrimary : Color.White;
                    pNet.MarkerBorderColor = UiTheme.ChartPrimary;
                    pNet.MarkerBorderWidth = 2;
                    pNet.Label = n.ToString("N0");
                    pNet.ToolTip = $"{_months[i]}\nК выплате: {n:N2} руб.\nНачислено: {g:N2} руб.";
                }

                int idxGross = sGrossLine.Points.AddXY(_shortMonths[i], g);
                if (hasData)
                {
                    sGrossLine.Points[idxGross].MarkerStyle = MarkerStyle.Circle;
                    sGrossLine.Points[idxGross].MarkerSize = 5;
                    sGrossLine.Points[idxGross].MarkerColor = UiTheme.ChartSecondary;
                    sGrossLine.Points[idxGross].ToolTip = $"{_months[i]} — Начислено: {g:N2} руб.";
                }
            }

            chart.Series.Add(sArea);
            chart.Series.Add(sGrossLine);
        }

        // График 4: Столбчатая диаграмма отработки часов (спокойные приглушённые оттенки)
        private void RenderEmployeeHoursChart(Chart chart, DataTable dt)
        {
            chart.Series.Clear();
            chart.Titles.Clear();
            ChartArea area = chart.ChartAreas[0];

            if (dt == null || dt.Rows.Count == 0)
            {
                SetEmptyChartState(chart, "В базе пока нет сотрудников для построения графика.");
                return;
            }

            area.Visible = true;
            chart.Legends[0].Enabled = true;
            chart.Legends[0].Docking = Docking.Bottom;
            area.AxisX.Interval = 1;
            area.AxisX.MajorGrid.Enabled = false;
            area.AxisY.MajorGrid.Enabled = true;
            area.AxisY.MajorGrid.LineColor = UiTheme.GridLineColor;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisY.Minimum = 0;
            area.AxisY.LabelStyle.Format = "0";

            Series sNorm = new Series("Норма, ч.")
            {
                ChartType = SeriesChartType.Column,
                Color = UiTheme.ChartMutedBar,
                BorderColor = UiTheme.ChartMutedBorder,
                BorderWidth = 1,
                IsValueShownAsLabel = false
            };
            sNorm["PointWidth"] = "0.58";

            Series sFact = new Series("Отработано, ч.")
            {
                ChartType = SeriesChartType.Column,
                Color = UiTheme.ChartPrimary,
                IsValueShownAsLabel = dt.Rows.Count <= 10,
                LabelFormat = "0.#",
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                LabelForeColor = UiTheme.TextPrimary
            };
            sFact["PointWidth"] = "0.58";

            double maxHours = 0.0;
            int limit = Math.Min(dt.Rows.Count, 12);
            for (int i = 0; i < limit; i++)
            {
                DataRow row = dt.Rows[i];
                string fullName = row["ФИО"].ToString();
                decimal rate = Convert.ToDecimal(row["Ставка"]);
                double factH = Convert.ToDouble(row["ЧасыФакт"]);
                double normH = Convert.ToDouble(row["ЧасыНорма"]);

                string label = UiTheme.FormatShortName(fullName);

                if (factH > maxHours) maxHours = factH;
                if (normH > maxHours) maxHours = normH;

                double pct = normH > 0 ? (factH / normH) * 100.0 : 0.0;

                int iNorm = sNorm.Points.AddXY(label, normH);
                sNorm.Points[iNorm].ToolTip = $"{fullName} (общая ставка {rate:0.##})\nНорма: {normH:0.#} ч.";

                int iFact = sFact.Points.AddXY(label, factH);
                sFact.Points[iFact].ToolTip = $"{fullName} (общая ставка {rate:0.##})\nОтработано: {factH:0.#} из {normH:0.#} ч. ({pct:0.#}%)";
            }

            area.AxisX.LabelStyle.Angle = limit > 6 ? -25 : 0;
            area.AxisY.Maximum = maxHours > 0 ? Math.Ceiling(maxHours * 1.22) : double.NaN;

            chart.Series.Add(sNorm);
            chart.Series.Add(sFact);
        }

        private static void SetEmptyChartState(Chart chart, string message)
        {
            chart.ChartAreas[0].Visible = false;
            chart.Legends[0].Enabled = false;
            Title emptyTitle = new Title(
                message,
                Docking.Top,
                new Font("Segoe UI", 9.25f, FontStyle.Regular),
                UiTheme.TextSecondary)
            {
                Alignment = ContentAlignment.MiddleCenter
            };
            emptyTitle.Position.Auto = false;
            emptyTitle.Position.X = 5f;
            emptyTitle.Position.Y = 36f;
            emptyTitle.Position.Width = 90f;
            emptyTitle.Position.Height = 28f;
            chart.Titles.Add(emptyTitle);
        }

        // =========================================================================
        // 6. ЭКРАН «ПОЛЬЗОВАТЕЛИ» (С ЗАЩИТОЙ ОТ УДАЛЕНИЯ САМОГО СЕБЯ)
        // =========================================================================
        private void LoadUsersScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                "Пользователи системы",
                "Управление учётными записями и ролями доступа");

            Panel toolbar = CreateToolbarCard();

            Button btnAdd = UiTheme.CreatePrimaryButton("+ Добавить пользователя", 12, 8, 205);
            Button btnDelete = UiTheme.CreateDangerButton("Удалить выбранного", 227, 8, 178);
            toolbar.Controls.Add(btnAdd);
            toolbar.Controls.Add(btnDelete);

            Label lblFooter;
            Panel footer = UiTheme.CreateSummaryFooter(_contentPanel, ContentMargin, out lblFooter);

            int gridTop = 140;
            int gridHeight = footer.Top - gridTop - 12;
            int innerWidth = _contentPanel.ClientSize.Width - (ContentMargin * 2);

            DataGridView dgv = UiTheme.CreateStyledGrid(_contentPanel, ContentMargin, gridTop, innerWidth, gridHeight, readOnly: true);

            Action loadData = () =>
            {
                try
                {
                    var response = new ServerConnection().SendRequest(new NetworkMessage { Command = "GET_USERS" });
                    if (response.IsSuccess)
                    {
                        DataTable dt = JsonConvert.DeserializeObject<DataTable>(response.Data);
                        if (dt != null && dt.Columns.Contains("Роль"))
                        {
                            foreach (DataRow row in dt.Rows)
                                if (row["Роль"] != null && row["Роль"].ToString() == "Сотрудник")
                                    row["Роль"] = "Экономист";
                        }
                        dgv.DataSource = dt;
                        if (dgv.Columns["UserID"] != null) dgv.Columns["UserID"].Visible = false;

                        int admins = 0;
                        int accountants = 0;
                        int staff = 0;
                        foreach (DataRow row in dt.Rows)
                        {
                            string r = dt.Columns.Contains("Роль") && row["Роль"] != DBNull.Value
                                ? row["Роль"].ToString()
                                : string.Empty;
                            if (r == "Администратор") admins++;
                            else if (r == "Бухгалтер") accountants++;
                            else staff++;
                        }

                        // Подсвечиваем текущую учётную запись администратора
                        foreach (DataGridViewRow gRow in dgv.Rows)
                        {
                            int uid = dgv.Columns["UserID"] != null ? Convert.ToInt32(gRow.Cells["UserID"].Value) : -1;
                            string lg = dgv.Columns["Логин"] != null ? gRow.Cells["Логин"].Value.ToString() : string.Empty;
                            if ((_currentUserId > 0 && uid == _currentUserId) ||
                                (!string.IsNullOrEmpty(_currentUserLogin) && string.Equals(lg, _currentUserLogin, StringComparison.OrdinalIgnoreCase)))
                            {
                                gRow.DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                            }
                        }

                        lblFooter.Text =
                            $"Всего пользователей: {dt.Rows.Count}  |  " +
                            $"Администраторов: {admins}  |  " +
                            $"Бухгалтеров: {accountants}  |  " +
                            $"Экономистов: {staff}";
                    }
                    else
                    {
                        MessageBox.Show("Ошибка: " + response.ErrorMessage);
                    }
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
            };
            loadData();

            btnAdd.Click += (s, e) =>
            {
                string[] result = ShowCustomInputDialog("Новый пользователь",
                    new string[] { "Логин:", "Пароль:", "Роль (1 — Администратор, 2 — Бухгалтер, 3 — Сотрудник):" });

                if (result != null && result.Length == 3)
                {
                    int roleId;
                    if (string.IsNullOrWhiteSpace(result[0]) || string.IsNullOrWhiteSpace(result[1]) ||
                        !int.TryParse(result[2].Trim(), out roleId) || roleId < 1 || roleId > 3)
                    {
                        MessageBox.Show("Заполните логин, пароль и укажите корректный номер роли (1, 2 или 3).",
                            "Ошибка ввода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    try
                    {
                        var req = new NetworkMessage { Command = "ADD_USER", Data = $"{result[0].Trim()}|{result[1]}|{roleId}" };
                        var res = new ServerConnection().SendRequest(req);
                        if (res.IsSuccess)
                        {
                            loadData();
                            MessageBox.Show("Пользователь добавлен!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("Ошибка: " + (res.ErrorMessage ?? "возможно, логин уже занят"));
                        }
                    }
                    catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
                }
            };

            btnDelete.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите пользователя!"); return; }
                if (dgv.Columns["UserID"] == null) { MessageBox.Show("Не найден идентификатор пользователя."); return; }

                int userId = Convert.ToInt32(dgv.SelectedRows[0].Cells["UserID"].Value);
                string loginCol = dgv.Columns["Логин"] != null ? "Логин" : "Login";
                string login = dgv.SelectedRows[0].Cells[loginCol].Value.ToString();

                // Защита от удаления собственной учётной записи администратора
                if ((_currentUserId > 0 && userId == _currentUserId) ||
                    (!string.IsNullOrEmpty(_currentUserLogin) &&
                     string.Equals(login, _currentUserLogin, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(
                        $"Вы не можете удалить собственную учётную запись «{login}», под которой выполнен вход!",
                        "Действие запрещено",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (MessageBox.Show($"Удалить пользователя «{login}»?", "Подтверждение",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        var req = new NetworkMessage
                        {
                            Command = "DELETE_USER",
                            Data = $"{userId}|{_currentUserId}"
                        };
                        var res = new ServerConnection().SendRequest(req);
                        if (res.IsSuccess)
                        {
                            loadData();
                            MessageBox.Show("Пользователь удалён!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show(res.ErrorMessage, "Удаление невозможно",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
                }
            };
        }

        // --- МОДАЛЬНЫЕ ОКНА В ФИРМЕННОМ СТИЛЕ ПОЛЕСГУ ---
        private string[] ShowCustomInputDialog(string title, string[] labels, string[] defaultValues = null)
        {
            using (Form prompt = new Form())
            {
                prompt.Text = title;
                prompt.ClientSize = new Size(420, 128 + (labels.Length * 62));
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.BackColor = UiTheme.CardBg;
                prompt.Font = new Font("Segoe UI", 9.5f);

                Panel header = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = UiTheme.SidebarBg };
                Label lblHeader = new Label
                {
                    Text = title,
                    Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(20, 13),
                    AutoSize = true
                };
                header.Controls.Add(lblHeader);
                prompt.Controls.Add(header);

                TextBox[] textBoxes = new TextBox[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    int top = 64 + (i * 62);
                    Label lbl = new Label
                    {
                        Left = 22,
                        Top = top,
                        Text = labels[i],
                        Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
                        ForeColor = UiTheme.TextSecondary,
                        AutoSize = true
                    };
                    TextBox txt = new TextBox { Font = new Font("Segoe UI", 10f) };
                    if (labels[i].IndexOf("пароль", StringComparison.OrdinalIgnoreCase) >= 0)
                        txt.UseSystemPasswordChar = true;
                    if (defaultValues != null && defaultValues.Length > i)
                        txt.Text = defaultValues[i];

                    Panel wrapped = UiTheme.WrapTextBox(txt, 22, top + 20, 376, 32);
                    textBoxes[i] = txt;
                    prompt.Controls.Add(lbl);
                    prompt.Controls.Add(wrapped);
                }

                int btnY = prompt.ClientSize.Height - 48;
                Button btnCancel = UiTheme.CreateOutlineButton("Отмена", 174, btnY, 108, 34);
                btnCancel.DialogResult = DialogResult.Cancel;

                Button btnOk = UiTheme.CreatePrimaryButton("Сохранить", 290, btnY, 108, 34);
                btnOk.DialogResult = DialogResult.OK;

                prompt.Controls.Add(btnCancel);
                prompt.Controls.Add(btnOk);
                prompt.AcceptButton = btnOk;
                prompt.CancelButton = btnCancel;

                if (prompt.ShowDialog() == DialogResult.OK)
                    return textBoxes.Select(tb => tb.Text).ToArray();
            }

            return null;
        }

        private string CalculateExactExperience(DateTime startDate)
        {
            DateTime endDate = DateTime.Today;
            if (endDate <= startDate) return "0 г. 0 мес. 0 дн.";

            int years = endDate.Year - startDate.Year;
            int months = endDate.Month - startDate.Month;
            int days = endDate.Day - startDate.Day;

            if (days < 0)
            {
                months--;
                DateTime prevMonth = endDate.AddMonths(-1);
                days += DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
            }

            if (months < 0)
            {
                years--;
                months += 12;
            }

            return $"{years} г. {months} мес. {days} дн.";
        }

        private void ShowEmployeeDetails(
            string fullName,
            string position,
            string department,
            string employmentType,
            decimal totalRate,
            string benefits,
            DateTime hireDate)
        {
            using (Form detailsForm = new Form())
            {
                detailsForm.Text = "Карточка сотрудника";
                detailsForm.ClientSize = new Size(470, 365);
                detailsForm.StartPosition = FormStartPosition.CenterParent;
                detailsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                detailsForm.MaximizeBox = false;
                detailsForm.MinimizeBox = false;
                detailsForm.BackColor = UiTheme.CardBg;
                detailsForm.Font = new Font("Segoe UI", 9.75f);

                Panel header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = UiTheme.SidebarBg };
                Label lblTitle = new Label
                {
                    Text = fullName,
                    Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(22, 15),
                    AutoSize = true
                };
                header.Controls.Add(lblTitle);
                detailsForm.Controls.Add(header);

                string expText = CalculateExactExperience(hireDate);
                string cleanDept = UiTheme.FormatDepartmentName(department);

                Label lblPos = new Label { Text = $"Должность:  {position}", ForeColor = UiTheme.TextPrimary, Location = new Point(24, 72), AutoSize = true };
                Label lblDept = new Label { Text = $"Кафедра:  {cleanDept}", ForeColor = UiTheme.TextPrimary, Location = new Point(24, 100), AutoSize = true };
                Label lblEmpType = new Label { Text = $"Занятость:  {employmentType} (общая ставка: {totalRate:0.##})", ForeColor = UiTheme.TextPrimary, Location = new Point(24, 128), AutoSize = true };
                Label lblBen = new Label { Text = $"Налоговые льготы:  {benefits}", ForeColor = UiTheme.TextPrimary, Location = new Point(24, 156), AutoSize = true };
                Label lblHire = new Label { Text = $"Дата приёма:  {hireDate:dd.MM.yyyy}", ForeColor = UiTheme.TextPrimary, Location = new Point(24, 184), AutoSize = true };

                Panel expCard = UiTheme.CreateCardPanel(leftAccent: true, topAccent: false);
                expCard.BackColor = UiTheme.MintPale;
                expCard.Location = new Point(24, 222);
                expCard.Size = new Size(422, 60);

                Label lblExpTitle = new Label
                {
                    Text = "Точный стаж работы в ПолесГУ",
                    Location = new Point(16, 7),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                    ForeColor = UiTheme.TextSecondary
                };
                Label lblExpValue = new Label
                {
                    Text = expText,
                    Location = new Point(16, 26),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                    ForeColor = UiTheme.PrimaryGreen
                };
                expCard.Controls.Add(lblExpTitle);
                expCard.Controls.Add(lblExpValue);

                Button btnOk = UiTheme.CreatePrimaryButton("Закрыть", 336, 306, 110, 36);
                btnOk.DialogResult = DialogResult.OK;

                detailsForm.Controls.AddRange(new Control[]
                {
                    lblPos, lblDept, lblEmpType, lblBen, lblHire, expCard, btnOk
                });
                detailsForm.AcceptButton = btnOk;
                detailsForm.CancelButton = btnOk;
                detailsForm.ShowDialog();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
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
        private readonly Panel _contentPanel;
        private Button _activeButton;

        // Ссылки на кнопки меню для быстрой навигации с главной страницы
        private Button _btnMenuEmployees;
        private Button _btnMenuTimesheet;
        private Button _btnMenuPayroll;
        private Button _btnMenuCharts;

        // Кэш последнего расчета и выбранный расчетный период
        private DataTable _payrollCache = null;
        private int _payrollCacheMonth = -1;
        private int _payrollCacheYear = -1;

        private int _selectedMonth = DateTime.Now.Month;
        private int _selectedYear = 2026;

        private readonly string[] _months = ExcelTimesheetService.MonthNames;
        private readonly string[] _shortMonths =
        {
            "Янв", "Фев", "Мар", "Апр", "Май", "Июн",
            "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"
        };

        private const int SidebarWidth = 245;
        private const int ContentMargin = 20;

        public MainForm(string role)
        {
            _userRole = role;
            this.Text = $"ПолесГУ — Система начисления заработной платы | {_userRole}";
            this.Size = new Size(1280, 740);
            this.MinimumSize = new Size(1200, 640);
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
            _btnMenuEmployees = AddMenuButton(sidebar, "Сотрудники", (s, e) => LoadEmployeesScreen());

            if (_userRole == "Бухгалтер" || _userRole == "Администратор")
            {
                _btnMenuTimesheet = AddMenuButton(sidebar, "Табель", (s, e) => LoadTimesheetScreen());
                _btnMenuPayroll = AddMenuButton(sidebar, "Расчет зарплаты", (s, e) => LoadPayrollScreen());
                _btnMenuCharts = AddMenuButton(sidebar, "Графики", (s, e) => LoadChartsScreen());
            }
            if (_userRole == "Администратор")
            {
                AddMenuButton(sidebar, "Пользователи", (s, e) => LoadUsersScreen());
            }

            // --- НИЖНИЙ БЛОК ПРОФИЛЯ И ВЫХОДА ---
            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 84,
                BackColor = UiTheme.SidebarHeaderBg
            };
            bottomPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(UiTheme.SidebarDivider, 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, bottomPanel.Width, 0);
                }
            };

            Label lblRoleInfo = new Label
            {
                Text = $"Роль: {_userRole}",
                ForeColor = UiTheme.SidebarTextMuted,
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                Location = new Point(24, 11),
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

            // Белая вертикальная полоса-индикатор активного раздела слева
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

        private void AddPeriodSelectors(Panel toolbar, out ComboBox cmbMonth, out NumericUpDown nudYear)
        {
            Label lblMonth = new Label
            {
                Text = "Месяц:",
                Location = new Point(14, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                ForeColor = UiTheme.TextSecondary
            };
            toolbar.Controls.Add(lblMonth);

            cmbMonth = new ComboBox
            {
                Location = new Point(68, 12),
                Size = new Size(118, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };
            cmbMonth.Items.AddRange(_months);
            cmbMonth.SelectedIndex = _selectedMonth - 1;
            toolbar.Controls.Add(cmbMonth);

            Label lblYear = new Label
            {
                Text = "Год:",
                Location = new Point(198, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                ForeColor = UiTheme.TextSecondary
            };
            toolbar.Controls.Add(lblYear);

            nudYear = new NumericUpDown
            {
                Location = new Point(234, 13),
                Size = new Size(70, 28),
                Minimum = 2020,
                Maximum = 2035,
                Value = _selectedYear,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            toolbar.Controls.Add(nudYear);
        }

        // =========================================================================
        // 1. ГЛАВНАЯ СТРАНИЦА (ДАШБОРД — БЕЗ НИЖНЕЙ СТРОКИ ИТОГОВ)
        // =========================================================================
        private void ShowWelcomeScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                $"Добро пожаловать, {_userRole}!",
                "Учреждение образования «Полесский государственный университет» — сводная панель системы");

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
                // Если сервер ещё не запущен, показываем аккуратные прочерки
            }

            // Сетка из 4 карточек ключевых показателей
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

            kpiGrid.Controls.Add(CreateStatCard("СОТРУДНИКОВ В ШТАТЕ", empCount, "Активные записи в базе"), 0, 0);
            kpiGrid.Controls.Add(CreateStatCard("ПОДРАЗДЕЛЕНИЙ", deptCount, "Кафедры и отделы вуза"), 1, 0);
            kpiGrid.Controls.Add(CreateStatCard("ПОЛЬЗОВАТЕЛЕЙ", userCount, "Учётные записи системы"), 2, 0);
            kpiGrid.Controls.Add(CreateStatCard("ОБЩИЙ ФОНД ВЫПЛАТ", $"{fundText} р.", "Сумма сохранённых расчётов"), 3, 0);

            // Карточка быстрого перехода по разделам
            Panel quickCard = UiTheme.CreateCardPanel(leftAccent: true, topAccent: false);
            quickCard.Location = new Point(ContentMargin, 232);
            quickCard.Size = new Size(_contentPanel.ClientSize.Width - (ContentMargin * 2), 125);
            quickCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(quickCard);

            Label lblQuickTitle = new Label
            {
                Text = "Быстрый переход к разделам",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                Location = new Point(22, 16),
                AutoSize = true
            };
            Label lblQuickSub = new Label
            {
                Text = $"Текущий расчётный период: {_months[_selectedMonth - 1]} {_selectedYear} г.",
                Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(22, 41),
                AutoSize = true
            };
            quickCard.Controls.Add(lblQuickTitle);
            quickCard.Controls.Add(lblQuickSub);

            int bx = 22;
            Button qEmp = UiTheme.CreateOutlineButton("Список сотрудников", bx, 72, 175, 36);
            qEmp.Click += (s, e) => { SetActiveButton(_btnMenuEmployees); LoadEmployeesScreen(); };
            quickCard.Controls.Add(qEmp);
            bx += 187;

            if (_userRole == "Бухгалтер" || _userRole == "Администратор")
            {
                Button qTab = UiTheme.CreatePrimaryButton("Импорт и просмотр табеля", bx, 72, 210, 36);
                qTab.Click += (s, e) => { SetActiveButton(_btnMenuTimesheet); LoadTimesheetScreen(); };
                quickCard.Controls.Add(qTab);
                bx += 222;

                Button qPay = UiTheme.CreateAccentButton("Расчёт зарплаты", bx, 72, 165, 36);
                qPay.Click += (s, e) => { SetActiveButton(_btnMenuPayroll); LoadPayrollScreen(); };
                quickCard.Controls.Add(qPay);
                bx += 177;

                Button qCharts = UiTheme.CreateOutlineButton("Открыть графики", bx, 72, 160, 36);
                qCharts.Click += (s, e) => { SetActiveButton(_btnMenuCharts); LoadChartsScreen(); };
                quickCard.Controls.Add(qCharts);
            }
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
        // 2. ЭКРАН «СОТРУДНИКИ»
        // =========================================================================
        private void LoadEmployeesScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                "Сотрудники университета",
                "Учёт профессорско-преподавательского состава и сотрудников подразделений ПолесГУ");

            bool canManage = _userRole == "Администратор" || _userRole == "Бухгалтер";
            Panel toolbar = CreateToolbarCard();

            Button btnAddEmp = UiTheme.CreatePrimaryButton("+ Принять на работу", 12, 8, 175);
            Button btnEditEmp = UiTheme.CreateOutlineButton("Редактировать", 197, 8, 138);
            Button btnDetails = UiTheme.CreateOutlineButton("Карточка сотрудника", canManage ? 345 : 12, 8, 168);
            Button btnDeleteEmp = UiTheme.CreateDangerButton("Уволить", 523, 8, 110);

            if (canManage)
            {
                toolbar.Controls.Add(btnAddEmp);
                toolbar.Controls.Add(btnEditEmp);
                toolbar.Controls.Add(btnDeleteEmp);
            }
            toolbar.Controls.Add(btnDetails);

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
                    var response = new ServerConnection().SendRequest(new NetworkMessage { Command = "GET_EMPLOYEES" });
                    if (response.IsSuccess)
                    {
                        DataTable dt = JsonConvert.DeserializeObject<DataTable>(response.Data);
                        dgv.DataSource = dt;

                        if (dgv.Columns["EmployeeID"] != null) dgv.Columns["EmployeeID"].Visible = false;
                        if (dgv.Columns["PositionID"] != null) dgv.Columns["PositionID"].Visible = false;
                        if (dgv.Columns["DepartmentID"] != null) dgv.Columns["DepartmentID"].Visible = false;

                        if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].FillWeight = 165;
                        if (dgv.Columns["Кафедра"] != null) dgv.Columns["Кафедра"].FillWeight = 160;
                        if (dgv.Columns["Должность"] != null) dgv.Columns["Должность"].FillWeight = 120;
                        if (dgv.Columns["Стаж"] != null)
                        {
                            dgv.Columns["Стаж"].FillWeight = 50;
                            dgv.Columns["Стаж"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        }
                        if (dgv.Columns["Ставка"] != null)
                        {
                            dgv.Columns["Ставка"].FillWeight = 55;
                            dgv.Columns["Ставка"].DefaultCellStyle.Format = "0.##";
                            dgv.Columns["Ставка"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        }
                        if (dgv.Columns["Тип занятости"] != null) dgv.Columns["Тип занятости"].FillWeight = 120;
                        if (dgv.Columns["Дата приема"] != null)
                        {
                            dgv.Columns["Дата приема"].FillWeight = 85;
                            dgv.Columns["Дата приема"].DefaultCellStyle.Format = "dd.MM.yyyy";
                            dgv.Columns["Дата приема"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        }

                        // Сводные показатели в нижней панели
                        int totalEmp = dt.Rows.Count;
                        var departments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        decimal sumRates = 0m;
                        double sumExpYears = 0.0;
                        int expCount = 0;

                        foreach (DataRow row in dt.Rows)
                        {
                            if (dt.Columns.Contains("Кафедра") && row["Кафедра"] != DBNull.Value)
                                departments.Add(row["Кафедра"].ToString());

                            if (dt.Columns.Contains("Ставка") && row["Ставка"] != DBNull.Value)
                                sumRates += Convert.ToDecimal(row["Ставка"]);
                            else
                                sumRates += 1.0m;

                            if (dt.Columns.Contains("Дата приема") && row["Дата приема"] != DBNull.Value)
                            {
                                DateTime hd = Convert.ToDateTime(row["Дата приема"]);
                                double yrs = Math.Max(0.0, (DateTime.Today - hd).TotalDays / 365.25);
                                sumExpYears += yrs;
                                expCount++;
                            }
                        }

                        double avgExp = expCount > 0 ? sumExpYears / expCount : 0.0;
                        lblFooter.Text =
                            $"Сотрудников: {totalEmp}   |   " +
                            $"Подразделений: {departments.Count}   |   " +
                            $"Суммарно ставок: {sumRates:0.##}   |   " +
                            $"Средний стаж в вузе: {avgExp:N1} г.";
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

                using (var form = new AddEmployeeForm(empId, fullName, posId, deptId, rate, empType))
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
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите сотрудника!"); return; }
                int empId = Convert.ToInt32(dgv.SelectedRows[0].Cells["EmployeeID"].Value);
                string fullName = dgv.SelectedRows[0].Cells["ФИО"].Value.ToString();
                if (MessageBox.Show($"Вы уверены, что хотите уволить сотрудника {fullName}?", "Увольнение",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    var res = new ServerConnection().SendRequest(new NetworkMessage { Command = "DELETE_EMPLOYEE", Data = empId.ToString() });
                    if (res.IsSuccess)
                    {
                        _payrollCache = null;
                        loadData();
                        MessageBox.Show("Сотрудник уволен!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                string fullName = dgv.SelectedRows[0].Cells["ФИО"].Value.ToString();
                string position = dgv.SelectedRows[0].Cells["Должность"].Value.ToString();
                string department = dgv.SelectedRows[0].Cells["Кафедра"].Value.ToString();
                DateTime hireDate = Convert.ToDateTime(dgv.SelectedRows[0].Cells["Дата приема"].Value);
                ShowEmployeeDetails(fullName, position, department, hireDate);
            };
        }

        // =========================================================================
        // 3. ЭКРАН «ТАБЕЛЬ» (БЕЗ КНОПКИ «СОЗДАТЬ БОЛВАНКУ» И БЕЗ ЛИШНЕЙ ПОДПИСИ)
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

            toolbar.Controls.Add(btnImport);
            toolbar.Controls.Add(btnSaveManual);
            toolbar.Controls.Add(btnClear);

            Label lblFooter;
            Panel footer = UiTheme.CreateSummaryFooter(_contentPanel, ContentMargin, out lblFooter);

            int gridTop = 140;
            int gridHeight = footer.Top - gridTop - 12;
            int innerWidth = _contentPanel.ClientSize.Width - (ContentMargin * 2);

            DataGridView dgv = UiTheme.CreateStyledGrid(_contentPanel, ContentMargin, gridTop, innerWidth, gridHeight, readOnly: false);
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
                    lblFooter.Text = "Сотрудников: 0   |   Нет данных для отображения.";
                    return;
                }

                int totalEmp = table.Rows.Count;
                int loadedCount = 0;
                int sumFactDays = 0;
                int sumNormDays = 0;
                decimal sumFactHours = 0m;
                decimal sumNormHours = 0m;

                foreach (DataRow row in table.Rows)
                {
                    if (table.Columns.Contains("Статус") && row["Статус"] != DBNull.Value
                        && row["Статус"].ToString() == "Из табеля")
                    {
                        loadedCount++;
                    }
                    if (table.Columns.Contains("Отработано дней") && row["Отработано дней"] != DBNull.Value)
                        sumFactDays += Convert.ToInt32(row["Отработано дней"]);
                    if (table.Columns.Contains("Норма дней") && row["Норма дней"] != DBNull.Value)
                        sumNormDays += Convert.ToInt32(row["Норма дней"]);
                    if (table.Columns.Contains("Отработано часов") && row["Отработано часов"] != DBNull.Value)
                        sumFactHours += Convert.ToDecimal(row["Отработано часов"]);
                    if (table.Columns.Contains("Норма часов") && row["Норма часов"] != DBNull.Value)
                        sumNormHours += Convert.ToDecimal(row["Норма часов"]);
                }

                decimal pct = sumNormHours > 0m ? Math.Round((sumFactHours / sumNormHours) * 100m, 1) : 0m;
                lblFooter.Text =
                    $"Сотрудников: {totalEmp}   |   " +
                    $"Из табеля: {loadedCount} из {totalEmp}   |   " +
                    $"Дни (факт / норма): {sumFactDays} / {sumNormDays}   |   " +
                    $"Часы (факт / норма): {sumFactHours:0.#} / {sumNormHours:0.#} ч. ({pct:0.#}%)";
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

                    if (dgv.Columns["EmployeeID"] != null) dgv.Columns["EmployeeID"].Visible = false;
                    if (dgv.Columns["Category"] != null) dgv.Columns["Category"].Visible = false;
                    if (dgv.Columns["Кафедра"] != null) dgv.Columns["Кафедра"].Visible = false;

                    string[] editableCols = { "Отработано дней", "Норма дней", "Отработано часов", "Норма часов" };
                    foreach (DataGridViewColumn col in dgv.Columns)
                    {
                        bool isEditable = editableCols.Contains(col.Name);
                        col.ReadOnly = !isEditable;
                        if (isEditable)
                        {
                            col.DefaultCellStyle.BackColor = UiTheme.EditableCellBg;
                            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                            col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        }
                    }

                    if (dgv.Columns["Ставка"] != null)
                    {
                        dgv.Columns["Ставка"].DefaultCellStyle.Format = "0.##";
                        dgv.Columns["Ставка"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        dgv.Columns["Ставка"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        dgv.Columns["Ставка"].FillWeight = 50;
                    }
                    if (dgv.Columns["Отработано часов"] != null) dgv.Columns["Отработано часов"].DefaultCellStyle.Format = "0.#";
                    if (dgv.Columns["Норма часов"] != null) dgv.Columns["Норма часов"].DefaultCellStyle.Format = "0.#";
                    if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].FillWeight = 155;
                    if (dgv.Columns["Должность"] != null) dgv.Columns["Должность"].FillWeight = 115;
                    if (dgv.Columns["Статус"] != null)
                    {
                        dgv.Columns["Статус"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        dgv.Columns["Статус"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        dgv.Columns["Статус"].FillWeight = 70;
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
                        int days = Convert.ToInt32(row["Отработано дней"]);
                        int normD = Convert.ToInt32(row["Норма дней"]);
                        decimal hours = Convert.ToDecimal(row["Отработано часов"]);
                        decimal normH = Convert.ToDecimal(row["Норма часов"]);

                        if (days < 0 || days > 31 || normD <= 0 || normD > 31 || hours < 0m || normH <= 0m)
                        {
                            MessageBox.Show($"Проверьте корректность дней и часов у сотрудника «{row["ФИО"]}».",
                                "Ошибка валидации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        entries.Add(new TimesheetEntryDto
                        {
                            EmployeeId = Convert.ToInt32(row["EmployeeID"]),
                            FullName = row["ФИО"].ToString(),
                            Rate = Convert.ToDecimal(row["Ставка"]),
                            DaysWorked = days,
                            NormDays = normD,
                            HoursWorked = hours,
                            NormHours = normH,
                            HasHours = true
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

        // Общий диалог импорта внешнего Excel-табеля с компьютера
        private bool RunExcelTimesheetImport(ref int month, ref int year)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Выберите Excel-файл табеля учёта рабочего времени";
                ofd.Filter = "Файлы Excel (*.xlsx)|*.xlsx|Все файлы (*.*)|*.*";

                if (ofd.ShowDialog() != DialogResult.OK)
                    return false;

                try
                {
                    ExcelParseResult parsed = ExcelTimesheetService.ParseTimesheetFile(ofd.FileName, month, year);
                    if (parsed.Entries.Count == 0)
                    {
                        MessageBox.Show("В выбранном файле не найдено строк с сотрудниками.",
                            "Пустой табель", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }

                    int fileMonth = parsed.DetectedMonth ?? month;
                    int fileYear = parsed.DetectedYear ?? year;
                    if (!parsed.IsIndividualPlan && (fileMonth != month || fileYear != year))
                    {
                        DialogResult choice = MessageBox.Show(
                            $"В файле табеля указан период: {_months[fileMonth - 1]} {fileYear} г.\n" +
                            $"Текущий выбранный период: {_months[month - 1]} {year} г.\n\n" +
                            $"Загрузить табель за {_months[fileMonth - 1]} {fileYear} г.?\n" +
                            $"• «Да» — загрузить за {_months[fileMonth - 1]} {fileYear} г.\n" +
                            $"• «Нет» — загрузить за {_months[month - 1]} {year} г.\n" +
                            "• «Отмена» — отменить импорт",
                            "Выбор периода импорта",
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

                    string extra = !string.IsNullOrEmpty(parsed.InfoMessage) ? parsed.InfoMessage + "\n\n" : string.Empty;
                    string tip = (importResult.SavedCount == 0 && importResult.UnmatchedNames != null && importResult.UnmatchedNames.Count > 0)
                        ? "\n\nПроверьте совпадение ФИО в Excel-файле со списком во вкладке «Сотрудники»."
                        : string.Empty;

                    MessageBox.Show(
                        $"{extra}Период: {_months[month - 1]} {year} г. (лист «{parsed.SheetName}»)\n" +
                        importResult.Message + tip,
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
        // 4. ЭКРАН «РАСЧЕТ ЗАРПЛАТЫ»
        // =========================================================================
        private void LoadPayrollScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                "Расчёт заработной платы",
                "Начисление оклада, надбавок и обязательных удержаний на основании данных табеля");

            Panel toolbar = CreateToolbarCard();

            ComboBox cmbMonth;
            NumericUpDown nudYear;
            AddPeriodSelectors(toolbar, out cmbMonth, out nudYear);

            Button btnCalculate = UiTheme.CreatePrimaryButton("Рассчитать", 322, 8, 132);
            Button btnImportTs = UiTheme.CreateAccentButton("Импорт табеля", 462, 8, 145);
            Button btnSave = UiTheme.CreateOutlineButton("Сохранить в БД", 615, 8, 145);
            Button btnExport = UiTheme.CreateOutlineButton("Экспорт в Excel", 768, 8, 145);

            btnSave.Enabled = false;
            btnExport.Enabled = false;

            toolbar.Controls.Add(btnCalculate);
            toolbar.Controls.Add(btnImportTs);
            toolbar.Controls.Add(btnSave);
            toolbar.Controls.Add(btnExport);

            Label lblTotals;
            Panel footer = UiTheme.CreateSummaryFooter(_contentPanel, ContentMargin, out lblTotals);
            lblTotals.Text = $"Период: {_months[_selectedMonth - 1]} {_selectedYear} г.   |   Нажмите «Рассчитать» или «Импорт табеля» для расчёта выплат.";

            int gridTop = 140;
            int gridHeight = footer.Top - gridTop - 12;
            int innerWidth = _contentPanel.ClientSize.Width - (ContentMargin * 2);

            DataGridView dgv = UiTheme.CreateStyledGrid(_contentPanel, ContentMargin, gridTop, innerWidth, gridHeight, readOnly: true);

            string[] textCols = { "EmployeeID", "ФИО", "Дни", "Часы" };

            Action<DataTable> applyTableFormatting = (DataTable dt) =>
            {
                dgv.DataSource = dt;
                if (dgv.Columns["EmployeeID"] != null) dgv.Columns["EmployeeID"].Visible = false;

                foreach (DataGridViewColumn col in dgv.Columns)
                {
                    if (!textCols.Contains(col.Name))
                    {
                        bool isRate = col.Name == "Ставка";
                        col.DefaultCellStyle.Format = isRate ? "0.##" : "N2";
                        col.DefaultCellStyle.Alignment = isRate
                            ? DataGridViewContentAlignment.MiddleCenter
                            : DataGridViewContentAlignment.MiddleRight;
                        col.HeaderCell.Style.Alignment = isRate
                            ? DataGridViewContentAlignment.MiddleCenter
                            : DataGridViewContentAlignment.MiddleRight;
                    }
                }

                if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].FillWeight = 160;
                if (dgv.Columns["Ставка"] != null) dgv.Columns["Ставка"].FillWeight = 50;
                if (dgv.Columns["Дни"] != null)
                {
                    dgv.Columns["Дни"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dgv.Columns["Дни"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dgv.Columns["Дни"].FillWeight = 62;
                }
                if (dgv.Columns["Часы"] != null)
                {
                    dgv.Columns["Часы"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dgv.Columns["Часы"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dgv.Columns["Часы"].FillWeight = 82;
                }
                if (dgv.Columns["К выплате"] != null)
                {
                    dgv.Columns["К выплате"].DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                    dgv.Columns["К выплате"].DefaultCellStyle.ForeColor = UiTheme.PrimaryGreen;
                    dgv.Columns["К выплате"].DefaultCellStyle.SelectionForeColor = UiTheme.PrimaryGreen;
                }

                decimal sumGross = 0m;
                decimal sumDeductions = 0m;
                decimal sumNet = 0m;
                foreach (DataRow row in dt.Rows)
                {
                    decimal b = dt.Columns.Contains("Оклад") ? Convert.ToDecimal(row["Оклад"]) : 0m;
                    decimal bon = dt.Columns.Contains("Надбавка") ? Convert.ToDecimal(row["Надбавка"]) : 0m;
                    decimal net = dt.Columns.Contains("К выплате") ? Convert.ToDecimal(row["К выплате"]) : 0m;
                    sumGross += b + bon;
                    sumNet += net;
                    sumDeductions += (b + bon) - net;
                }

                lblTotals.Text =
                    $"Сотрудников: {dt.Rows.Count}   |   " +
                    $"Начислено: {sumGross:N2} руб.   |   " +
                    $"Удержано: {sumDeductions:N2} руб.   |   " +
                    $"Итого к выплате: {sumNet:N2} руб.";

                btnSave.Enabled = dt.Rows.Count > 0;
                btnExport.Enabled = dt.Rows.Count > 0;
            };

            Action executeCalculation = () =>
            {
                try
                {
                    btnCalculate.Text = "Расчёт...";
                    btnCalculate.Enabled = false;

                    int m = cmbMonth.SelectedIndex + 1;
                    int y = (int)nudYear.Value;
                    _selectedMonth = m;
                    _selectedYear = y;

                    var request = new NetworkMessage { Command = "CALCULATE_PAYROLL", Data = $"{m}|{y}" };
                    var response = new ServerConnection().SendRequest(request);

                    if (response.IsSuccess)
                    {
                        DataTable dt = JsonConvert.DeserializeObject<DataTable>(response.Data);
                        _payrollCache = dt;
                        _payrollCacheMonth = m;
                        _payrollCacheYear = y;
                        applyTableFormatting(dt);
                    }
                    else
                    {
                        MessageBox.Show("Ошибка расчёта: " + response.ErrorMessage, "Ошибка",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
                finally { btnCalculate.Text = "Рассчитать"; btnCalculate.Enabled = true; }
            };

            Action onPeriodChanged = () =>
            {
                _selectedMonth = cmbMonth.SelectedIndex + 1;
                _selectedYear = (int)nudYear.Value;
                if (_payrollCache != null && _payrollCacheMonth == _selectedMonth && _payrollCacheYear == _selectedYear)
                {
                    applyTableFormatting(_payrollCache);
                }
                else
                {
                    dgv.DataSource = null;
                    btnSave.Enabled = false;
                    btnExport.Enabled = false;
                    lblTotals.Text = $"Период: {_months[_selectedMonth - 1]} {_selectedYear} г.   |   Нажмите «Рассчитать» или «Импорт табеля».";
                }
            };

            bool suppressPeriodEvents = false;
            cmbMonth.SelectedIndexChanged += (s, e) => { if (!suppressPeriodEvents) onPeriodChanged(); };
            nudYear.ValueChanged += (s, e) => { if (!suppressPeriodEvents) onPeriodChanged(); };

            if (_payrollCache != null && _payrollCacheMonth == _selectedMonth && _payrollCacheYear == _selectedYear)
            {
                applyTableFormatting(_payrollCache);
            }

            btnImportTs.Click += (s, e) =>
            {
                int m = cmbMonth.SelectedIndex + 1;
                int y = (int)nudYear.Value;
                if (RunExcelTimesheetImport(ref m, ref y))
                {
                    suppressPeriodEvents = true;
                    cmbMonth.SelectedIndex = m - 1;
                    if (y >= (int)nudYear.Minimum && y <= (int)nudYear.Maximum) nudYear.Value = y;
                    suppressPeriodEvents = false;
                    executeCalculation();
                }
            };

            btnCalculate.Click += (s, e) => executeCalculation();

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

                    if (response.IsSuccess) MessageBox.Show(response.Data, "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else MessageBox.Show("Ошибка: " + response.ErrorMessage, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
            };

            btnExport.Click += (s, e) =>
            {
                int y = (int)nudYear.Value;

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Excel Workbook (*.xlsx)|*.xlsx";
                    sfd.FileName = $"Ведомость_{cmbMonth.Text}_{y}.xlsx";

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
                                IXLWorksheet worksheet = workbook.Worksheets.Add(exportDt, "Расчет");
                                IXLRange headerRange = worksheet.Range(1, 1, 1, exportDt.Columns.Count);
                                headerRange.Style.Font.Bold = true;
                                headerRange.Style.Font.FontColor = XLColor.White;
                                headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(24, 119, 64);

                                int totalRow = exportDt.Rows.Count + 2;
                                worksheet.Cell(totalRow, 1).Value = "ИТОГО:";
                                worksheet.Cell(totalRow, 1).Style.Font.Bold = true;

                                string[] moneyCols = { "Оклад", "Надбавка", "НДФЛ (13%)", "ФСЗН (1%)", "Профсоюз (1%)", "К выплате" };
                                for (int c = 0; c < exportDt.Columns.Count; c++)
                                {
                                    if (moneyCols.Contains(exportDt.Columns[c].ColumnName))
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
        // 5. ЭКРАН «ГРАФИКИ» (4 АНАЛИТИЧЕСКИХ ГРАФИКА В СТИЛЕ ПОЛЕСГУ + ИТОГИ ВНИЗУ)
        // =========================================================================
        private void LoadChartsScreen()
        {
            _contentPanel.Controls.Clear();

            UiTheme.CreatePageHeader(
                _contentPanel,
                "Графики и аналитика",
                "Визуализация фонда оплаты труда, структуры удержаний, годовой динамики и отработки часов");

            Panel toolbar = CreateToolbarCard();

            ComboBox cmbMonths;
            NumericUpDown nudYear;
            AddPeriodSelectors(toolbar, out cmbMonths, out nudYear);

            Button btnRefresh = UiTheme.CreatePrimaryButton("Обновить графики", 322, 8, 160);
            toolbar.Controls.Add(btnRefresh);

            Label lblFooter;
            Panel footer = UiTheme.CreateSummaryFooter(_contentPanel, ContentMargin, out lblFooter);

            int gridTop = 136;
            int gridHeight = footer.Top - gridTop - 8;
            int innerWidth = _contentPanel.ClientSize.Width - (ContentMargin * 2) + 12;

            // Адаптивная сетка 2x2 на 4 карточки с графиками
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
            _contentPanel.Controls.Add(chartsGrid);

            Label lblSub1, lblSub2, lblSub3, lblSub4;
            Chart chartDept = UiTheme.CreateChartCard(chartsGrid, "1. Фонд оплаты труда по кафедрам", out lblSub1);
            Chart chartStructure = UiTheme.CreateChartCard(chartsGrid, "2. Структура фонда оплаты труда", out lblSub2);
            Chart chartTrend = UiTheme.CreateChartCard(chartsGrid, "3. Динамика фонда зарплаты за год", out lblSub3);
            Chart chartHours = UiTheme.CreateChartCard(chartsGrid, "4. Отработка часов (Факт / Норма)", out lblSub4);

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

                    // Нижняя сводная строка вкладки «Графики»
                    decimal totalGross = data.TotalBaseSalary + data.TotalBonus;
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
                            $"Период: {periodLabel}   |   " +
                            $"Сотрудников: {empCnt}   |   " +
                            $"Часы по табелю: {sumH:0.#} / {sumNH:0.#} ч.   |   " +
                            "Расчёт зарплаты за этот месяц ещё не сохранён в БД";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка сети: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            cmbMonths.SelectedIndexChanged += (s, e) => updateCharts();
            nudYear.ValueChanged += (s, e) => updateCharts();
            btnRefresh.Click += (s, e) => updateCharts();

            updateCharts();
        }

        // График 1: Горизонтальная диаграмма ФОТ по кафедрам (Начислено и К выплате)
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

            // В горизонтальной диаграмме (Bar) ось X — категории (вертикально), ось Y — суммы (горизонтально)
            area.AxisX.MajorGrid.Enabled = false;
            area.AxisX.Interval = 1;
            area.AxisY.MajorGrid.Enabled = true;
            area.AxisY.MajorGrid.LineColor = UiTheme.GridLineColor;
            area.AxisY.MajorGrid.LineDashStyle = ChartDashStyle.Dash;
            area.AxisY.Minimum = 0;
            area.AxisY.LabelStyle.Format = "N0";

            Series sGross = new Series("Начислено")
            {
                ChartType = SeriesChartType.Bar,
                Color = Color.FromArgb(178, 222, 195),
                BorderColor = Color.FromArgb(124, 190, 150),
                BorderWidth = 1,
                IsValueShownAsLabel = false
            };
            sGross["PointWidth"] = "0.65";

            Series sNet = new Series("К выплате")
            {
                ChartType = SeriesChartType.Bar,
                Color = UiTheme.PrimaryGreen,
                IsValueShownAsLabel = true,
                LabelFormat = "N0",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                LabelForeColor = UiTheme.TextPrimary
            };
            sNet["PointWidth"] = "0.65";

            double maxVal = 0.0;
            foreach (DataRow row in dt.Rows)
            {
                string dept = row["Кафедра"].ToString();
                string shortDept = dept.Length > 26 ? dept.Substring(0, 24) + "…" : dept;
                double gross = Convert.ToDouble(row["Начислено"]);
                double net = Convert.ToDouble(row["КВыплате"]);
                int cnt = Convert.ToInt32(row["Сотрудников"]);
                if (gross > maxVal) maxVal = gross;

                int i1 = sGross.Points.AddXY(shortDept, gross);
                sGross.Points[i1].ToolTip = $"{dept}\nНачислено: {gross:N2} руб. (сотрудников: {cnt})";

                int i2 = sNet.Points.AddXY(shortDept, net);
                sNet.Points[i2].ToolTip = $"{dept}\nК выплате: {net:N2} руб. (сотрудников: {cnt})";
            }

            area.AxisY.Maximum = maxVal > 0 ? Math.Ceiling(maxVal * 1.22) : double.NaN;
            chart.Series.Add(sGross);
            chart.Series.Add(sNet);
        }

        // График 2: Кольцевая диаграмма (Doughnut) структуры начислений и удержаний
        private void RenderStructureChart(Chart chart, AnalyticsDataDto data, Label lblSub)
        {
            chart.Series.Clear();
            chart.Titles.Clear();
            ChartArea area = chart.ChartAreas[0];

            decimal gross = data.TotalBaseSalary + data.TotalBonus;
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
                new { Name = "К выплате", Amount = data.TotalNetSalary, Color = UiTheme.PrimaryGreen },
                new { Name = "НДФЛ (13%)", Amount = data.TotalTax, Color = Color.FromArgb(62, 168, 108) },
                new { Name = "ФСЗН (1%)", Amount = data.TotalFszen, Color = Color.FromArgb(132, 204, 160) },
                new { Name = "Профсоюз (1%)", Amount = data.TotalUnion, Color = Color.FromArgb(190, 230, 205) }
            };

            foreach (var sl in slices)
            {
                if (sl.Amount <= 0m) continue;
                decimal pct = Math.Round((sl.Amount / gross) * 100m, 1);
                int idx = s.Points.AddXY(sl.Name, Convert.ToDouble(sl.Amount));
                DataPoint pt = s.Points[idx];
                pt.Color = sl.Color;
                pt.LegendText = $"{sl.Name}: {sl.Amount:N2} р. ({pct:0.#}%)";
                pt.ToolTip = $"{sl.Name}\nСумма: {sl.Amount:N2} руб.\nДоля от начисленного: {pct:0.#}%";
            }

            chart.Series.Add(s);
        }

        // График 3: Плавная годовая динамика фонда зарплаты по 12 месяцам (SplineArea + Spline)
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
                Color = Color.FromArgb(52, 24, 119, 64),
                BorderColor = UiTheme.PrimaryGreen,
                BorderWidth = 3,
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                LabelForeColor = UiTheme.PrimaryGreen
            };
            sArea["LineTension"] = "0.35";

            Series sGrossLine = new Series("Начислено")
            {
                ChartType = SeriesChartType.Spline,
                Color = Color.FromArgb(74, 176, 117),
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
                    pNet.MarkerSize = isCurrent ? 9 : 7;
                    pNet.MarkerColor = isCurrent ? UiTheme.PrimaryGreen : Color.White;
                    pNet.MarkerBorderColor = UiTheme.PrimaryGreen;
                    pNet.MarkerBorderWidth = 2;
                    pNet.Label = n.ToString("N0");
                    pNet.ToolTip = $"{_months[i]}\nК выплате: {n:N2} руб.\nНачислено: {g:N2} руб.";
                }

                int idxGross = sGrossLine.Points.AddXY(_shortMonths[i], g);
                if (hasData)
                {
                    sGrossLine.Points[idxGross].MarkerStyle = MarkerStyle.Circle;
                    sGrossLine.Points[idxGross].MarkerSize = 5;
                    sGrossLine.Points[idxGross].MarkerColor = Color.FromArgb(74, 176, 117);
                    sGrossLine.Points[idxGross].ToolTip = $"{_months[i]} — Начислено: {g:N2} руб.";
                }
            }

            chart.Series.Add(sArea);
            chart.Series.Add(sGrossLine);
        }

        // График 4: Сгруппированная столбчатая диаграмма отработки часов (Факт / Норма) по сотрудникам
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

            Series sNorm = new Series("Норма часов")
            {
                ChartType = SeriesChartType.Column,
                Color = Color.FromArgb(206, 235, 217),
                BorderColor = Color.FromArgb(142, 199, 164),
                BorderWidth = 1,
                IsValueShownAsLabel = false
            };
            sNorm["PointWidth"] = "0.58";

            Series sFact = new Series("Отработано часов")
            {
                ChartType = SeriesChartType.Column,
                Color = UiTheme.PrimaryGreen,
                IsValueShownAsLabel = dt.Rows.Count <= 10,
                LabelFormat = "0.#",
                Font = new Font("Segoe UI", 7.75f, FontStyle.Bold),
                LabelForeColor = UiTheme.TextPrimary
            };
            sFact["PointWidth"] = "0.58";

            // Проверяем повторяющиеся ФИО (совместители), чтобы подписывать ставку
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in dt.Rows)
            {
                string fn = r["ФИО"].ToString();
                nameCounts[fn] = nameCounts.ContainsKey(fn) ? nameCounts[fn] + 1 : 1;
            }

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
                if (nameCounts[fullName] > 1)
                    label = $"{label} ({rate:0.##})";

                if (factH > maxHours) maxHours = factH;
                if (normH > maxHours) maxHours = normH;

                double pct = normH > 0 ? (factH / normH) * 100.0 : 0.0;

                int iNorm = sNorm.Points.AddXY(label, normH);
                sNorm.Points[iNorm].ToolTip = $"{fullName} (ставка {rate:0.##})\nНорма: {normH:0.#} ч.";

                int iFact = sFact.Points.AddXY(label, factH);
                sFact.Points[iFact].ToolTip = $"{fullName} (ставка {rate:0.##})\nОтработано: {factH:0.#} из {normH:0.#} ч. ({pct:0.#}%)";
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
        // 6. ЭКРАН «ПОЛЬЗОВАТЕЛИ»
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

                        lblFooter.Text =
                            $"Всего пользователей: {dt.Rows.Count}   |   " +
                            $"Администраторов: {admins}   |   " +
                            $"Бухгалтеров: {accountants}   |   " +
                            $"Сотрудников: {staff}";
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
                        if (res.IsSuccess) { loadData(); MessageBox.Show("Пользователь добавлен!", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                        else MessageBox.Show("Ошибка: " + (res.ErrorMessage ?? "возможно, логин занят"));
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

                if (MessageBox.Show($"Удалить пользователя «{login}»?", "Подтверждение",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        var req = new NetworkMessage { Command = "DELETE_USER", Data = userId.ToString() };
                        var res = new ServerConnection().SendRequest(req);
                        if (res.IsSuccess) { loadData(); MessageBox.Show("Пользователь удалён!", "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                        else MessageBox.Show("Ошибка удаления: " + res.ErrorMessage);
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

        private void ShowEmployeeDetails(string fullName, string position, string department, DateTime hireDate)
        {
            using (Form detailsForm = new Form())
            {
                detailsForm.Text = "Карточка сотрудника";
                detailsForm.ClientSize = new Size(460, 295);
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

                Label lblPos = new Label { Text = $"Должность:  {position}", ForeColor = UiTheme.TextPrimary, Location = new Point(24, 74), AutoSize = true };
                Label lblDept = new Label { Text = $"Подразделение:  {department}", ForeColor = UiTheme.TextPrimary, Location = new Point(24, 104), AutoSize = true };
                Label lblHire = new Label { Text = $"Дата приёма:  {hireDate:dd.MM.yyyy}", ForeColor = UiTheme.TextPrimary, Location = new Point(24, 134), AutoSize = true };

                Panel expCard = UiTheme.CreateCardPanel(leftAccent: true, topAccent: false);
                expCard.BackColor = UiTheme.MintPale;
                expCard.Location = new Point(24, 170);
                expCard.Size = new Size(412, 58);

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

                Button btnOk = UiTheme.CreatePrimaryButton("Закрыть", 326, 244, 110, 36);
                btnOk.DialogResult = DialogResult.OK;

                detailsForm.Controls.AddRange(new Control[] { lblPos, lblDept, lblHire, expCard, btnOk });
                detailsForm.AcceptButton = btnOk;
                detailsForm.CancelButton = btnOk;
                detailsForm.ShowDialog();
            }
        }
    }
}

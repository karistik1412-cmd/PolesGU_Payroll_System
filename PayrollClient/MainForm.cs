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

        // Кэш последнего расчета и выбранный расчетный период
        private DataTable _payrollCache = null;
        private int _payrollCacheMonth = -1;
        private int _payrollCacheYear = -1;

        private int _selectedMonth = DateTime.Now.Month;
        private int _selectedYear = 2026;

        private readonly string[] _months = ExcelTimesheetService.MonthNames;

        public MainForm(string role)
        {
            _userRole = role;
            this.Text = $"ПолесГУ: Система начисления зарплаты | {_userRole}";
            this.Size = new Size(1240, 680);
            this.MinimumSize = new Size(1080, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 244, 248);
            this.Font = new Font("Segoe UI", 10f);

            // --- ЛЕВАЯ ПАНЕЛЬ ---
            Panel sidebar = new Panel();
            sidebar.Location = new Point(0, 0);
            sidebar.Size = new Size(240, this.ClientSize.Height);
            sidebar.BackColor = Color.FromArgb(45, 52, 54);
            sidebar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            this.Controls.Add(sidebar);

            // --- ПРАВАЯ ПАНЕЛЬ ---
            _contentPanel = new Panel();
            _contentPanel.Location = new Point(240, 0);
            _contentPanel.Size = new Size(this.ClientSize.Width - 240, this.ClientSize.Height);
            _contentPanel.BackColor = Color.FromArgb(240, 244, 248);
            _contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(_contentPanel);

            // --- ЛОГОТИП И МЕНЮ ---
            Label logoLabel = new Label();
            logoLabel.Text = "ПолесГУ\nЗарплата";
            logoLabel.ForeColor = Color.White;
            logoLabel.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            logoLabel.Dock = DockStyle.Top;
            logoLabel.Height = 80;
            logoLabel.TextAlign = ContentAlignment.MiddleCenter;
            sidebar.Controls.Add(logoLabel);

            Button btnHome = AddMenuButton(sidebar, "Главная", (s, e) => ShowWelcomeScreen());
            AddMenuButton(sidebar, "Сотрудники", (s, e) => LoadEmployeesScreen());

            if (_userRole == "Бухгалтер" || _userRole == "Администратор")
            {
                AddMenuButton(sidebar, "Табель", (s, e) => LoadTimesheetScreen());
                AddMenuButton(sidebar, "Расчет зарплаты", (s, e) => LoadPayrollScreen());
                AddMenuButton(sidebar, "Отчеты", (s, e) => LoadAnalyticsScreen());
            }
            if (_userRole == "Администратор")
            {
                AddMenuButton(sidebar, "Пользователи", (s, e) => LoadUsersScreen());
            }

            Button btnExit = CreateButton("Выход");
            btnExit.Dock = DockStyle.Bottom;
            btnExit.ForeColor = Color.FromArgb(255, 100, 100);
            btnExit.Click += (s, e) => Application.Exit();
            sidebar.Controls.Add(btnExit);

            SetActiveButton(btnHome);
            ShowWelcomeScreen();
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ МЕНЮ И КНОПОК ---
        private void SetActiveButton(Button btn)
        {
            if (_activeButton != null) _activeButton.BackColor = Color.FromArgb(45, 52, 54);
            _activeButton = btn;
            _activeButton.BackColor = Color.FromArgb(70, 80, 83);
        }

        private Button AddMenuButton(Panel sidebar, string text, EventHandler onClick)
        {
            Button btn = CreateButton(text);
            btn.Dock = DockStyle.Top;
            btn.Click += (s, e) => { SetActiveButton(btn); onClick(s, e); };
            sidebar.Controls.Add(btn);
            btn.BringToFront();
            return btn;
        }

        private Button CreateButton(string text)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Height = 50;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = Color.FromArgb(45, 52, 54);
            btn.ForeColor = Color.White;
            btn.Font = new Font("Segoe UI", 11f);
            btn.Cursor = Cursors.Hand;
            btn.TextAlign = ContentAlignment.MiddleLeft;
            btn.Padding = new Padding(20, 0, 0, 0);
            btn.MouseEnter += (s, e) => { if (btn != _activeButton) btn.BackColor = Color.FromArgb(60, 70, 73); };
            btn.MouseLeave += (s, e) => { if (btn != _activeButton) btn.BackColor = Color.FromArgb(45, 52, 54); };
            return btn;
        }

        private Button CreateActionButton(string text, int x, int y, int width, Color color)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        // --- ГЛАВНАЯ СТРАНИЦА (ДАШБОРД) ---
        private void ShowWelcomeScreen()
        {
            _contentPanel.Controls.Clear();

            Label welcome = new Label();
            welcome.Text = $"Добро пожаловать, {_userRole}!";
            welcome.Font = new Font("Segoe UI", 24f, FontStyle.Bold);
            welcome.ForeColor = Color.FromArgb(45, 52, 54);
            welcome.Location = new Point(20, 20);
            welcome.AutoSize = true;
            _contentPanel.Controls.Add(welcome);

            try
            {
                var connection = new ServerConnection();
                var request = new NetworkMessage { Command = "GET_DASHBOARD" };
                var response = connection.SendRequest(request);

                if (response.IsSuccess)
                {
                    string[] stats = response.Data.Split('|');
                    decimal totalFund = 0m;
                    if (stats.Length > 2)
                    {
                        decimal.TryParse(stats[2].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out totalFund);
                    }

                    CreateStatCard("Сотрудников в базе", stats[0], 20, 85);
                    CreateStatCard("Пользователей системы", stats[1], 300, 85);
                    CreateStatCard("Общий фонд (руб.)", Math.Round(totalFund, 2).ToString("N2"), 20, 220);
                }
            }
            catch (Exception) { /* Игнорируем ошибки, если сервер недоступен */ }
        }

        private void CreateStatCard(string title, string value, int x, int y)
        {
            Panel card = new Panel();
            card.Location = new Point(x, y);
            card.Size = new Size(260, 115);
            card.BackColor = Color.White;

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.ForeColor = Color.Gray;
            lblTitle.Location = new Point(15, 15);
            lblTitle.AutoSize = true;
            card.Controls.Add(lblTitle);

            Label lblValue = new Label();
            lblValue.Text = value;
            lblValue.Font = new Font("Segoe UI", 22f, FontStyle.Bold);
            lblValue.ForeColor = Color.FromArgb(46, 139, 87);
            lblValue.Location = new Point(15, 45);
            lblValue.AutoSize = true;
            card.Controls.Add(lblValue);

            _contentPanel.Controls.Add(card);
        }

        // --- ЭКРАН СОТРУДНИКИ ---
        private void LoadEmployeesScreen()
        {
            _contentPanel.Controls.Clear();

            int innerWidth = _contentPanel.ClientSize.Width - 40;
            bool canManage = _userRole == "Администратор" || _userRole == "Бухгалтер";

            Panel topPanel = new Panel();
            topPanel.Location = new Point(20, 15);
            topPanel.Size = new Size(innerWidth, 45);
            topPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(topPanel);

            Button btnAddEmp = CreateActionButton("+ Принять на работу", 0, 2, 180, Color.FromArgb(46, 139, 87));
            Button btnEditEmp = CreateActionButton("Редактировать", 190, 2, 140, Color.FromArgb(52, 152, 219));
            Button btnDeleteEmp = CreateActionButton("Уволить", 340, 2, 120, Color.FromArgb(231, 76, 60));
            Button btnDetails = CreateActionButton("Подробнее", canManage ? 470 : 0, 2, 120, Color.FromArgb(155, 89, 182));

            if (canManage)
            {
                topPanel.Controls.Add(btnAddEmp);
                topPanel.Controls.Add(btnEditEmp);
                topPanel.Controls.Add(btnDeleteEmp);
            }
            topPanel.Controls.Add(btnDetails);

            DataGridView dgv = new DataGridView();
            dgv.Location = new Point(20, 70);
            dgv.Size = new Size(innerWidth, _contentPanel.ClientSize.Height - 90);
            dgv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgv.BackgroundColor = Color.White;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dgv.AllowUserToResizeColumns = true;
            dgv.AllowUserToAddRows = false;
            dgv.ReadOnly = true;
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgv.RowHeadersVisible = false;
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(46, 139, 87);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _contentPanel.Controls.Add(dgv);

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

                        if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].Width = 210;
                        if (dgv.Columns["Кафедра"] != null) dgv.Columns["Кафедра"].Width = 210;
                        if (dgv.Columns["Должность"] != null) dgv.Columns["Должность"].Width = 155;
                        if (dgv.Columns["Стаж"] != null) dgv.Columns["Стаж"].Width = 55;
                        if (dgv.Columns["Ставка"] != null) dgv.Columns["Ставка"].Width = 65;
                        if (dgv.Columns["Тип занятости"] != null) dgv.Columns["Тип занятости"].Width = 160;

                        if (dgv.Columns["Дата приема"] != null)
                            dgv.Columns["Дата приема"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
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
                if (MessageBox.Show($"Вы уверены, что хотите уволить {fullName}?", "Увольнение",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    var res = new ServerConnection().SendRequest(new NetworkMessage { Command = "DELETE_EMPLOYEE", Data = empId.ToString() });
                    if (res.IsSuccess)
                    {
                        _payrollCache = null;
                        loadData();
                        MessageBox.Show("Сотрудник уволен!");
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

        // --- ЭКРАН ТАБЕЛЯ УЧЕТА РАБОЧЕГО ВРЕМЕНИ (ИМПОРТ ИЗ ТАБЕЛЯ-БОЛВАНКИ) ---
        private void LoadTimesheetScreen()
        {
            _contentPanel.Controls.Clear();

            int innerWidth = _contentPanel.ClientSize.Width - 40;

            Panel topPanel = new Panel();
            topPanel.Location = new Point(20, 12);
            topPanel.Size = new Size(innerWidth, 46);
            topPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(topPanel);

            Label lblMonth = new Label { Text = "Месяц:", Location = new Point(0, 12), AutoSize = true };
            topPanel.Controls.Add(lblMonth);

            ComboBox cmbMonth = new ComboBox
            {
                Location = new Point(56, 9),
                Size = new Size(112, 30),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMonth.Items.AddRange(_months);
            cmbMonth.SelectedIndex = _selectedMonth - 1;
            topPanel.Controls.Add(cmbMonth);

            Label lblYear = new Label { Text = "Год:", Location = new Point(176, 12), AutoSize = true };
            topPanel.Controls.Add(lblYear);

            NumericUpDown nudYear = new NumericUpDown
            {
                Location = new Point(214, 9),
                Size = new Size(68, 30),
                Minimum = 2020,
                Maximum = 2035,
                Value = _selectedYear
            };
            topPanel.Controls.Add(nudYear);

            Button btnImport = CreateActionButton("Импорт из Excel", 294, 4, 155, Color.FromArgb(46, 139, 87));
            Button btnGenerate = CreateActionButton("Создать болванку", 457, 4, 162, Color.FromArgb(52, 152, 219));
            Button btnSaveManual = CreateActionButton("Сохранить правки", 627, 4, 158, Color.FromArgb(230, 126, 34));
            Button btnClear = CreateActionButton("Сбросить", 793, 4, 105, Color.FromArgb(231, 76, 60));

            topPanel.Controls.Add(btnImport);
            topPanel.Controls.Add(btnGenerate);
            topPanel.Controls.Add(btnSaveManual);
            topPanel.Controls.Add(btnClear);

            Label lblHint = new Label
            {
                Text = "Импортируйте «табель-болванку» (.xlsx) с ноутбука или создайте готовую болванку со случайными днями и часами по сотрудникам БД.",
                Location = new Point(20, 62),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(95, 105, 110)
            };
            _contentPanel.Controls.Add(lblHint);

            DataGridView dgv = new DataGridView
            {
                Location = new Point(20, 88),
                Size = new Size(innerWidth, _contentPanel.ClientSize.Height - 108),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false
            };
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 52, 54);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            dgv.ColumnHeadersHeight = 36;
            dgv.DataError += (s, ev) =>
            {
                MessageBox.Show("Введите корректное числовое значение для дней или часов.",
                    "Некорректное значение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ev.Cancel = true;
            };
            _contentPanel.Controls.Add(dgv);

            bool suppressEvents = false;

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
                            col.DefaultCellStyle.BackColor = Color.FromArgb(248, 253, 249);
                            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        }
                    }

                    if (dgv.Columns["Ставка"] != null)
                    {
                        dgv.Columns["Ставка"].DefaultCellStyle.Format = "0.##";
                        dgv.Columns["Ставка"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        dgv.Columns["Ставка"].FillWeight = 45;
                    }
                    if (dgv.Columns["Отработано часов"] != null) dgv.Columns["Отработано часов"].DefaultCellStyle.Format = "0.#";
                    if (dgv.Columns["Норма часов"] != null) dgv.Columns["Норма часов"].DefaultCellStyle.Format = "0.#";
                    if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].FillWeight = 150;
                    if (dgv.Columns["Должность"] != null) dgv.Columns["Должность"].FillWeight = 110;
                    if (dgv.Columns["Статус"] != null)
                    {
                        dgv.Columns["Статус"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        dgv.Columns["Статус"].FillWeight = 65;
                    }

                    foreach (DataGridViewRow row in dgv.Rows)
                    {
                        if (dgv.Columns["Статус"] != null && row.Cells["Статус"].Value != null)
                        {
                            string st = row.Cells["Статус"].Value.ToString();
                            row.Cells["Статус"].Style.ForeColor = st == "Из табеля"
                                ? Color.FromArgb(39, 174, 96)
                                : Color.Gray;
                            row.Cells["Статус"].Style.Font = new Font("Segoe UI", 9.5f,
                                st == "Из табеля" ? FontStyle.Bold : FontStyle.Regular);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка сети: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

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

            btnGenerate.Click += (s, e) =>
            {
                int m = cmbMonth.SelectedIndex + 1;
                int y = (int)nudYear.Value;

                using (var sfd = new SaveFileDialog())
                {
                    sfd.Title = "Сохранить табель-болванку на компьютер";
                    sfd.Filter = "Excel Workbook (*.xlsx)|*.xlsx";
                    sfd.FileName = $"Табель_болванка_{_months[m - 1]}_{y}.xlsx";

                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            DataTable currentEmployees = dgv.DataSource as DataTable;
                            ExcelTimesheetService.GenerateDummyTimesheet(sfd.FileName, currentEmployees, m, y);
                            MessageBox.Show(
                                $"Табель-болванка успешно создан:\n{sfd.FileName}\n\n" +
                                "В файл подставлены случайные отработанные дни и часы. " +
                                "Вы можете отредактировать его в Excel и загрузить через «Импорт из Excel».",
                                "Табель-болванка готов",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Ошибка при создании файла: " + ex.Message, "Ошибка",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
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
                        MessageBox.Show("Данные табеля сохранены в БД! Теперь можно выполнить расчет зарплаты.",
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

        // Общий диалог импорта табеля из Excel-файла с ноутбука
        private bool RunExcelTimesheetImport(ref int month, ref int year)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Выберите Excel-файл табеля учета рабочего времени";
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

                    // Если в шапке файла явно указан другой месяц или год — уточняем у пользователя
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
                        ? "\n\nСовет: проверьте совпадение ФИО с разделом «Сотрудники» или нажмите «Создать болванку» во вкладке «Табель», чтобы сформировать файл по вашим сотрудникам из БД."
                        : string.Empty;

                    MessageBox.Show(
                        $"{extra}Период: {_months[month - 1]} {year} г. (лист «{parsed.SheetName}»)\n" +
                        importResult.Message + tip,
                        "Импорт табеля завершен",
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

        // --- ЭКРАН РАСЧЕТА ЗАРПЛАТЫ ---
        private void LoadPayrollScreen()
        {
            _contentPanel.Controls.Clear();

            int innerWidth = _contentPanel.ClientSize.Width - 40;

            Panel topPanel = new Panel();
            topPanel.Location = new Point(20, 12);
            topPanel.Size = new Size(innerWidth, 48);
            topPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(topPanel);

            Label lblMonth = new Label { Text = "Месяц:", Location = new Point(0, 13), AutoSize = true };
            topPanel.Controls.Add(lblMonth);

            ComboBox cmbMonth = new ComboBox
            {
                Location = new Point(56, 10),
                Size = new Size(112, 30),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMonth.Items.AddRange(_months);
            cmbMonth.SelectedIndex = _selectedMonth - 1;
            topPanel.Controls.Add(cmbMonth);

            Label lblYear = new Label { Text = "Год:", Location = new Point(176, 13), AutoSize = true };
            topPanel.Controls.Add(lblYear);

            NumericUpDown nudYear = new NumericUpDown
            {
                Location = new Point(214, 10),
                Size = new Size(68, 30),
                Minimum = 2020,
                Maximum = 2035,
                Value = _selectedYear
            };
            topPanel.Controls.Add(nudYear);

            Button btnImportTs = CreateActionButton("Импорт табеля", 294, 5, 145, Color.FromArgb(22, 160, 133));
            Button btnCalculate = CreateActionButton("Рассчитать", 447, 5, 130, Color.FromArgb(46, 139, 87));
            Button btnSave = CreateActionButton("Сохранить в БД", 585, 5, 145, Color.FromArgb(52, 152, 219));
            Button btnExport = CreateActionButton("Экспорт в Excel", 738, 5, 145, Color.FromArgb(230, 126, 34));

            btnSave.Enabled = false;
            btnExport.Enabled = false;

            topPanel.Controls.Add(btnImportTs);
            topPanel.Controls.Add(btnCalculate);
            topPanel.Controls.Add(btnSave);
            topPanel.Controls.Add(btnExport);

            DataGridView dgv = new DataGridView();
            dgv.Location = new Point(20, 68);
            dgv.Size = new Size(innerWidth, _contentPanel.ClientSize.Height - 115);
            dgv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dgv.BackgroundColor = Color.White;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgv.AllowUserToAddRows = false;
            dgv.ReadOnly = true;
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgv.RowHeadersVisible = false;
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 52, 54);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            dgv.ColumnHeadersHeight = 36;
            _contentPanel.Controls.Add(dgv);

            Label lblTotals = new Label
            {
                Text = "Импортируйте табель или нажмите «Рассчитать» для начисления зарплаты за выбранный месяц.",
                Location = new Point(20, _contentPanel.ClientSize.Height - 38),
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(45, 52, 54)
            };
            _contentPanel.Controls.Add(lblTotals);

            string[] textCols = { "EmployeeID", "ФИО", "Дни", "Часы" };

            Action<DataTable> applyTableFormatting = (DataTable dt) =>
            {
                dgv.DataSource = dt;
                if (dgv.Columns["EmployeeID"] != null) dgv.Columns["EmployeeID"].Visible = false;

                foreach (DataGridViewColumn col in dgv.Columns)
                {
                    if (!textCols.Contains(col.Name))
                    {
                        col.DefaultCellStyle.Format = col.Name == "Ставка" ? "0.##" : "N2";
                        col.DefaultCellStyle.Alignment = col.Name == "Ставка"
                            ? DataGridViewContentAlignment.MiddleCenter
                            : DataGridViewContentAlignment.MiddleRight;
                    }
                }

                if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].FillWeight = 155;
                if (dgv.Columns["Ставка"] != null) dgv.Columns["Ставка"].FillWeight = 50;
                if (dgv.Columns["Дни"] != null)
                {
                    dgv.Columns["Дни"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dgv.Columns["Дни"].FillWeight = 62;
                }
                if (dgv.Columns["Часы"] != null)
                {
                    dgv.Columns["Часы"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    dgv.Columns["Часы"].FillWeight = 82;
                }
                if (dgv.Columns["К выплате"] != null)
                {
                    dgv.Columns["К выплате"].DefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                    dgv.Columns["К выплате"].DefaultCellStyle.ForeColor = Color.FromArgb(46, 139, 87);
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
                    $"Сотрудников: {dt.Rows.Count}   |   Начислено: {sumGross:N2} руб.   |   " +
                    $"Удержано: {sumDeductions:N2} руб.   |   Итого к выплате: {sumNet:N2} руб.";

                btnSave.Enabled = dt.Rows.Count > 0;
                btnExport.Enabled = dt.Rows.Count > 0;
            };

            Action executeCalculation = () =>
            {
                try
                {
                    btnCalculate.Text = "Расчет...";
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
                        MessageBox.Show("Ошибка расчета: " + response.ErrorMessage, "Ошибка",
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
                    lblTotals.Text = $"Период изменен на {_months[_selectedMonth - 1]} {_selectedYear} г. Нажмите «Рассчитать» или «Импорт табеля».";
                }
            };

            bool suppressPeriodEvents = false;
            cmbMonth.SelectedIndexChanged += (s, e) => { if (!suppressPeriodEvents) onPeriodChanged(); };
            nudYear.ValueChanged += (s, e) => { if (!suppressPeriodEvents) onPeriodChanged(); };

            // Показываем кэш, если он относится к выбранному периоду
            if (_payrollCache != null && _payrollCacheMonth == _selectedMonth && _payrollCacheYear == _selectedYear)
            {
                applyTableFormatting(_payrollCache);
            }

            // Импорт табеля прямо с экрана расчета + мгновенный пересчет зарплаты
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
                int m = cmbMonth.SelectedIndex + 1;
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
                                headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

                                // Добавляем итоговую строку по денежным столбцам
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

        // --- ЭКРАН АНАЛИТИКИ (ГРАФИКИ) ---
        private void LoadAnalyticsScreen()
        {
            _contentPanel.Controls.Clear();
            int innerWidth = _contentPanel.ClientSize.Width - 40;

            Panel topPanel = new Panel();
            topPanel.Location = new Point(20, 12);
            topPanel.Size = new Size(innerWidth, 46);
            topPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(topPanel);

            Label lblMonth = new Label
            {
                Text = "Выберите месяц:",
                Location = new Point(0, 13),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            topPanel.Controls.Add(lblMonth);

            ComboBox cmbMonths = new ComboBox
            {
                Location = new Point(135, 10),
                Size = new Size(140, 30),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMonths.Items.AddRange(_months);
            cmbMonths.SelectedIndex = _selectedMonth - 1;
            topPanel.Controls.Add(cmbMonths);

            Label lblYear = new Label
            {
                Text = "Год:",
                Location = new Point(290, 13),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            topPanel.Controls.Add(lblYear);

            NumericUpDown nudYear = new NumericUpDown
            {
                Location = new Point(330, 10),
                Size = new Size(72, 30),
                Minimum = 2020,
                Maximum = 2035,
                Value = _selectedYear
            };
            topPanel.Controls.Add(nudYear);

            Chart chart = new Chart
            {
                Location = new Point(20, 68),
                Size = new Size(innerWidth, _contentPanel.ClientSize.Height - 88),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White
            };
            ChartArea chartArea = new ChartArea("MainArea") { BackColor = Color.White };
            chart.ChartAreas.Add(chartArea);
            Series series = new Series("Фонд зарплаты")
            {
                ChartType = SeriesChartType.Column,
                Color = Color.FromArgb(46, 139, 87),
                IsValueShownAsLabel = true,
                LabelFormat = "N2"
            };
            chart.Series.Add(series);
            _contentPanel.Controls.Add(chart);

            Action updateChart = () =>
            {
                try
                {
                    chart.Series["Фонд зарплаты"].Points.Clear();
                    int selectedMonth = cmbMonths.SelectedIndex + 1;
                    int selectedYear = (int)nudYear.Value;
                    _selectedMonth = selectedMonth;
                    _selectedYear = selectedYear;

                    var request = new NetworkMessage { Command = "GET_ANALYTICS", Data = $"{selectedMonth}|{selectedYear}" };
                    var response = new ServerConnection().SendRequest(request);

                    if (response.IsSuccess)
                    {
                        DataTable dt = JsonConvert.DeserializeObject<DataTable>(response.Data);
                        if (dt.Rows.Count == 0)
                        {
                            chart.Series["Фонд зарплаты"].Points.AddXY("Нет сохраненных данных", 0);
                        }
                        else
                        {
                            foreach (DataRow row in dt.Rows)
                            {
                                chart.Series["Фонд зарплаты"].Points.AddXY(
                                    row["Кафедра"].ToString(),
                                    Convert.ToDouble(row["СуммаКВыплате"]));
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Ошибка: " + response.ErrorMessage);
                    }
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
            };

            cmbMonths.SelectedIndexChanged += (s, e) => updateChart();
            nudYear.ValueChanged += (s, e) => updateChart();
            updateChart();
        }

        // --- ЭКРАН ПОЛЬЗОВАТЕЛИ ---
        private void LoadUsersScreen()
        {
            _contentPanel.Controls.Clear();
            int innerWidth = _contentPanel.ClientSize.Width - 40;

            Panel topPanel = new Panel
            {
                Location = new Point(20, 15),
                Size = new Size(innerWidth, 45),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _contentPanel.Controls.Add(topPanel);

            Button btnAdd = CreateActionButton("+ Добавить пользователя", 0, 2, 205, Color.FromArgb(46, 139, 87));
            Button btnDelete = CreateActionButton("Удалить выбранного", 215, 2, 180, Color.FromArgb(231, 76, 60));
            topPanel.Controls.Add(btnAdd);
            topPanel.Controls.Add(btnDelete);

            DataGridView dgv = new DataGridView
            {
                Location = new Point(20, 70),
                Size = new Size(innerWidth, _contentPanel.ClientSize.Height - 90),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackgroundColor = Color.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false
            };
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 52, 54);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _contentPanel.Controls.Add(dgv);

            Action loadData = () =>
            {
                try
                {
                    var response = new ServerConnection().SendRequest(new NetworkMessage { Command = "GET_USERS" });
                    if (response.IsSuccess)
                    {
                        dgv.DataSource = JsonConvert.DeserializeObject<DataTable>(response.Data);
                        if (dgv.Columns["UserID"] != null) dgv.Columns["UserID"].Visible = false;
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
                    new string[] { "Логин:", "Пароль:", "Роль (1-Админ, 2-Бухгалтер, 3-Сотрудник):" });

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
                        if (res.IsSuccess) { loadData(); MessageBox.Show("Пользователь добавлен!"); }
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

                if (MessageBox.Show($"Удалить пользователя {login}?", "Подтверждение", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    try
                    {
                        var req = new NetworkMessage { Command = "DELETE_USER", Data = userId.ToString() };
                        var res = new ServerConnection().SendRequest(req);
                        if (res.IsSuccess) { loadData(); MessageBox.Show("Пользователь удален!"); }
                        else MessageBox.Show("Ошибка удаления: " + res.ErrorMessage);
                    }
                    catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
                }
            };
        }

        // Универсальный метод для ввода данных (используется в Пользователях)
        private string[] ShowCustomInputDialog(string title, string[] labels, string[] defaultValues = null)
        {
            Form prompt = new Form()
            {
                Width = 400,
                Height = 150 + (labels.Length * 55),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false
            };

            TextBox[] textBoxes = new TextBox[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                Label lbl = new Label() { Left = 20, Top = 15 + (i * 52), Text = labels[i], AutoSize = true };
                TextBox txt = new TextBox() { Left = 20, Top = 36 + (i * 52), Width = 340 };
                if (defaultValues != null && defaultValues.Length > i) txt.Text = defaultValues[i];
                textBoxes[i] = txt;
                prompt.Controls.Add(lbl);
                prompt.Controls.Add(txt);
            }

            Button confirmation = new Button()
            {
                Text = "OK",
                Left = 260,
                Width = 100,
                Top = prompt.ClientSize.Height - 42,
                DialogResult = DialogResult.OK
            };
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            using (prompt)
            {
                if (prompt.ShowDialog() == DialogResult.OK)
                    return textBoxes.Select(tb => tb.Text).ToArray();
            }

            return null;
        }

        // Метод для точного расчета стажа (годы, месяцы, дни)
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

        // Метод для создания окна с информацией о сотруднике
        private void ShowEmployeeDetails(string fullName, string position, string department, DateTime hireDate)
        {
            using (Form detailsForm = new Form())
            {
                detailsForm.Text = "Информация о сотруднике";
                detailsForm.Size = new Size(450, 300);
                detailsForm.StartPosition = FormStartPosition.CenterParent;
                detailsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                detailsForm.MaximizeBox = false;
                detailsForm.MinimizeBox = false;
                detailsForm.BackColor = Color.FromArgb(240, 244, 248);
                detailsForm.Font = new Font("Segoe UI", 10f);

                string expText = CalculateExactExperience(hireDate);

                Label lblTitle = new Label()
                {
                    Text = fullName,
                    Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(46, 139, 87),
                    Location = new Point(20, 20),
                    AutoSize = true
                };

                Label lblPos = new Label() { Text = $"Должность: {position}", Location = new Point(20, 60), AutoSize = true };
                Label lblDept = new Label() { Text = $"Подразделение: {department}", Location = new Point(20, 90), AutoSize = true };
                Label lblHire = new Label() { Text = $"Дата приема: {hireDate.ToShortDateString()}", Location = new Point(20, 120), AutoSize = true };

                Label lblExpTitle = new Label()
                {
                    Text = "Точный стаж работы в вузе:",
                    Location = new Point(20, 160),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10f, FontStyle.Bold)
                };
                Label lblExpValue = new Label()
                {
                    Text = expText,
                    Location = new Point(20, 185),
                    AutoSize = true,
                    Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(52, 152, 219)
                };

                Button btnOk = new Button()
                {
                    Text = "Закрыть",
                    Location = new Point(310, 210),
                    Size = new Size(100, 35),
                    BackColor = Color.FromArgb(46, 139, 87),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    DialogResult = DialogResult.OK
                };
                btnOk.FlatAppearance.BorderSize = 0;

                detailsForm.Controls.AddRange(new Control[] { lblTitle, lblPos, lblDept, lblHire, lblExpTitle, lblExpValue, btnOk });
                detailsForm.AcceptButton = btnOk;
                detailsForm.ShowDialog();
            }
        }
    }
}

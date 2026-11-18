using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json;
using PayrollClient.Network;
using System.Windows.Forms.DataVisualization.Charting;
using System.Linq;

namespace PayrollClient
{
    public partial class MainForm : Form
    {
        private string _userRole;
        private Panel _contentPanel;
        private Button _activeButton;

        private DataTable _payrollCache = null; // Кэш для хранения последнего расчета

        private string[] _months = { "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
                                     "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь" };

        public MainForm(string role)
        {
            _userRole = role;
            this.Text = $"ПолесГУ: Система начисления зарплаты | {_userRole}";
            this.Size = new Size(1150, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 244, 248);
            this.Font = new Font("Segoe UI", 10f);

            // --- ЛЕВАЯ ПАНЕЛЬ ---
            Panel sidebar = new Panel();
            sidebar.Location = new Point(0, 0);
            sidebar.Size = new Size(250, this.ClientSize.Height);
            sidebar.BackColor = Color.FromArgb(45, 52, 54);
            sidebar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            this.Controls.Add(sidebar);

            // --- ПРАВАЯ ПАНЕЛЬ ---
            _contentPanel = new Panel();
            _contentPanel.Location = new Point(250, 0);
            _contentPanel.Size = new Size(this.ClientSize.Width - 250, this.ClientSize.Height);
            _contentPanel.BackColor = Color.FromArgb(240, 244, 248);
            _contentPanel.Padding = new Padding(20);
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

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ МЕНЮ ---
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

        // --- ГЛАВНАЯ СТРАНИЦА (ДАШБОРД) ---
        private void ShowWelcomeScreen()
        {
            _contentPanel.Controls.Clear();

            Label welcome = new Label();
            welcome.Text = $"Добро пожаловать, {_userRole}!";
            welcome.Font = new Font("Segoe UI", 24f, FontStyle.Bold);
            welcome.ForeColor = Color.FromArgb(45, 52, 54);
            welcome.Location = new Point(0, 0);
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
                    CreateStatCard("Сотрудников в базе", stats[0], 0, 60);
                    CreateStatCard("Пользователей системы", stats[1], 280, 60);
                    CreateStatCard("Общий фонд (руб.)", Math.Round(decimal.Parse(stats[2]), 2).ToString(), 0, 200);
                }
            }
            catch (Exception) { /* Игнорируем ошибки, если сервер недоступен */ }
        }

        private void CreateStatCard(string title, string value, int x, int y)
        {
            Panel card = new Panel();
            card.Location = new Point(x, y);
            card.Size = new Size(250, 110);
            card.BackColor = Color.White;

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.ForeColor = Color.Gray;
            lblTitle.Location = new Point(15, 15);
            lblTitle.AutoSize = true;
            card.Controls.Add(lblTitle);

            Label lblValue = new Label();
            lblValue.Text = value;
            lblValue.Font = new Font("Segoe UI", 24f, FontStyle.Bold);
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

            Panel topPanel = new Panel();
            topPanel.Location = new Point(0, 0);
            topPanel.Size = new Size(_contentPanel.ClientSize.Width, 50);
            topPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(topPanel);

            Button btnAddEmp = new Button()
            {
                Text = "+ Принять на работу",
                Size = new Size(180, 40),
                Location = new Point(0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(46, 139, 87),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAddEmp.FlatAppearance.BorderSize = 0;
            topPanel.Controls.Add(btnAddEmp);

            Button btnEditEmp = new Button()
            {
                Text = "Редактировать",
                Size = new Size(140, 40),
                Location = new Point(190, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 152, 219),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnEditEmp.FlatAppearance.BorderSize = 0;
            topPanel.Controls.Add(btnEditEmp);

            Button btnDeleteEmp = new Button()
            {
                Text = "Уволить",
                Size = new Size(120, 40),
                Location = new Point(340, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDeleteEmp.FlatAppearance.BorderSize = 0;
            topPanel.Controls.Add(btnDeleteEmp);

            Button btnDetails = new Button()
            {
                Text = "Подробнее",
                Size = new Size(120, 40),
                Location = new Point(470, 0), // Справа от кнопки "Уволить"
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(155, 89, 182),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDetails.FlatAppearance.BorderSize = 0;
            topPanel.Controls.Add(btnDetails);

            DataGridView dgv = new DataGridView();
            dgv.Location = new Point(0, 60);
            dgv.Size = new Size(_contentPanel.ClientSize.Width, _contentPanel.ClientSize.Height - 60);
            // ВОЗВРАЩАЕМ ANCHOR ДЛЯ ТАБЛИЦЫ
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

                        if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].Width = 200;
                        if (dgv.Columns["Кафедра"] != null) dgv.Columns["Кафедра"].Width = 200;
                        if (dgv.Columns["Должность"] != null) dgv.Columns["Должность"].Width = 150;
                        if (dgv.Columns["Стаж"] != null) dgv.Columns["Стаж"].Width = 50;
                        if (dgv.Columns["Ставка"] != null) dgv.Columns["Ставка"].Width = 60; // НОВАЯ КОЛОНКА
                        if (dgv.Columns["Тип занятости"] != null) dgv.Columns["Тип занятости"].Width = 150; // НОВАЯ КОЛОНКА

                        if (dgv.Columns["Дата приема"] != null)
                            dgv.Columns["Дата приема"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    }
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
            };
            loadData();

            btnAddEmp.Click += (s, e) => { using (var form = new AddEmployeeForm()) { if (form.ShowDialog() == DialogResult.OK) loadData(); } };
            btnEditEmp.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите сотрудника!"); return; }
                int empId = Convert.ToInt32(dgv.SelectedRows[0].Cells["EmployeeID"].Value);
                string fullName = dgv.SelectedRows[0].Cells["ФИО"].Value.ToString();
                int posId = Convert.ToInt32(dgv.SelectedRows[0].Cells["PositionID"].Value);
                int deptId = Convert.ToInt32(dgv.SelectedRows[0].Cells["DepartmentID"].Value);
                using (var form = new AddEmployeeForm(empId, fullName, posId, deptId)) { if (form.ShowDialog() == DialogResult.OK) loadData(); }
            };
            btnDeleteEmp.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите сотрудника!"); return; }
                int empId = Convert.ToInt32(dgv.SelectedRows[0].Cells["EmployeeID"].Value);
                string fullName = dgv.SelectedRows[0].Cells["ФИО"].Value.ToString();
                if (MessageBox.Show($"Вы уверены, что хотите уволить {fullName}?", "Увольнение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    var res = new ServerConnection().SendRequest(new NetworkMessage { Command = "DELETE_EMPLOYEE", Data = empId.ToString() });
                    if (res.IsSuccess) { loadData(); MessageBox.Show("Сотрудник уволен!"); }
                }
            };
            btnDetails.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите сотрудника!"); return; }

                // Получаем данные выбранной строки
                string fullName = dgv.SelectedRows[0].Cells["ФИО"].Value.ToString();
                string position = dgv.SelectedRows[0].Cells["Должность"].Value.ToString();
                string department = dgv.SelectedRows[0].Cells["Кафедра"].Value.ToString();
                DateTime hireDate = Convert.ToDateTime(dgv.SelectedRows[0].Cells["Дата приема"].Value);

                // Вызываем метод для показа деталей
                ShowEmployeeDetails(fullName, position, department, hireDate);
            };
        }

        // --- ЭКРАН РАСЧЕТА ЗАРПЛАТЫ ---
        private void LoadPayrollScreen()
        {
            _contentPanel.Controls.Clear();

            Panel topPanel = new Panel();
            topPanel.Location = new Point(0, 0);
            topPanel.Size = new Size(_contentPanel.ClientSize.Width, 60);
            topPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(topPanel);

            Label lblMonth = new Label();
            lblMonth.Text = "Месяц расчета:";
            lblMonth.Location = new Point(0, 17);
            lblMonth.AutoSize = true;
            topPanel.Controls.Add(lblMonth);

            ComboBox cmbMonth = new ComboBox();
            cmbMonth.Location = new Point(115, 14);
            cmbMonth.Size = new Size(110, 30);
            cmbMonth.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMonth.Items.AddRange(_months);
            cmbMonth.SelectedIndex = DateTime.Now.Month - 1;
            topPanel.Controls.Add(cmbMonth);

            Button btnCalculate = new Button();
            btnCalculate.Text = "Выполнить";
            btnCalculate.Size = new Size(140, 40);
            btnCalculate.Location = new Point(235, 10);
            btnCalculate.FlatStyle = FlatStyle.Flat;
            btnCalculate.FlatAppearance.BorderSize = 0;
            btnCalculate.BackColor = Color.FromArgb(46, 139, 87);
            btnCalculate.ForeColor = Color.White;
            btnCalculate.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnCalculate.Cursor = Cursors.Hand;
            btnCalculate.TextAlign = ContentAlignment.MiddleCenter;
            topPanel.Controls.Add(btnCalculate);

            Button btnSave = new Button();
            btnSave.Text = "Сохранить в БД";
            btnSave.Size = new Size(140, 40);
            btnSave.Location = new Point(385, 10);
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.BackColor = Color.FromArgb(52, 152, 219);
            btnSave.ForeColor = Color.White;
            btnSave.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnSave.Cursor = Cursors.Hand;
            btnSave.TextAlign = ContentAlignment.MiddleCenter;
            btnSave.Enabled = false;
            topPanel.Controls.Add(btnSave);

            Button btnExport = new Button();
            btnExport.Text = "Экспорт в Excel";
            btnExport.Size = new Size(140, 40);
            btnExport.Location = new Point(535, 10);
            btnExport.FlatStyle = FlatStyle.Flat;
            btnExport.FlatAppearance.BorderSize = 0;
            btnExport.BackColor = Color.FromArgb(230, 126, 34);
            btnExport.ForeColor = Color.White;
            btnExport.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnExport.Cursor = Cursors.Hand;
            btnExport.TextAlign = ContentAlignment.MiddleCenter;
            btnExport.Enabled = false;
            topPanel.Controls.Add(btnExport);

            DataGridView dgv = new DataGridView();
            dgv.Location = new Point(0, 70);
            dgv.Size = new Size(_contentPanel.ClientSize.Width, _contentPanel.ClientSize.Height - 70);
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
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _contentPanel.Controls.Add(dgv);

            // ВОТ ЗДЕСЬ ИСПРАВЛЕНИЕ: Action заменен на Action<DataTable>
            Action<DataTable> applyTableFormatting = (DataTable dt) =>
            {
                dgv.DataSource = dt;
                if (dgv.Columns["EmployeeID"] != null) dgv.Columns["EmployeeID"].Visible = false;

                foreach (DataGridViewColumn col in dgv.Columns)
                    if (col.Name != "ФИО" && col.Name != "EmployeeID") col.DefaultCellStyle.Format = "N2";

                if (dgv.Columns["ФИО"] != null) dgv.Columns["ФИО"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

                btnSave.Enabled = true;
                btnExport.Enabled = true;
            };

            // Если данные уже были рассчитаны ранее, показываем их сразу!
            if (_payrollCache != null)
            {
                applyTableFormatting(_payrollCache);
            }

            btnCalculate.Click += (s, e) =>
            {
                try
                {
                    btnCalculate.Text = "Расчет...";
                    btnCalculate.Enabled = false;

                    int selectedMonth = cmbMonth.SelectedIndex + 1;
                    var request = new NetworkMessage { Command = "CALCULATE_PAYROLL", Data = selectedMonth.ToString() };
                    var response = new ServerConnection().SendRequest(request);

                    if (response.IsSuccess)
                    {
                        DataTable dt = JsonConvert.DeserializeObject<DataTable>(response.Data);
                        _payrollCache = dt; // СОХРАНЯЕМ В КЭШ
                        applyTableFormatting(dt);
                    }
                    else MessageBox.Show("Ошибка: " + response.ErrorMessage);
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
                finally { btnCalculate.Text = "Выполнить"; btnCalculate.Enabled = true; }
            };

            btnSave.Click += (s, e) =>
            {
                try
                {
                    DataTable currentTable = (DataTable)dgv.DataSource;
                    if (currentTable == null) return;

                    int selectedMonth = cmbMonth.SelectedIndex + 1;
                    string jsonData = JsonConvert.SerializeObject(currentTable);
                    var request = new NetworkMessage { Command = "SAVE_PAYROLL", Data = $"{selectedMonth}|{jsonData}" };
                    var response = new ServerConnection().SendRequest(request);

                    if (response.IsSuccess) MessageBox.Show(response.Data, "Успех");
                    else MessageBox.Show("Ошибка: " + response.ErrorMessage);
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
            };

            btnExport.Click += (s, e) =>
            {
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Filter = "Excel Workbook (*.xlsx)|*.xlsx";
                sfd.FileName = $"Ведомость_{cmbMonth.Text}_2026.xlsx";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        DataTable dt = (DataTable)dgv.DataSource;
                        if (dt == null) return;

                        DataTable exportDt = dt.Copy();
                        if (exportDt.Columns.Contains("EmployeeID")) exportDt.Columns.Remove("EmployeeID");

                        using (var workbook = new ClosedXML.Excel.XLWorkbook())
                        {
                            var worksheet = workbook.Worksheets.Add(exportDt, "Расчет");
                            var headerRange = worksheet.Range(1, 1, 1, exportDt.Columns.Count);
                            headerRange.Style.Font.Bold = true;
                            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                            worksheet.Columns().AdjustToContents();
                            workbook.SaveAs(sfd.FileName);
                        }
                        MessageBox.Show("Excel-отчет успешно сохранен!", "Успех");
                    }
                    catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
                }
            };
        }

        // --- ЭКРАН АНАЛИТИКИ (ГРАФИКИ) ---
        private void LoadAnalyticsScreen()
        {
            _contentPanel.Controls.Clear();
            Panel topPanel = new Panel();
            topPanel.Location = new Point(0, 0);
            topPanel.Size = new Size(_contentPanel.ClientSize.Width, 50);
            topPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _contentPanel.Controls.Add(topPanel);

            Label lblMonth = new Label() { Text = "Выберите месяц:", Location = new Point(0, 15), AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            topPanel.Controls.Add(lblMonth);

            ComboBox cmbMonths = new ComboBox() { Location = new Point(130, 12), Size = new Size(150, 30), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbMonths.Items.AddRange(_months);
            cmbMonths.SelectedIndex = DateTime.Now.Month - 1;
            topPanel.Controls.Add(cmbMonths);

            Chart chart = new Chart() { Location = new Point(0, 60), Size = new Size(_contentPanel.ClientSize.Width, _contentPanel.ClientSize.Height - 60), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.White };
            ChartArea chartArea = new ChartArea("MainArea") { BackColor = Color.White };
            chart.ChartAreas.Add(chartArea);
            Series series = new Series("Фонд зарплаты") { ChartType = SeriesChartType.Column, Color = Color.FromArgb(46, 139, 87), IsValueShownAsLabel = true };
            chart.Series.Add(series);
            _contentPanel.Controls.Add(chart);

            Action updateChart = () =>
            {
                try
                {
                    chart.Series["Фонд зарплаты"].Points.Clear();
                    int selectedMonth = cmbMonths.SelectedIndex + 1;
                    var request = new NetworkMessage { Command = "GET_ANALYTICS", Data = selectedMonth.ToString() };
                    var response = new ServerConnection().SendRequest(request);

                    if (response.IsSuccess)
                    {
                        DataTable dt = JsonConvert.DeserializeObject<DataTable>(response.Data);
                        if (dt.Rows.Count == 0) chart.Series["Фонд зарплаты"].Points.AddXY("Нет данных", 0);
                        else
                        {
                            foreach (DataRow row in dt.Rows)
                                chart.Series["Фонд зарплаты"].Points.AddXY(row["Кафедра"].ToString(), Convert.ToDouble(row["СуммаКВыплате"]));
                        }
                    }
                }
                catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
            };
            cmbMonths.SelectedIndexChanged += (s, e) => updateChart();
            updateChart();
        }

        // --- ЭКРАН ПОЛЬЗОВАТЕЛИ ---
        private void LoadUsersScreen()
        {
            _contentPanel.Controls.Clear();
            Panel topPanel = new Panel() { Location = new Point(0, 0), Size = new Size(_contentPanel.ClientSize.Width, 50), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _contentPanel.Controls.Add(topPanel);

            Button btnAdd = new Button() { Text = "+ Добавить пользователя", Size = new Size(200, 40), Location = new Point(0, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(46, 139, 87), ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnAdd.FlatAppearance.BorderSize = 0;
            topPanel.Controls.Add(btnAdd);

            Button btnDelete = new Button() { Text = "Удалить выбранного", Size = new Size(180, 40), Location = new Point(210, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(231, 76, 60), ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Cursor = Cursors.Hand };
            btnDelete.FlatAppearance.BorderSize = 0;
            topPanel.Controls.Add(btnDelete);

            DataGridView dgv = new DataGridView() { Location = new Point(0, 60), Size = new Size(_contentPanel.ClientSize.Width, _contentPanel.ClientSize.Height - 60), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false };
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 52, 54);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _contentPanel.Controls.Add(dgv);

            Action loadData = () => {
                var response = new ServerConnection().SendRequest(new NetworkMessage { Command = "GET_USERS" });
                if (response.IsSuccess)
                {
                    dgv.DataSource = JsonConvert.DeserializeObject<DataTable>(response.Data);
                    if (dgv.Columns["UserID"] != null) dgv.Columns["UserID"].Visible = false;
                }
            };
            loadData();

            btnAdd.Click += (s, e) => {
                string[] result = ShowCustomInputDialog("Новый пользователь",
                    new string[] { "Логин:", "Пароль:", "Роль (1-Админ, 2-Бухгалтер, 3-Сотрудник):" });

                if (result != null && result.Length == 3)
                {
                    var req = new NetworkMessage { Command = "ADD_USER", Data = $"{result[0]}|{result[1]}|{result[2]}" };
                    var res = new ServerConnection().SendRequest(req);
                    if (res.IsSuccess) { loadData(); MessageBox.Show("Пользователь добавлен!"); }
                    else MessageBox.Show("Ошибка (возможно, логин занят)");
                }
            };

            btnDelete.Click += (s, e) => {
                if (dgv.SelectedRows.Count == 0) { MessageBox.Show("Выберите пользователя!"); return; }
                int userId = Convert.ToInt32(dgv.SelectedRows[0].Cells["UserID"].Value);
                string login = dgv.SelectedRows[0].Cells["Login"].Value.ToString();

                if (MessageBox.Show($"Удалить пользователя {login}?", "Подтверждение", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    var req = new NetworkMessage { Command = "DELETE_USER", Data = userId.ToString() };
                    var res = new ServerConnection().SendRequest(req);
                    if (res.IsSuccess) { loadData(); MessageBox.Show("Пользователь удален!"); }
                    else MessageBox.Show("Ошибка удаления");
                }
            };
        }
        // Универсальный метод для красивого ввода данных (используется в Пользователях)
        private string[] ShowCustomInputDialog(string title, string[] labels, string[] defaultValues = null)
        {
            Form prompt = new Form()
            {
                Width = 400,
                Height = 150 + (labels.Length * 40),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = title,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false
            };

            TextBox[] textBoxes = new TextBox[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                Label lbl = new Label() { Left = 20, Top = 20 + (i * 40), Text = labels[i], AutoSize = true };
                TextBox txt = new TextBox() { Left = 20, Top = 40 + (i * 40), Width = 340 };
                if (defaultValues != null && defaultValues.Length > i) txt.Text = defaultValues[i];
                textBoxes[i] = txt;
                prompt.Controls.Add(lbl);
                prompt.Controls.Add(txt);
            }

            Button confirmation = new Button() { Text = "OK", Left = 260, Width = 100, Top = prompt.Height - 70, DialogResult = DialogResult.OK };
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            if (prompt.ShowDialog() == DialogResult.OK)
                return textBoxes.Select(tb => tb.Text).ToArray();

            return null;
        }
        // Метод для точного расчета стажа (годы, месяцы, дни)
        private string CalculateExactExperience(DateTime startDate)
        {
            DateTime endDate = DateTime.Today;

            int years = endDate.Year - startDate.Year;
            int months = endDate.Month - startDate.Month;
            int days = endDate.Day - startDate.Day;

            if (days < 0)
            {
                months--;
                // Берем количество дней в предыдущем месяце
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

        // Метод для создания окна с информацией
        private void ShowEmployeeDetails(string fullName, string position, string department, DateTime hireDate)
        {
            Form detailsForm = new Form();
            detailsForm.Text = "Информация о сотруднике";
            detailsForm.Size = new Size(450, 300);
            detailsForm.StartPosition = FormStartPosition.CenterParent;
            detailsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            detailsForm.MaximizeBox = false;
            detailsForm.MinimizeBox = false;
            detailsForm.BackColor = Color.FromArgb(240, 244, 248);
            detailsForm.Font = new Font("Segoe UI", 10f);

            string expText = CalculateExactExperience(hireDate);

            Label lblTitle = new Label() { Text = fullName, Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Color.FromArgb(46, 139, 87), Location = new Point(20, 20), AutoSize = true };

            Label lblPos = new Label() { Text = $"Должность: {position}", Location = new Point(20, 60), AutoSize = true };
            Label lblDept = new Label() { Text = $"Подразделение: {department}", Location = new Point(20, 90), AutoSize = true };
            Label lblHire = new Label() { Text = $"Дата приема: {hireDate.ToShortDateString()}", Location = new Point(20, 120), AutoSize = true };

            Label lblExpTitle = new Label() { Text = "Точный стаж работы в вузе:", Location = new Point(20, 160), AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            Label lblExpValue = new Label() { Text = expText, Location = new Point(20, 185), AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(52, 152, 219) };

            Button btnOk = new Button() { Text = "Закрыть", Location = new Point(310, 210), Size = new Size(100, 35), BackColor = Color.FromArgb(46, 139, 87), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            btnOk.FlatAppearance.BorderSize = 0;

            detailsForm.Controls.AddRange(new Control[] { lblTitle, lblPos, lblDept, lblHire, lblExpTitle, lblExpValue, btnOk });
            detailsForm.ShowDialog();
        }
    }
}
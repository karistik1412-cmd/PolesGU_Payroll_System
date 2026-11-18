using System;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Newtonsoft.Json;
using PayrollClient.Network;
using PayrollClient.Services;

namespace PayrollClient
{
    public partial class AddEmployeeForm : Form
    {
        private TextBox txtFullName;
        private ComboBox cmbCategory;
        private ComboBox cmbPositions;
        private ComboBox cmbDepartments;
        private ComboBox cmbRate;
        private ComboBox cmbEmployment;
        private Button btnSave;
        private Button btnCancel;

        private DataTable _allPositions;
        private DataTable _allDepartments;
        private readonly int? _editId = null;

        public AddEmployeeForm()
        {
            InitializeForm("Приём сотрудника на работу", "Сохранить");
            LoadDictionaries();
        }

        public AddEmployeeForm(int empId, string fullName, int posId, int deptId, decimal? rate = null, string empType = null)
        {
            _editId = empId;
            InitializeForm("Редактирование сотрудника", "Обновить");
            txtFullName.Text = fullName;
            LoadDictionaries();

            int catId = 1;
            if (_allPositions != null)
            {
                DataRow[] rows = _allPositions.Select($"PositionID = {posId}");
                if (rows.Length > 0 && rows[0]["Category"] != DBNull.Value)
                    catId = Convert.ToInt32(rows[0]["Category"]);
            }
            if (catId >= 1 && catId <= cmbCategory.Items.Count)
                cmbCategory.SelectedIndex = catId - 1;
            if (posId != -1) cmbPositions.SelectedValue = posId;
            if (deptId != -1) cmbDepartments.SelectedValue = deptId;

            if (rate.HasValue)
            {
                for (int i = 0; i < cmbRate.Items.Count; i++)
                {
                    string itemText = cmbRate.Items[i].ToString().Replace(',', '.');
                    decimal itemVal;
                    if (decimal.TryParse(itemText, NumberStyles.Float, CultureInfo.InvariantCulture, out itemVal)
                        && Math.Abs(itemVal - rate.Value) < 0.01m)
                    {
                        cmbRate.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(empType))
            {
                int idx = cmbEmployment.Items.IndexOf(empType.Trim());
                if (idx >= 0) cmbEmployment.SelectedIndex = idx;
            }
        }

        private void InitializeForm(string title, string btnText)
        {
            this.Text = title;
            this.ClientSize = new Size(440, 445);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = UiTheme.CardBg;
            this.Font = new Font("Segoe UI", 9.75f);

            // Фирменная зелёная шапка ПолесГУ
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = UiTheme.SidebarBg
            };
            Label lblHeader = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(22, 14),
                AutoSize = true
            };
            header.Controls.Add(lblHeader);
            this.Controls.Add(header);

            Font labelFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            int x = 24;
            int w = 392;

            Label lblName = new Label { Text = "ФИО сотрудника", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 70), AutoSize = true };
            txtFullName = new TextBox { Font = new Font("Segoe UI", 10f) };
            Panel nameBox = UiTheme.WrapTextBox(txtFullName, x, 92, w, 34);

            Label lblCategory = new Label { Text = "Категория персонала", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 136), AutoSize = true };
            cmbCategory = new ComboBox
            {
                Location = new Point(x, 158),
                Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };
            cmbCategory.Items.Add("Преподавательский состав");
            cmbCategory.Items.Add("Прочий персонал (служащие)");
            cmbCategory.SelectedIndex = 0;
            cmbCategory.SelectedIndexChanged += CategoryChanged;

            Label lblPos = new Label { Text = "Должность", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 198), AutoSize = true };
            cmbPositions = new ComboBox
            {
                Location = new Point(x, 220),
                Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };

            Label lblDept = new Label { Text = "Подразделение / кафедра", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 260), AutoSize = true };
            cmbDepartments = new ComboBox
            {
                Location = new Point(x, 282),
                Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };

            Label lblRate = new Label { Text = "Ставка", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 322), AutoSize = true };
            cmbRate = new ComboBox
            {
                Location = new Point(x, 344),
                Width = 115,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };
            cmbRate.Items.AddRange(new object[] { "0,25", "0,5", "0,75", "1,0", "1,5" });
            cmbRate.SelectedIndex = 3;

            Label lblEmp = new Label { Text = "Тип занятости", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x + 132, 322), AutoSize = true };
            cmbEmployment = new ComboBox
            {
                Location = new Point(x + 132, 344),
                Width = w - 132,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };
            cmbEmployment.Items.AddRange(new object[] { "Основное", "Внутреннее совместительство" });
            cmbEmployment.SelectedIndex = 0;

            btnCancel = UiTheme.CreateOutlineButton("Отмена", x + w - 232, 392, 108, 36);
            btnCancel.DialogResult = DialogResult.Cancel;

            btnSave = UiTheme.CreatePrimaryButton(btnText, x + w - 116, 392, 116, 36);
            btnSave.Click += BtnSave_Click;

            this.Controls.AddRange(new Control[]
            {
                lblName, nameBox, lblCategory, cmbCategory, lblPos, cmbPositions,
                lblDept, cmbDepartments, lblRate, cmbRate, lblEmp, cmbEmployment,
                btnCancel, btnSave
            });

            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;
        }

        private void LoadDictionaries()
        {
            try
            {
                var conn = new ServerConnection();
                var posRes = conn.SendRequest(new NetworkMessage { Command = "GET_POSITIONS" });
                if (posRes.IsSuccess) _allPositions = JsonConvert.DeserializeObject<DataTable>(posRes.Data);

                var deptRes = conn.SendRequest(new NetworkMessage { Command = "GET_DEPARTMENTS" });
                if (deptRes.IsSuccess) _allDepartments = JsonConvert.DeserializeObject<DataTable>(deptRes.Data);

                CategoryChanged(null, null);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка загрузки справочников: " + ex.Message); }
        }

        private void CategoryChanged(object sender, EventArgs e)
        {
            if (_allPositions == null || _allDepartments == null) return;
            if (!_allPositions.Columns.Contains("Category") || !_allDepartments.Columns.Contains("DepartmentType")) return;

            int catId = cmbCategory.SelectedIndex + 1;

            _allPositions.DefaultView.RowFilter = $"Category = {catId}";
            cmbPositions.DataSource = _allPositions.DefaultView;
            cmbPositions.DisplayMember = "PositionName";
            cmbPositions.ValueMember = "PositionID";

            int deptTypeId = (catId == 1) ? 1 : 2;
            _allDepartments.DefaultView.RowFilter = $"DepartmentType = {deptTypeId}";
            cmbDepartments.DataSource = _allDepartments.DefaultView;
            cmbDepartments.DisplayMember = "DepartmentName";
            cmbDepartments.ValueMember = "DepartmentID";
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtFullName.Text) || cmbPositions.SelectedValue == null || cmbDepartments.SelectedValue == null)
            {
                MessageBox.Show("Заполните все поля!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                int posId = Convert.ToInt32(cmbPositions.SelectedValue);
                int deptId = Convert.ToInt32(cmbDepartments.SelectedValue);
                string name = txtFullName.Text.Trim();

                string rateStr = cmbRate.Text.Replace(',', '.');
                decimal rate = Convert.ToDecimal(rateStr, CultureInfo.InvariantCulture);
                string rateForServer = rate.ToString(CultureInfo.InvariantCulture);
                string empType = cmbEmployment.Text;

                NetworkMessage request = _editId.HasValue
                    ? new NetworkMessage { Command = "UPDATE_EMPLOYEE", Data = $"{_editId.Value}|{name}|{posId}|{deptId}|{rateForServer}|{empType}" }
                    : new NetworkMessage { Command = "ADD_EMPLOYEE", Data = $"{name}|{posId}|{deptId}|{rateForServer}|{empType}" };

                var res = new ServerConnection().SendRequest(request);

                if (res.IsSuccess)
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Ошибка сервера: " + res.ErrorMessage, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
        }
    }
}

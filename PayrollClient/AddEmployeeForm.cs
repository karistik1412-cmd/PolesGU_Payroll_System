using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json;
using PayrollClient.Network;

namespace PayrollClient
{
    public partial class AddEmployeeForm : Form
    {
        private TextBox txtFullName;
        private ComboBox cmbCategory;
        private ComboBox cmbPositions;
        private ComboBox cmbDepartments;
        private ComboBox cmbRate; // НОВЫЙ СПИСОК
        private ComboBox cmbEmployment; // НОВЫЙ СПИСОК
        private Button btnSave;

        private DataTable _allPositions;
        private DataTable _allDepartments;
        private int? _editId = null;

        public AddEmployeeForm()
        {
            InitializeForm("Прием на работу", "Сохранить");
            LoadDictionaries();
        }

        public AddEmployeeForm(int empId, string fullName, int posId, int deptId, decimal? rate = null, string empType = null)
        {
            _editId = empId;
            InitializeForm("Редактирование данных", "Обновить");
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
                    if (decimal.TryParse(itemText, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out itemVal)
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
            this.Size = new Size(420, 480); // Увеличили высоту
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(240, 244, 248);
            this.Font = new Font("Segoe UI", 10f);

            Label lblName = new Label() { Text = "ФИО сотрудника:", Location = new Point(20, 20), AutoSize = true };
            txtFullName = new TextBox() { Location = new Point(20, 45), Width = 360 };

            Label lblCategory = new Label() { Text = "Категория персонала:", Location = new Point(20, 80), AutoSize = true };
            cmbCategory = new ComboBox() { Location = new Point(20, 105), Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCategory.Items.Add("Преподавательский состав");
            cmbCategory.Items.Add("Прочий персонал (служащие)");
            cmbCategory.SelectedIndex = 0;
            cmbCategory.SelectedIndexChanged += CategoryChanged;

            Label lblPos = new Label() { Text = "Должность:", Location = new Point(20, 140), AutoSize = true };
            cmbPositions = new ComboBox() { Location = new Point(20, 165), Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };

            Label lblDept = new Label() { Text = "Подразделение:", Location = new Point(20, 200), AutoSize = true };
            cmbDepartments = new ComboBox() { Location = new Point(20, 225), Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };

            // ВЫБОР СТАВКИ
            Label lblRate = new Label() { Text = "Ставка:", Location = new Point(20, 260), AutoSize = true };
            cmbRate = new ComboBox() { Location = new Point(20, 285), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbRate.Items.AddRange(new object[] { "0,25", "0,5", "0,75", "1,0", "1,5" });
            cmbRate.SelectedIndex = 3; // По умолчанию 1.0

            // ВЫБОР ТИПА ЗАНЯТОСТИ
            Label lblEmp = new Label() { Text = "Тип занятости:", Location = new Point(140, 260), AutoSize = true };
            cmbEmployment = new ComboBox() { Location = new Point(140, 285), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbEmployment.Items.AddRange(new object[] { "Основное", "Внутреннее совместительство" });
            cmbEmployment.SelectedIndex = 0;

            btnSave = new Button() { Text = btnText, Location = new Point(280, 340), Size = new Size(100, 35), BackColor = Color.FromArgb(46, 139, 87), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;

            this.Controls.AddRange(new Control[] { lblName, txtFullName, lblCategory, cmbCategory, lblPos, cmbPositions, lblDept, cmbDepartments, lblRate, cmbRate, lblEmp, cmbEmployment, btnSave });
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
                string name = txtFullName.Text;


                string rateStr = cmbRate.Text.Replace(',', '.');
                decimal rate = Convert.ToDecimal(rateStr, System.Globalization.CultureInfo.InvariantCulture);
                string empType = cmbEmployment.Text;

                NetworkMessage request;

                if (_editId.HasValue)
                {
                    // При редактировании тоже передаем ставку и тип!
                    string rateForServer = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    request = new NetworkMessage { Command = "UPDATE_EMPLOYEE", Data = $"{_editId.Value}|{name}|{posId}|{deptId}|{rateForServer}|{empType}" };
                }
                else
                {
                    // При передаче тоже используем InvariantCulture, чтобы сервер понял
                    string rateForServer = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    request = new NetworkMessage { Command = "ADD_EMPLOYEE", Data = $"{name}|{posId}|{deptId}|{rateForServer}|{empType}" };
                }

                var res = new ServerConnection().SendRequest(request);

                if (res.IsSuccess)
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else { MessageBox.Show("Ошибка сервера.", "Ошибка"); }
            }
            catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
        }
    }
}
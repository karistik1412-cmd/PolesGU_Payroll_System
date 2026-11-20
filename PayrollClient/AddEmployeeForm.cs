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
        private Label lblDept;
        private ComboBox cmbDepartments;
        private ComboBox cmbRate;
        private ComboBox cmbEmployment;
        private ComboBox cmbBenefitCategory;
        private ComboBox cmbChildrenBenefit;
        private Button btnSave;
        private Button btnCancel;

        private DataTable _allPositions;
        private DataTable _allDepartments;
        private readonly int? _editId = null;
        private bool _suppressAutoRate = false;

        public AddEmployeeForm()
        {
            InitializeForm("Приём сотрудника на работу", "Сохранить");
            LoadDictionaries();
        }

        // Быстрое оформление внутреннего совместительства для выбранного сотрудника
        public AddEmployeeForm(string fullName, int posId, int deptId, int benefitCategory, int childrenBenefit)
        {
            _suppressAutoRate = true;
            InitializeForm("Оформление внутреннего совместительства", "Сохранить");
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

            cmbEmployment.SelectedIndex = 1; // Внутреннее совместительство
            cmbRate.SelectedIndex = 1;       // 0,5 ставки

            if (benefitCategory >= 0 && benefitCategory < cmbBenefitCategory.Items.Count)
                cmbBenefitCategory.SelectedIndex = benefitCategory;
            if (childrenBenefit >= 0 && childrenBenefit < cmbChildrenBenefit.Items.Count)
                cmbChildrenBenefit.SelectedIndex = childrenBenefit;

            _suppressAutoRate = false;
        }

        public AddEmployeeForm(
            int empId,
            string fullName,
            int posId,
            int deptId,
            decimal? rate = null,
            string empType = null,
            int benefitCategory = 0,
            int childrenBenefit = 0)
        {
            _editId = empId;
            _suppressAutoRate = true;

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

            if (!string.IsNullOrWhiteSpace(empType))
            {
                if (empType.IndexOf("совмест", StringComparison.OrdinalIgnoreCase) >= 0)
                    cmbEmployment.SelectedIndex = 1;
                else
                    cmbEmployment.SelectedIndex = 0;
            }

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

            if (benefitCategory >= 0 && benefitCategory < cmbBenefitCategory.Items.Count)
                cmbBenefitCategory.SelectedIndex = benefitCategory;

            if (childrenBenefit >= 0 && childrenBenefit < cmbChildrenBenefit.Items.Count)
                cmbChildrenBenefit.SelectedIndex = childrenBenefit;

            _suppressAutoRate = false;
        }

        private void InitializeForm(string title, string btnText)
        {
            this.Text = title;
            this.ClientSize = new Size(460, 565);
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
            int w = 412;

            // ФИО
            Label lblName = new Label { Text = "ФИО сотрудника", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 68), AutoSize = true };
            txtFullName = new TextBox { Font = new Font("Segoe UI", 10f) };
            Panel nameBox = UiTheme.WrapTextBox(txtFullName, x, 90, w, 34);

            // Категория персонала
            Label lblCategory = new Label { Text = "Категория персонала", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 134), AutoSize = true };
            cmbCategory = CreateComboBox(x, 156, w);
            cmbCategory.Items.Add("Преподавательский состав");
            cmbCategory.Items.Add("Прочий персонал (служащие)");
            cmbCategory.SelectedIndex = 0;
            cmbCategory.SelectedIndexChanged += CategoryChanged;

            // Должность
            Label lblPos = new Label { Text = "Должность", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 194), AutoSize = true };
            cmbPositions = CreateComboBox(x, 216, w);

            // Кафедра (для прочего персонала подпись меняется на «Отдел»)
            lblDept = new Label { Text = "Кафедра", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 254), AutoSize = true };
            cmbDepartments = CreateComboBox(x, 276, w);

            // Ставка и тип занятости (поддерживается Основное + Внутреннее совместительство на одной кафедре)
            Label lblRate = new Label { Text = "Ставка", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 314), AutoSize = true };
            cmbRate = CreateComboBox(x, 336, 115);
            cmbRate.Items.AddRange(new object[] { "0,25", "0,5", "0,75", "1,0" });
            cmbRate.SelectedIndex = 3;

            Label lblEmp = new Label { Text = "Тип занятости", Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x + 132, 314), AutoSize = true };
            cmbEmployment = CreateComboBox(x + 132, 336, w - 132);
            cmbEmployment.Items.AddRange(new object[] { "Основное", "Внутреннее совместительство" });
            cmbEmployment.SelectedIndex = 0;
            cmbEmployment.SelectedIndexChanged += (s, e) =>
            {
                // При выборе внутреннего совместительства по умолчанию подставляем 0,5 ставки (ст. 345 ТК РБ)
                if (!_suppressAutoRate && !_editId.HasValue)
                {
                    cmbRate.SelectedIndex = cmbEmployment.SelectedIndex == 1 ? 1 : 3;
                }
            };

            // Налоговая льгота работника (ст. 209 НК РБ)
            Label lblBenefit = new Label
            {
                Text = "Налоговая льгота сотрудника (вычет по НДФЛ)",
                Font = labelFont,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(x, 376),
                AutoSize = true
            };
            cmbBenefitCategory = CreateComboBox(x, 398, w);
            cmbBenefitCategory.Items.AddRange(new object[]
            {
                "Нет (0 руб.)",
                "Стандартный вычет при доходе до 1 164 руб. (192 руб.)",
                "Инвалид I–II гр. / ветеран / ЧАЭС (272 руб.)",
                "Молодой специалист (620 руб.)"
            });
            cmbBenefitCategory.SelectedIndex = 0;

            // Налоговая льгота на детей / иждивенцев
            Label lblChildren = new Label
            {
                Text = "Льгота на детей и иждивенцев (вычет по НДФЛ)",
                Font = labelFont,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(x, 436),
                AutoSize = true
            };
            cmbChildrenBenefit = CreateComboBox(x, 458, w);
            cmbChildrenBenefit.Items.AddRange(new object[]
            {
                "Нет детей / иждивенцев (0 руб.)",
                "1 ребёнок до 18 лет (56 руб.)",
                "2 ребёнка до 18 лет (214 руб.)",
                "3 и более детей до 18 лет (321 руб.)",
                "Одинокий родитель / ребёнок-инвалид (107 руб.)"
            });
            cmbChildrenBenefit.SelectedIndex = 0;

            btnCancel = UiTheme.CreateOutlineButton("Отмена", x + w - 232, 512, 108, 36);
            btnCancel.DialogResult = DialogResult.Cancel;

            btnSave = UiTheme.CreatePrimaryButton(btnText, x + w - 116, 512, 116, 36);
            btnSave.Click += BtnSave_Click;

            this.Controls.AddRange(new Control[]
            {
                lblName, nameBox, lblCategory, cmbCategory, lblPos, cmbPositions,
                lblDept, cmbDepartments, lblRate, cmbRate, lblEmp, cmbEmployment,
                lblBenefit, cmbBenefitCategory, lblChildren, cmbChildrenBenefit,
                btnCancel, btnSave
            });

            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;
        }

        private static ComboBox CreateComboBox(int x, int y, int width)
        {
            return new ComboBox
            {
                Location = new Point(x, y),
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = UiTheme.TextPrimary
            };
        }

        private void LoadDictionaries()
        {
            try
            {
                var conn = new ServerConnection();
                var posRes = conn.SendRequest(new NetworkMessage { Command = "GET_POSITIONS" });
                if (posRes.IsSuccess) _allPositions = JsonConvert.DeserializeObject<DataTable>(posRes.Data);

                var deptRes = conn.SendRequest(new NetworkMessage { Command = "GET_DEPARTMENTS" });
                if (deptRes.IsSuccess)
                {
                    _allDepartments = JsonConvert.DeserializeObject<DataTable>(deptRes.Data);
                    if (_allDepartments != null && _allDepartments.Columns.Contains("DepartmentName"))
                    {
                        foreach (DataRow row in _allDepartments.Rows)
                        {
                            if (row["DepartmentName"] != DBNull.Value)
                                row["DepartmentName"] = UiTheme.FormatDepartmentName(row["DepartmentName"].ToString());
                        }
                    }
                }

                CategoryChanged(null, null);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка загрузки справочников: " + ex.Message); }
        }

        private void CategoryChanged(object sender, EventArgs e)
        {
            int catId = cmbCategory.SelectedIndex + 1;
            if (lblDept != null)
                lblDept.Text = catId == 1 ? "Кафедра" : "Отдел";

            if (_allPositions == null || _allDepartments == null) return;
            if (!_allPositions.Columns.Contains("Category") || !_allDepartments.Columns.Contains("DepartmentType")) return;

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
                MessageBox.Show("Заполните все обязательные поля!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                int benCat = Math.Max(0, cmbBenefitCategory.SelectedIndex);
                int childBen = Math.Max(0, cmbChildrenBenefit.SelectedIndex);

                // Ограничение ТК РБ: внутреннее совместительство не превышает 0,5 ставки
                if (empType.IndexOf("совмест", StringComparison.OrdinalIgnoreCase) >= 0 && rate > 0.5m)
                {
                    MessageBox.Show(
                        "По внутреннему совместительству ставка не должна превышать 0,5 (ст. 345 ТК РБ).\n" +
                        "Выберите ставку 0,25 или 0,5.",
                        "Проверка ставки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                NetworkMessage request = _editId.HasValue
                    ? new NetworkMessage
                    {
                        Command = "UPDATE_EMPLOYEE",
                        Data = $"{_editId.Value}|{name}|{posId}|{deptId}|{rateForServer}|{empType}|{benCat}|{childBen}"
                    }
                    : new NetworkMessage
                    {
                        Command = "ADD_EMPLOYEE",
                        Data = $"{name}|{posId}|{deptId}|{rateForServer}|{empType}|{benCat}|{childBen}"
                    };

                var res = new ServerConnection().SendRequest(request);

                if (res.IsSuccess)
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else
                {
                    MessageBox.Show(res.ErrorMessage, "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex) { MessageBox.Show("Ошибка сети: " + ex.Message); }
        }
    }
}

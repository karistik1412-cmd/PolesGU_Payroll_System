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
        private CheckedListBox clbBenefits;
        private CheckedListBox clbBonuses;
        private Button btnSave;
        private Button btnCancel;

        private DataTable _allPositions;
        private DataTable _allDepartments;
        private readonly int? _editId = null;

        public AddEmployeeForm()
        {
            InitializeForm(Localization.T("emp.addTitle"), Localization.T("common.save"));
            LoadDictionaries();
        }

        // Быстрое оформление внутреннего совместительства для выбранного сотрудника
        public AddEmployeeForm(string fullName, int posId, int deptId, int benefitCategory, int childrenBenefit)
        {
            InitializeForm(Localization.T("emp.partTimeTitle"), Localization.T("common.save"));
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

            InitializeForm(Localization.T("emp.editTitle"), Localization.T("common.save"));
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

            for (int i = 0; i < clbBenefits.Items.Count; i++)
                clbBenefits.SetItemChecked(i, (benefitCategory & (1 << i)) != 0);

            for (int i = 0; i < clbBonuses.Items.Count; i++)
                clbBonuses.SetItemChecked(i, (childrenBenefit & (1 << i)) != 0);
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
            int w = 492;

            // ФИО
            Label lblName = new Label { Text = Localization.T("emp.fio"), Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 68), AutoSize = true };
            txtFullName = new TextBox { Font = new Font("Segoe UI", 10f) };
            Panel nameBox = UiTheme.WrapTextBox(txtFullName, x, 90, w, 34);

            // Категория персонала
            Label lblCategory = new Label { Text = Localization.T("emp.category"), Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 134), AutoSize = true };
            cmbCategory = CreateComboBox(x, 156, w);
            cmbCategory.Items.Add(Localization.T("val.cat.teaching"));
            cmbCategory.Items.Add(Localization.T("val.cat.other"));
            cmbCategory.SelectedIndex = 0;
            cmbCategory.SelectedIndexChanged += CategoryChanged;

            // Должность
            Label lblPos = new Label { Text = Localization.T("emp.position"), Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 194), AutoSize = true };
            cmbPositions = CreateComboBox(x, 216, w);

            // Кафедра (для прочего персонала подпись меняется на «Отдел»)
            lblDept = new Label { Text = Localization.T("val.dept.department"), Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 254), AutoSize = true };
            cmbDepartments = CreateComboBox(x, 276, w);

            // Ставка и тип занятости (поддерживается Основное + Внутреннее совместительство на одной кафедре)
            Label lblRate = new Label { Text = Localization.T("emp.rate"), Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(x, 314), AutoSize = true };
            cmbRate = CreateComboBox(x, 336, 115);
            cmbRate.Items.AddRange(new object[] { "0,25", "0,5", "0,75", "1,0" });
            cmbRate.SelectedIndex = 3;

            // Ставка и тип занятости в одну строку: ширина подписи считается по фактическому тексту,
            // иначе длинный перевод «Тип занятости» / «Employment type» наезжает на поле ставки.
            int empLabelX = x + Math.Max(120, TextRenderer.MeasureText(lblRate.Text, labelFont).Width + 24);
            Label lblEmp = new Label { Text = Localization.T("emp.type"), Font = labelFont, ForeColor = UiTheme.TextSecondary, Location = new Point(empLabelX, 314), AutoSize = true };
            cmbEmployment = CreateComboBox(empLabelX, 336, w - (empLabelX - x));
            // В списке — перевод, в Items — служебные русские значения (их понимает сервер)
            cmbEmployment.DrawMode = DrawMode.OwnerDrawFixed;
            cmbEmployment.DropDownWidth = Math.Max(220, w - (empLabelX - x));
            cmbEmployment.DrawItem += EmploymentDrawItem;
            cmbEmployment.Items.AddRange(new object[] { "Основное", "Внутреннее совместительство" });
            cmbEmployment.SelectedIndex = 0;
            cmbEmployment.SelectedIndexChanged += (s, e) =>
            {
                bool isPartTime = cmbEmployment.SelectedIndex == 1;
                string prev = cmbRate.Text;
                cmbRate.Items.Clear();
                if (isPartTime)
                    cmbRate.Items.AddRange(new object[] { "0,25", "0,5" });
                else
                    cmbRate.Items.AddRange(new object[] { "0,25", "0,5", "0,75", "1,0" });

                int idx = cmbRate.Items.IndexOf(prev);
                cmbRate.SelectedIndex = idx >= 0 ? idx : (isPartTime ? 1 : cmbRate.Items.Count - 1);
            };

            // Налоговые льготы работника (множественный выбор по ст. 209 НК РБ)
            Label lblBenefit = new Label
            {
                Text = Localization.T("emp.taxBenefits"),
                Font = labelFont,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(x, 376),
                AutoSize = true
            };
            clbBenefits = new CheckedListBox
            {
                Location = new Point(x, 398),
                Size = new Size(w, 82),
                CheckOnClick = true,
                Font = new Font("Segoe UI", 8.5f),
                BorderStyle = BorderStyle.FixedSingle
            };
            clbBenefits.Items.AddRange(new object[]
            {
                Localization.T("val.ben.standard"),
                Localization.T("val.ben.oneChild"),
                Localization.T("val.ben.twoChildren"),
                Localization.T("val.ben.singleParent"),
                Localization.T("val.ben.disabled"),
                Localization.T("val.ben.youngSpecialist")
            });

            // Надбавки сотрудника (множественный выбор)
            Label lblBonuses = new Label
            {
                Text = Localization.T("emp.allowances"),
                Font = labelFont,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(x, 486),
                AutoSize = true
            };
            clbBonuses = new CheckedListBox
            {
                Location = new Point(x, 508),
                Size = new Size(w, 72),
                CheckOnClick = true,
                Font = new Font("Segoe UI", 8.5f),
                BorderStyle = BorderStyle.FixedSingle
            };
            clbBonuses.Items.AddRange(new object[]
            {
                Localization.T("val.bonus.industry"),
                Localization.T("val.bonus.contract"),
                Localization.T("val.bonus.education"),
                Localization.T("val.bonus.youngSpec"),
                Localization.T("val.bonus.currentMonth"),
                Localization.T("val.bonus.achievements"),
                Localization.T("val.bonus.experience"),
                Localization.T("val.bonus.curatorship")
            });

            cmbBenefitCategory = CreateComboBox(x, 398, w);
            cmbBenefitCategory.Visible = false;
            cmbBenefitCategory.Items.AddRange(new object[] { "0", "1", "2", "3" });
            cmbBenefitCategory.SelectedIndex = 0;

            // Налоговая льгота на детей / иждивенцев
            Label lblChildren = new Label
            {
                Text = Localization.T("emp.childrenBenefit"),
                Font = labelFont,
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(x, 436),
                AutoSize = true
            };
            cmbChildrenBenefit = CreateComboBox(x, 458, w);
            cmbChildrenBenefit.Visible = false;
            cmbChildrenBenefit.Items.AddRange(new object[] { "0", "1", "2", "3", "4" });
            cmbChildrenBenefit.SelectedIndex = 0;

            btnCancel = UiTheme.CreateOutlineButton(Localization.T("common.cancel"), this.ClientSize.Width - 262, 622, 108, 36);
            btnCancel.DialogResult = DialogResult.Cancel;

            btnSave = UiTheme.CreatePrimaryButton(btnText, this.ClientSize.Width - 146, 622, 116, 36);
            btnSave.Click += BtnSave_Click;

            this.ClientSize = new Size(540, 680);
            this.Controls.AddRange(new Control[]
            {
                lblName, nameBox, lblCategory, cmbCategory, lblPos, cmbPositions,
                lblDept, cmbDepartments, lblRate, cmbRate, lblEmp, cmbEmployment,
                lblBenefit, clbBenefits, lblBonuses, clbBonuses,
                cmbBenefitCategory, cmbChildrenBenefit,
                btnCancel, btnSave
            });

            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;
        }

        // В списке показываем перевод, а значение (Items[i]) остаётся служебным —
        // иначе сервер не распознает тип занятости (для него это строка «Основное»).
        private static void EmploymentDrawItem(object sender, DrawItemEventArgs e)
        {
            ComboBox cb = sender as ComboBox;
            if (cb == null || e.Index < 0 || e.Index >= cb.Items.Count) return;

            e.DrawBackground();
            string text = Localization.Term(cb.Items[e.Index].ToString());
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color fore = selected ? SystemColors.HighlightText : e.ForeColor;
            TextRenderer.DrawText(e.Graphics, text, cb.Font, e.Bounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
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
            catch (Exception ex) { MessageBox.Show(Localization.T("emp.dictionariesError") + ex.Message); }
        }

        private void CategoryChanged(object sender, EventArgs e)
        {
            int catId = cmbCategory.SelectedIndex + 1;
            if (lblDept != null)
                lblDept.Text = catId == 1 ? Localization.T("val.dept.department") : Localization.T("val.dept.unit");

            if (_allPositions == null || _allDepartments == null) return;
            if (!_allPositions.Columns.Contains("Category") || !_allDepartments.Columns.Contains("DepartmentType")) return;

            _allPositions.DefaultView.RowFilter = $"Category = {catId}";

            // Название должности переводим в отдельном столбце, чтобы ValueMember
            // оставался числовым PositionID, а на экране был перевод.
            if (!_allPositions.Columns.Contains("PositionNameLocalized"))
                _allPositions.Columns.Add("PositionNameLocalized", typeof(string));
            foreach (DataRow r in _allPositions.Rows)
            {
                r["PositionNameLocalized"] = r["PositionName"] == DBNull.Value
                    ? string.Empty
                    : Localization.Term(r["PositionName"].ToString());
            }

            cmbPositions.DataSource = _allPositions.DefaultView;
            cmbPositions.DisplayMember = "PositionNameLocalized";
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
                MessageBox.Show(Localization.T("emp.fillRequired"), Localization.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                // Берём служебное русское значение из Items, а не видимый (переведённый) текст
                string empType = cmbEmployment.SelectedItem != null
                    ? cmbEmployment.SelectedItem.ToString()
                    : "Основное";
                int benCat = 0;
                for (int i = 0; i < clbBenefits.Items.Count; i++)
                    if (clbBenefits.GetItemChecked(i)) benCat |= (1 << i);
                int childBen = 0;
                for (int i = 0; i < clbBonuses.Items.Count; i++)
                    if (clbBonuses.GetItemChecked(i)) childBen |= (1 << i);

                // Ограничение ТК РБ: внутреннее совместительство не превышает 0,5 ставки
                if (empType.IndexOf("совмест", StringComparison.OrdinalIgnoreCase) >= 0 && rate > 0.5m)
                {
                    MessageBox.Show(
                        Localization.T("emp.rateLimit"),
                        Localization.T("emp.rateCheck"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    MessageBox.Show(res.ErrorMessage, Localization.T("common.attention"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex) { MessageBox.Show(Localization.T("common.errorNetwork") + ex.Message); }
        }
    }
}

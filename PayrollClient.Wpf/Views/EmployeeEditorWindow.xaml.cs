using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using PayrollClient.Network;
using PayrollClient.Services;

namespace PayrollClient.Views
{
    public partial class EmployeeEditorWindow : Window
    {
        private readonly int? _editId;
        private DataTable _positions;
        private DataTable _departments;

        private static readonly string[] BenefitKeys =
        {
            "val.ben.standard", "val.ben.oneChild", "val.ben.twoChildren",
            "val.ben.singleParent", "val.ben.disabled", "val.ben.youngSpecialist"
        };

        private static readonly string[] BonusKeys =
        {
            "val.bonus.industry", "val.bonus.contract", "val.bonus.education", "val.bonus.youngSpec",
            "val.bonus.currentMonth", "val.bonus.achievements", "val.bonus.experience", "val.bonus.curatorship"
        };

        private static readonly decimal[] Rates = { 0.25m, 0.5m, 0.75m, 1.0m };

        public EmployeeEditorWindow() : this(null) { }

        public EmployeeEditorWindow(DataRow employee)
        {
            InitializeComponent();

            if (employee != null && employee.Table.Columns.Contains("EmployeeID"))
            {
                int id;
                if (int.TryParse(employee["EmployeeID"].ToString(), out id)) _editId = id;
            }

            Title = Localization.T(_editId.HasValue ? "emp.editTitle" : "emp.addTitle");
            lblHeader.Text = Title;
            ApplyLabels();

            FillDictionaries();
            if (employee != null) FillFromRow(employee);
            else Defaults();
        }

        private void ApplyLabels()
        {
            lblName.Text = Localization.T("emp.fio");
            lblCategory.Text = Localization.T("emp.category");
            lblPosition.Text = Localization.T("emp.position");
            lblDepartment.Text = Localization.T("val.dept.department");
            lblRate.Text = Localization.T("emp.rate");
            lblEmployment.Text = Localization.T("emp.type");
            lblBenefits.Text = Localization.T("emp.taxBenefits");
            lblBonuses.Text = Localization.T("emp.allowances");
            btnSave.Content = Localization.T("common.save");
            btnCancel.Content = Localization.T("common.cancel");

            cmbCategory.Items.Clear();
            cmbCategory.Items.Add(new ComboBoxItem { Content = Localization.T("val.cat.teaching"), Tag = 1 });
            cmbCategory.Items.Add(new ComboBoxItem { Content = Localization.T("val.cat.other"), Tag = 2 });
            cmbCategory.SelectedIndex = 0;
            cmbCategory.SelectionChanged += (s, e) => ReloadPositionsAndDepartments();

            // Ставки показываем с запятой, значение храним числом
            cmbRate.Items.Clear();
            foreach (decimal rate in Rates)
                cmbRate.Items.Add(new ComboBoxItem { Content = rate.ToString("0.##"), Tag = rate });
            cmbRate.SelectedIndex = 3;

            // Тип занятости: служебное русское значение в Tag, перевод — на экране
            cmbEmployment.Items.Clear();
            cmbEmployment.Items.Add(new ComboBoxItem { Content = Localization.T("val.emp.main"), Tag = "Основное" });
            cmbEmployment.Items.Add(new ComboBoxItem { Content = Localization.T("val.emp.partTime"), Tag = "Внутреннее совместительство" });
            cmbEmployment.SelectedIndex = 0;
            cmbEmployment.SelectionChanged += (s, e) => SyncRateLimits();

            benefitsPanel.Children.Clear();
            foreach (string key in BenefitKeys)
                benefitsPanel.Children.Add(new CheckBox
                {
                    Content = Localization.T(key),
                    Style = (Style)Application.Current.Resources["AppCheckBox"]
                });

            bonusesPanel.Children.Clear();
            foreach (string key in BonusKeys)
                bonusesPanel.Children.Add(new CheckBox
                {
                    Content = Localization.T(key),
                    Style = (Style)Application.Current.Resources["AppCheckBox"]
                });
        }

        private void Defaults()
        {
            cmbCategory.SelectedIndex = 0;
            cmbRate.SelectedIndex = 3;
            cmbEmployment.SelectedIndex = 0;
        }

        private void SyncRateLimits()
        {
            bool partTime = cmbEmployment.SelectedIndex == 1;
            decimal current = SelectedRate();

            cmbRate.Items.Clear();
            foreach (decimal rate in Rates)
            {
                if (partTime && rate > 0.5m) continue;
                cmbRate.Items.Add(new ComboBoxItem { Content = rate.ToString("0.##"), Tag = rate });
            }

            int index = 0;
            for (int i = 0; i < cmbRate.Items.Count; i++)
            {
                ComboBoxItem item = cmbRate.Items[i] as ComboBoxItem;
                if (item != null && (decimal)item.Tag == current) { index = i; break; }
                if (partTime) index = cmbRate.Items.Count - 1;
            }
            cmbRate.SelectedIndex = Math.Min(index, cmbRate.Items.Count - 1);
        }

        private void FillDictionaries()
        {
            try
            {
                ServerConnection connection = new ServerConnection();

                NetworkMessage posResponse = connection.SendRequest(
                    new NetworkMessage { Command = "GET_POSITIONS" });
                if (posResponse != null && posResponse.IsSuccess)
                    _positions = JsonConvert.DeserializeObject<DataTable>(posResponse.Data);

                NetworkMessage deptResponse = connection.SendRequest(
                    new NetworkMessage { Command = "GET_DEPARTMENTS" });
                if (deptResponse != null && deptResponse.IsSuccess)
                {
                    _departments = JsonConvert.DeserializeObject<DataTable>(deptResponse.Data);
                    if (_departments != null && _departments.Columns.Contains("DepartmentName"))
                    {
                        foreach (DataRow row in _departments.Rows)
                            if (row["DepartmentName"] != DBNull.Value)
                                row["DepartmentName"] = UiNames.CleanDepartment(row["DepartmentName"].ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("emp.dictionariesError") + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            ReloadPositionsAndDepartments();
        }

        private void ReloadPositionsAndDepartments()
        {
            if (_positions != null)
            {
                int category = SelectedCategory();
                List<ComboBoxItem> items = new List<ComboBoxItem>();
                foreach (DataRow row in _positions.Rows)
                {
                    int rowCategory;
                    if (_positions.Columns.Contains("Category") &&
                        !int.TryParse(row["Category"].ToString(), out rowCategory)) continue;
                    else if (!_positions.Columns.Contains("Category")) rowCategory = category;

                    if (rowCategory != category) continue;

                    items.Add(new ComboBoxItem
                    {
                        Content = Localization.Term(row["PositionName"].ToString()),
                        Tag = Convert.ToInt32(row["PositionID"])
                    });
                }
                cmbPositions.Items.Clear();
                foreach (ComboBoxItem item in items) cmbPositions.Items.Add(item);
                if (cmbPositions.Items.Count > 0) cmbPositions.SelectedIndex = 0;
            }

            if (_departments != null)
            {
                int departmentType = SelectedCategory() == 1 ? 1 : 2;
                cmbDepartments.Items.Clear();
                foreach (DataRow row in _departments.Rows)
                {
                    if (_departments.Columns.Contains("DepartmentType") &&
                        Convert.ToInt32(row["DepartmentType"]) != departmentType) continue;

                    cmbDepartments.Items.Add(new ComboBoxItem
                    {
                        Content = row["DepartmentName"].ToString(),
                        Tag = Convert.ToInt32(row["DepartmentID"])
                    });
                }
                if (cmbDepartments.Items.Count > 0) cmbDepartments.SelectedIndex = 0;
            }

            lblDepartment.Text = SelectedCategory() == 1
                ? Localization.T("val.dept.department")
                : Localization.T("val.dept.unit");
        }

        private void FillFromRow(DataRow row)
        {
            txtFullName.Text = row["ФИО"] == DBNull.Value ? string.Empty : row["ФИО"].ToString();

            int positionId = row["PositionID"] == DBNull.Value ? 0 : Convert.ToInt32(row["PositionID"]);
            int departmentId = row["DepartmentID"] == DBNull.Value ? 0 : Convert.ToInt32(row["DepartmentID"]);

            // Категория определяется по должности: преподавательская (1) или прочий персонал (2)
            if (_positions != null && _positions.Columns.Contains("Category"))
            {
                DataRow position = _positions.Rows.Cast<DataRow>()
                    .FirstOrDefault(r => Convert.ToInt32(r["PositionID"]) == positionId);
                if (position != null)
                {
                    int category = Convert.ToInt32(position["Category"]);
                    if (category >= 1 && category <= cmbCategory.Items.Count)
                        cmbCategory.SelectedIndex = category - 1;
                }
            }

            SelectByTag(cmbPositions, positionId);
            SelectByTag(cmbDepartments, departmentId);

            decimal rate = 1m;
            if (row["Ставка"] != DBNull.Value)
                decimal.TryParse(row["Ставка"].ToString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out rate);

            string employment = row["Тип занятости"] == DBNull.Value ? "Основное" : row["Тип занятости"].ToString();
            bool partTime = employment.IndexOf("совмест", StringComparison.OrdinalIgnoreCase) >= 0;
            cmbEmployment.SelectedIndex = partTime ? 1 : 0;

            SelectRate(rate);

            int benefitCategory = row["BenefitCategory"] == DBNull.Value ? 0 : Convert.ToInt32(row["BenefitCategory"]);
            int childrenBenefit = row["ChildrenBenefit"] == DBNull.Value ? 0 : Convert.ToInt32(row["ChildrenBenefit"]);
            for (int i = 0; i < benefitsPanel.Children.Count; i++)
                ((CheckBox)benefitsPanel.Children[i]).IsChecked = (benefitCategory & (1 << i)) != 0;
            for (int i = 0; i < bonusesPanel.Children.Count; i++)
                ((CheckBox)bonusesPanel.Children[i]).IsChecked = (childrenBenefit & (1 << i)) != 0;
        }

        private static void SelectByTag(ComboBox combo, int tag)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                ComboBoxItem item = combo.Items[i] as ComboBoxItem;
                if (item != null && Convert.ToInt32(item.Tag) == tag)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SelectRate(decimal rate)
        {
            for (int i = 0; i < cmbRate.Items.Count; i++)
            {
                ComboBoxItem item = cmbRate.Items[i] as ComboBoxItem;
                if (item == null) continue;
                if (Math.Abs((decimal)item.Tag - rate) < 0.01m)
                {
                    cmbRate.SelectedIndex = i;
                    return;
                }
            }
        }

        private int SelectedCategory()
        {
            ComboBoxItem item = cmbCategory.SelectedItem as ComboBoxItem;
            return item == null ? 1 : Convert.ToInt32(item.Tag);
        }

        private decimal SelectedRate()
        {
            ComboBoxItem item = cmbRate.SelectedItem as ComboBoxItem;
            return item == null ? 1m : (decimal)item.Tag;
        }

        private string SelectedEmployment()
        {
            ComboBoxItem item = cmbEmployment.SelectedItem as ComboBoxItem;
            return item == null ? "Основное" : item.Tag.ToString();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            ComboBoxItem position = cmbPositions.SelectedItem as ComboBoxItem;
            ComboBoxItem department = cmbDepartments.SelectedItem as ComboBoxItem;

            if (string.IsNullOrWhiteSpace(txtFullName.Text) || position == null || department == null)
            {
                MessageBox.Show(Localization.T("emp.fillRequired"), Localization.T("common.error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal rate = SelectedRate();
            string employment = SelectedEmployment();

            if (employment.IndexOf("совмест", StringComparison.OrdinalIgnoreCase) >= 0 && rate > 0.5m)
            {
                MessageBox.Show(Localization.T("emp.rateLimit"), Localization.T("emp.rateCheck"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int benefitCategory = 0;
            for (int i = 0; i < benefitsPanel.Children.Count; i++)
                if (((CheckBox)benefitsPanel.Children[i]).IsChecked == true) benefitCategory |= (1 << i);

            int childrenBenefit = 0;
            for (int i = 0; i < bonusesPanel.Children.Count; i++)
                if (((CheckBox)bonusesPanel.Children[i]).IsChecked == true) childrenBenefit |= (1 << i);

            string rateText = rate.ToString(CultureInfo.InvariantCulture);
            string name = txtFullName.Text.Trim();
            int positionId = Convert.ToInt32(position.Tag);
            int departmentId = Convert.ToInt32(department.Tag);

            try
            {
                NetworkMessage request = _editId.HasValue
                    ? new NetworkMessage
                    {
                        Command = "UPDATE_EMPLOYEE",
                        Data = string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                            _editId.Value, name, positionId, departmentId, rateText,
                            employment, benefitCategory, childrenBenefit)
                    }
                    : new NetworkMessage
                    {
                        Command = "ADD_EMPLOYEE",
                        Data = string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}",
                            name, positionId, departmentId, rateText,
                            employment, benefitCategory, childrenBenefit)
                    };

                NetworkMessage response = new ServerConnection().SendRequest(request);
                if (response != null && response.IsSuccess)
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(response == null ? Localization.T("common.error") : response.ErrorMessage,
                        Localization.T("common.attention"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.errorNetwork") + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

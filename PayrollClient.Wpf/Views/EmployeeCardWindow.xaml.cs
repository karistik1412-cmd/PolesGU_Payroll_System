using System;
using System.Data;
using System.Globalization;
using System.Windows;
using PayrollClient.Services;

namespace PayrollClient.Views
{
    public partial class EmployeeCardWindow : Window
    {
        public EmployeeCardWindow(DataRow employee, decimal totalRate, DateTime hireDate)
        {
            InitializeComponent();

            Title = Localization.T("emp.cardTitle");
            btnClose.Content = Localization.T("common.close");

            string name = Value(employee, "ФИО");
            string position = Localization.Term(Value(employee, "Должность"));
            string department = UiNames.CleanDepartment(Value(employee, "Кафедра"));
            string employment = Localization.Term(Value(employee, "Тип занятости"));
            string benefits = Localization.Term(Value(employee, "Льготы"));

            lblTitle.Text = name;
            lblPosition.Text = string.Format("{0}  {1}", Localization.T("card.position"), position);
            lblDepartment.Text = string.Format("{0}  {1}", Localization.T("card.department"), department);
            lblEmployment.Text = string.Format("{0}  {1} ({2}: {3})",
                Localization.T("card.employment"),
                employment,
                Localization.T("emp.rate").ToLowerInvariant(),
                totalRate.ToString("0.##"));
            lblBenefits.Text = string.Format("{0}  {1}", Localization.T("card.benefits"), benefits);
            lblHireDate.Text = string.Format("{0}  {1}",
                Localization.T("card.hireDate"),
                hireDate.ToString("dd.MM.yyyy"));

            lblExperienceTitle.Text = Localization.T("card.experienceTitle");
            lblExperience.Text = CalculateExperience(hireDate);
        }

        // Стаж на текущую дату: «1 г. 3 мес. 12 дн.» с переводом единиц
        private static string CalculateExperience(DateTime startDate)
        {
            DateTime end = DateTime.Today;
            if (end <= startDate)
            {
                return string.Format("{0} {1}  {2} {3}  {4} {5}",
                    "0", Localization.T("unit.years"),
                    "0", Localization.T("unit.months"),
                    "0", Localization.T("unit.days"));
            }

            int years = end.Year - startDate.Year;
            int months = end.Month - startDate.Month;
            int days = end.Day - startDate.Day;

            if (days < 0)
            {
                months--;
                DateTime previous = end.AddMonths(-1);
                days += DateTime.DaysInMonth(previous.Year, previous.Month);
            }
            if (months < 0)
            {
                years--;
                months += 12;
            }

            return string.Format("{0} {1}  {2} {3}  {4} {5}",
                years, Localization.T("unit.years"),
                months, Localization.T("unit.months"),
                days, Localization.T("unit.days"));
        }

        private static string Value(DataRow row, string column)
        {
            if (row == null || !row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
                return string.Empty;
            return row[column].ToString();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

using System.Data;

namespace PayrollServer.Models
{
    // Комплексный набор аналитических данных для вкладки «Графики»
    public class AnalyticsData
    {
        // 1. Фонд оплаты труда по кафедрам за выбранный месяц
        public DataTable DepartmentPayroll { get; set; }

        // 2. Структура начислений и удержаний за выбранный месяц
        public int PaidEmployeesCount { get; set; }
        public decimal TotalBaseSalary { get; set; }
        public decimal TotalBonus { get; set; }
        public decimal TotalSickVacationPay { get; set; }
        public decimal TotalTaxDeduction { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalFszen { get; set; }
        public decimal TotalUnion { get; set; }
        public decimal TotalNetSalary { get; set; }

        // 3. Динамика фонда зарплаты по месяцам выбранного года
        public DataTable MonthlyTrend { get; set; }

        // 4. Отработка часов (Факт / Норма) по сотрудникам из табеля
        public DataTable EmployeeHours { get; set; }
    }
}

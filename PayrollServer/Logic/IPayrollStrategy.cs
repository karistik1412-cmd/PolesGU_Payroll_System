using System;

namespace PayrollServer.Logic
{
    // Паттерн Strategy (Стратегия) - 2-й паттерн проектирования для расчета оклада по табелю
    public interface IPayrollStrategy
    {
        decimal CalculateBaseSalary(
            decimal baseRate,
            decimal coefficient,
            decimal rate,
            int factDays,
            int normDays,
            decimal factHours,
            decimal normHours);
    }

    // Расчет оклада пропорционально отработанным часам из табеля
    public class HourlyPayrollStrategy : IPayrollStrategy
    {
        public decimal CalculateBaseSalary(
            decimal baseRate,
            decimal coefficient,
            decimal rate,
            int factDays,
            int normDays,
            decimal factHours,
            decimal normHours)
        {
            if (normHours <= 0m || factHours <= 0m)
                return 0m;

            decimal tariffSalary = baseRate * coefficient * rate;
            decimal hoursRatio = factHours / normHours;
            return Math.Round(tariffSalary * hoursRatio, 2, MidpointRounding.AwayFromZero);
        }
    }

    // Расчет оклада пропорционально отработанным дням из табеля
    public class DailyPayrollStrategy : IPayrollStrategy
    {
        public decimal CalculateBaseSalary(
            decimal baseRate,
            decimal coefficient,
            decimal rate,
            int factDays,
            int normDays,
            decimal factHours,
            decimal normHours)
        {
            if (normDays <= 0 || factDays <= 0)
                return 0m;

            decimal tariffSalary = baseRate * coefficient * rate;
            decimal daysRatio = (decimal)factDays / normDays;
            return Math.Round(tariffSalary * daysRatio, 2, MidpointRounding.AwayFromZero);
        }
    }
}

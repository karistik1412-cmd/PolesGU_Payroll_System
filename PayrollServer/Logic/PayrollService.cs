using System;
using System.Data;
using System.Data.SqlClient;
using PayrollServer.Database;
using PayrollServer.Models;

namespace PayrollServer.Logic
{
    public class PayrollService
    {
        private const decimal BaseRate = 250.00m;
        private const int DefaultYear = 2026;

        private readonly IPayrollStrategy _hourlyStrategy = new HourlyPayrollStrategy();
        private readonly IPayrollStrategy _dailyStrategy = new DailyPayrollStrategy();

        // Расчет зарплаты за выбранный месяц и год на основе данных табеля (дни и часы)
        public DataTable CalculatePayroll(int month, int year = DefaultYear)
        {
            DataTable resultTable = new DataTable();
            resultTable.Columns.Add("EmployeeID", typeof(int));
            resultTable.Columns.Add("ФИО", typeof(string));
            resultTable.Columns.Add("Ставка", typeof(decimal));
            resultTable.Columns.Add("Дни", typeof(string));
            resultTable.Columns.Add("Часы", typeof(string));
            resultTable.Columns.Add("Оклад", typeof(decimal));
            resultTable.Columns.Add("Надбавка", typeof(decimal));
            resultTable.Columns.Add("НДФЛ (13%)", typeof(decimal));
            resultTable.Columns.Add("ФСЗН (1%)", typeof(decimal));
            resultTable.Columns.Add("Профсоюз (1%)", typeof(decimal));
            resultTable.Columns.Add("К выплате", typeof(decimal));

            int defaultNormDays = TimesheetService.GetWorkingDaysInMonth(year, month);

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasHoursColumns = TimesheetService.EnsureHoursColumns(conn);
                string daysCol = TimesheetService.DetectDaysColumnName(conn);

                string selectQuery = hasHoursColumns
                    ? $@"
                        SELECT e.EmployeeID, e.FullName, p.Coefficient, ISNULL(p.Category, 1) AS Category,
                               e.ExperienceYears, e.HireDate, e.Rate,
                               t.TimesheetID, t.[{daysCol}] AS FactDays, t.NormDays,
                               t.HoursWorked, t.NormHours
                        FROM Employees e
                        JOIN Positions p ON e.PositionID = p.PositionID
                        LEFT JOIN Timesheets t ON e.EmployeeID = t.EmployeeID AND t.[Month] = @m AND t.[Year] = @y
                        ORDER BY e.FullName, e.Rate DESC"
                    : $@"
                        SELECT e.EmployeeID, e.FullName, p.Coefficient, ISNULL(p.Category, 1) AS Category,
                               e.ExperienceYears, e.HireDate, e.Rate,
                               t.TimesheetID, t.[{daysCol}] AS FactDays, t.NormDays,
                               CAST(NULL AS DECIMAL(6, 2)) AS HoursWorked,
                               CAST(NULL AS DECIMAL(6, 2)) AS NormHours
                        FROM Employees e
                        JOIN Positions p ON e.PositionID = p.PositionID
                        LEFT JOIN Timesheets t ON e.EmployeeID = t.EmployeeID AND t.[Month] = @m AND t.[Year] = @y
                        ORDER BY e.FullName, e.Rate DESC";

                using (var cmd = new SqlCommand(selectQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int empId = Convert.ToInt32(reader["EmployeeID"]);
                            string fullName = reader["FullName"].ToString();
                            decimal coefficient = Convert.ToDecimal(reader["Coefficient"]);
                            int category = Convert.ToInt32(reader["Category"]);
                            decimal rate = reader["Rate"] != DBNull.Value ? Convert.ToDecimal(reader["Rate"]) : 1.0m;
                            if (rate <= 0m) rate = 1.0m;

                            bool hasTimesheet = reader["TimesheetID"] != DBNull.Value;
                            int normDays = (hasTimesheet && reader["NormDays"] != DBNull.Value && Convert.ToInt32(reader["NormDays"]) > 0)
                                ? Convert.ToInt32(reader["NormDays"])
                                : defaultNormDays;

                            int factDays = (hasTimesheet && reader["FactDays"] != DBNull.Value)
                                ? Convert.ToInt32(reader["FactDays"])
                                : normDays;

                            bool hasExplicitHours = hasTimesheet && reader["HoursWorked"] != DBNull.Value;
                            decimal normHours = (hasTimesheet && reader["NormHours"] != DBNull.Value && Convert.ToDecimal(reader["NormHours"]) > 0m)
                                ? Convert.ToDecimal(reader["NormHours"])
                                : TimesheetService.CalculateNormHours(normDays, category, rate);

                            decimal factHours = hasExplicitHours
                                ? Convert.ToDecimal(reader["HoursWorked"])
                                : TimesheetService.CalculateNormHours(factDays, category, rate);

                            // Точный расчет полных лет стажа
                            DateTime hireDate = Convert.ToDateTime(reader["HireDate"]);
                            int experience = CalculateFullYears(hireDate, DateTime.Today);
                            if (reader["ExperienceYears"] != DBNull.Value)
                            {
                                int dbExp = Convert.ToInt32(reader["ExperienceYears"]);
                                if (dbExp > experience) experience = dbExp;
                            }

                            // Выбираем стратегию расчета: по часам (если в табеле есть часы) или по дням
                            IPayrollStrategy strategy = (hasExplicitHours && normHours > 0m)
                                ? _hourlyStrategy
                                : _dailyStrategy;

                            decimal baseSalary = strategy.CalculateBaseSalary(
                                BaseRate, coefficient, rate, factDays, normDays, factHours, normHours);

                            // Надбавка за стаж работы в бюджетной организации
                            decimal bonusPercent = 0.10m;
                            if (experience >= 5 && experience < 10) bonusPercent = 0.15m;
                            else if (experience >= 10 && experience < 15) bonusPercent = 0.20m;
                            else if (experience >= 15) bonusPercent = 0.30m;

                            decimal bonus = Math.Round(baseSalary * bonusPercent, 2, MidpointRounding.AwayFromZero);
                            decimal grossSalary = baseSalary + bonus;

                            decimal taxAmount = Math.Round(grossSalary * 0.13m, 2, MidpointRounding.AwayFromZero);
                            decimal fszenAmount = Math.Round(grossSalary * 0.01m, 2, MidpointRounding.AwayFromZero);
                            decimal unionAmount = Math.Round(grossSalary * 0.01m, 2, MidpointRounding.AwayFromZero);
                            decimal netSalary = Math.Round(grossSalary - taxAmount - fszenAmount - unionAmount, 2, MidpointRounding.AwayFromZero);

                            string daysDisplay = $"{factDays} / {normDays}";
                            string hoursDisplay = $"{factHours:0.#} / {normHours:0.#}";

                            resultTable.Rows.Add(
                                empId, fullName, rate, daysDisplay, hoursDisplay,
                                baseSalary, bonus, taxAmount, fszenAmount, unionAmount, netSalary);
                        }
                    }
                }
            }

            return resultTable;
        }

        // Сохранение расчета зарплаты в БД в рамках транзакции
        public bool SavePayroll(DataTable payrollTable, int month, int year = DefaultYear)
        {
            if (payrollTable == null) return false;

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        string deleteQuery = "DELETE FROM PayrollCalculations WHERE [Month] = @m AND [Year] = @y";
                        using (var delCmd = new SqlCommand(deleteQuery, conn, tx))
                        {
                            delCmd.Parameters.AddWithValue("@m", month);
                            delCmd.Parameters.AddWithValue("@y", year);
                            delCmd.ExecuteNonQuery();
                        }

                        string insertQuery = @"
                            INSERT INTO PayrollCalculations 
                            (EmployeeID, [Month], [Year], BaseSalary, Bonus, TaxAmount, FszenAmount, UnionAmount, NetSalary) 
                            VALUES (@id, @m, @y, @base, @bonus, @tax, @fszen, @union, @net)";

                        foreach (DataRow row in payrollTable.Rows)
                        {
                            int empId = Convert.ToInt32(row["EmployeeID"]);
                            using (var insCmd = new SqlCommand(insertQuery, conn, tx))
                            {
                                insCmd.Parameters.AddWithValue("@id", empId);
                                insCmd.Parameters.AddWithValue("@m", month);
                                insCmd.Parameters.AddWithValue("@y", year);
                                insCmd.Parameters.AddWithValue("@base", Convert.ToDecimal(row["Оклад"]));
                                insCmd.Parameters.AddWithValue("@bonus", Convert.ToDecimal(row["Надбавка"]));
                                insCmd.Parameters.AddWithValue("@tax", Convert.ToDecimal(row["НДФЛ (13%)"]));
                                insCmd.Parameters.AddWithValue("@fszen", Convert.ToDecimal(row["ФСЗН (1%)"]));
                                insCmd.Parameters.AddWithValue("@union", Convert.ToDecimal(row["Профсоюз (1%)"]));
                                insCmd.Parameters.AddWithValue("@net", Convert.ToDecimal(row["К выплате"]));
                                insCmd.ExecuteNonQuery();
                            }
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }

            return true;
        }

        public DataTable GetAnalyticsData(int month, int year = DefaultYear)
        {
            DataTable dt = new DataTable();
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = @"
                    SELECT d.DepartmentName AS [Кафедра], SUM(pc.NetSalary) AS [СуммаКВыплате]
                    FROM PayrollCalculations pc
                    JOIN Employees e ON pc.EmployeeID = e.EmployeeID
                    JOIN Departments d ON e.DepartmentID = d.DepartmentID
                    WHERE pc.[Month] = @m AND pc.[Year] = @y
                    GROUP BY d.DepartmentName";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);

                    using (var adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            return dt;
        }

        // Расширенная аналитика для 4 графиков и итоговой панели
        public AnalyticsData GetFullAnalytics(int month, int year = DefaultYear)
        {
            var data = new AnalyticsData
            {
                DepartmentPayroll = new DataTable(),
                MonthlyTrend = new DataTable(),
                EmployeeHours = new DataTable()
            };

            data.EmployeeHours.Columns.Add("ФИО", typeof(string));
            data.EmployeeHours.Columns.Add("Ставка", typeof(decimal));
            data.EmployeeHours.Columns.Add("ЧасыФакт", typeof(decimal));
            data.EmployeeHours.Columns.Add("ЧасыНорма", typeof(decimal));

            int defaultNormDays = TimesheetService.GetWorkingDaysInMonth(year, month);

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();

                // 1. Фонд оплаты труда по кафедрам (Начислено и К выплате)
                string deptSql = @"
                    SELECT d.DepartmentName AS [Кафедра],
                           SUM(pc.BaseSalary + pc.Bonus) AS [Начислено],
                           SUM(pc.NetSalary) AS [КВыплате],
                           COUNT(pc.EmployeeID) AS [Сотрудников]
                    FROM PayrollCalculations pc
                    JOIN Employees e ON pc.EmployeeID = e.EmployeeID
                    JOIN Departments d ON e.DepartmentID = d.DepartmentID
                    WHERE pc.[Month] = @m AND pc.[Year] = @y
                    GROUP BY d.DepartmentName
                    ORDER BY SUM(pc.NetSalary) ASC";

                using (var cmd = new SqlCommand(deptSql, conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);
                    using (var adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(data.DepartmentPayroll);
                    }
                }

                // 2. Структура фонда оплаты труда за выбранный месяц
                string structSql = @"
                    SELECT COUNT(*) AS Cnt,
                           ISNULL(SUM(BaseSalary), 0) AS SumBase,
                           ISNULL(SUM(Bonus), 0) AS SumBonus,
                           ISNULL(SUM(TaxAmount), 0) AS SumTax,
                           ISNULL(SUM(ISNULL(FszenAmount, 0)), 0) AS SumFszen,
                           ISNULL(SUM(ISNULL(UnionAmount, 0)), 0) AS SumUnion,
                           ISNULL(SUM(NetSalary), 0) AS SumNet
                    FROM PayrollCalculations
                    WHERE [Month] = @m AND [Year] = @y";

                using (var cmd = new SqlCommand(structSql, conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            data.PaidEmployeesCount = Convert.ToInt32(reader["Cnt"]);
                            data.TotalBaseSalary = Convert.ToDecimal(reader["SumBase"]);
                            data.TotalBonus = Convert.ToDecimal(reader["SumBonus"]);
                            data.TotalTax = Convert.ToDecimal(reader["SumTax"]);
                            data.TotalFszen = Convert.ToDecimal(reader["SumFszen"]);
                            data.TotalUnion = Convert.ToDecimal(reader["SumUnion"]);
                            data.TotalNetSalary = Convert.ToDecimal(reader["SumNet"]);
                        }
                    }
                }

                // 3. Помесячная динамика начислений и выплат за весь выбранный год
                string trendSql = @"
                    SELECT [Month],
                           SUM(BaseSalary + Bonus) AS [Начислено],
                           SUM(NetSalary) AS [КВыплате]
                    FROM PayrollCalculations
                    WHERE [Year] = @y
                    GROUP BY [Month]
                    ORDER BY [Month]";

                using (var cmd = new SqlCommand(trendSql, conn))
                {
                    cmd.Parameters.AddWithValue("@y", year);
                    using (var adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(data.MonthlyTrend);
                    }
                }

                // 4. Отработка часов по сотрудникам (Факт / Норма) из табеля
                bool hasHours = TimesheetService.EnsureHoursColumns(conn);
                string daysCol = TimesheetService.DetectDaysColumnName(conn);

                string hoursSql = hasHours
                    ? $@"
                        SELECT e.FullName, ISNULL(p.Category, 1) AS Category, ISNULL(e.Rate, 1.0) AS Rate,
                               t.TimesheetID, t.[{daysCol}] AS FactDays, t.NormDays,
                               t.HoursWorked, t.NormHours
                        FROM Employees e
                        JOIN Positions p ON e.PositionID = p.PositionID
                        LEFT JOIN Timesheets t ON e.EmployeeID = t.EmployeeID AND t.[Month] = @m AND t.[Year] = @y
                        ORDER BY e.FullName, e.Rate DESC"
                    : $@"
                        SELECT e.FullName, ISNULL(p.Category, 1) AS Category, ISNULL(e.Rate, 1.0) AS Rate,
                               t.TimesheetID, t.[{daysCol}] AS FactDays, t.NormDays,
                               CAST(NULL AS DECIMAL(6, 2)) AS HoursWorked,
                               CAST(NULL AS DECIMAL(6, 2)) AS NormHours
                        FROM Employees e
                        JOIN Positions p ON e.PositionID = p.PositionID
                        LEFT JOIN Timesheets t ON e.EmployeeID = t.EmployeeID AND t.[Month] = @m AND t.[Year] = @y
                        ORDER BY e.FullName, e.Rate DESC";

                using (var cmd = new SqlCommand(hoursSql, conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string fullName = reader["FullName"].ToString();
                            int cat = Convert.ToInt32(reader["Category"]);
                            decimal rate = Convert.ToDecimal(reader["Rate"]);
                            if (rate <= 0m) rate = 1.0m;

                            bool hasTs = reader["TimesheetID"] != DBNull.Value;
                            int normDays = (hasTs && reader["NormDays"] != DBNull.Value && Convert.ToInt32(reader["NormDays"]) > 0)
                                ? Convert.ToInt32(reader["NormDays"])
                                : defaultNormDays;
                            int factDays = (hasTs && reader["FactDays"] != DBNull.Value)
                                ? Convert.ToInt32(reader["FactDays"])
                                : normDays;

                            decimal normHours = (hasTs && reader["NormHours"] != DBNull.Value && Convert.ToDecimal(reader["NormHours"]) > 0m)
                                ? Convert.ToDecimal(reader["NormHours"])
                                : TimesheetService.CalculateNormHours(normDays, cat, rate);
                            decimal factHours = (hasTs && reader["HoursWorked"] != DBNull.Value)
                                ? Convert.ToDecimal(reader["HoursWorked"])
                                : TimesheetService.CalculateNormHours(factDays, cat, rate);

                            data.EmployeeHours.Rows.Add(fullName, rate, factHours, normHours);
                        }
                    }
                }
            }

            return data;
        }

        private static int CalculateFullYears(DateTime startDate, DateTime endDate)
        {
            if (endDate <= startDate) return 0;
            int years = endDate.Year - startDate.Year;
            if (endDate < startDate.AddYears(years)) years--;
            return years < 0 ? 0 : years;
        }
    }
}

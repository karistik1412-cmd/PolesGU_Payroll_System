using System;
using System.Collections.Generic;
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

        // Агрегированный расчёт по одному сотруднику (суммирует основное место + внутреннее совместительство)
        private class CombinedEmployeePayroll
        {
            public int PrimaryEmployeeId { get; set; }
            public bool HasMainRecord { get; set; }
            public string FullName { get; set; }
            public decimal TotalRate { get; set; }
            public int DisplayFactDays { get; set; }
            public int DisplayNormDays { get; set; }
            public decimal TotalFactHours { get; set; }
            public decimal TotalNormHours { get; set; }
            public decimal TotalFirstHalfNormHours { get; set; }
            public int MaxSickDays { get; set; }
            public int MaxVacationDays { get; set; }
            public decimal TotalBaseSalary { get; set; }
            public decimal TotalBonus { get; set; }
            public decimal TotalSickPay { get; set; }
            public decimal TotalVacationPay { get; set; }
            public decimal PlannedMonthlyBase { get; set; }
            public decimal PlannedMonthlyBonus { get; set; }
            public int BenefitCategory { get; set; }
            public int ChildrenBenefit { get; set; }
        }

        // Автоматическое добавление столбцов SickVacationPay, TaxDeduction, AdvanceAmount в PayrollCalculations
        private static bool EnsurePayrollExtraColumns(SqlConnection conn, SqlTransaction tx = null)
        {
            if (HasPayrollExtraColumns(conn, tx)) return true;

            if (tx == null)
            {
                try
                {
                    string alterSql = @"
                        IF COL_LENGTH('dbo.PayrollCalculations', 'SickVacationPay') IS NULL
                            ALTER TABLE dbo.PayrollCalculations ADD SickVacationPay DECIMAL(10, 2) NOT NULL CONSTRAINT DF_PC_SickVac DEFAULT(0);
                        IF COL_LENGTH('dbo.PayrollCalculations', 'TaxDeduction') IS NULL
                            ALTER TABLE dbo.PayrollCalculations ADD TaxDeduction DECIMAL(10, 2) NOT NULL CONSTRAINT DF_PC_TaxDed DEFAULT(0);
                        IF COL_LENGTH('dbo.PayrollCalculations', 'AdvanceAmount') IS NULL
                            ALTER TABLE dbo.PayrollCalculations ADD AdvanceAmount DECIMAL(10, 2) NOT NULL CONSTRAINT DF_PC_Advance DEFAULT(0);";

                    using (var cmd = new SqlCommand(alterSql, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[РАСЧЕТ] Не удалось автоматически обновить PayrollCalculations: " + ex.Message);
                }
            }

            return HasPayrollExtraColumns(conn, tx);
        }

        private static bool HasPayrollExtraColumns(SqlConnection conn, SqlTransaction tx = null)
        {
            string sql = @"
                SELECT CASE
                    WHEN COL_LENGTH('dbo.PayrollCalculations', 'SickVacationPay') IS NOT NULL
                     AND COL_LENGTH('dbo.PayrollCalculations', 'TaxDeduction') IS NOT NULL
                     AND COL_LENGTH('dbo.PayrollCalculations', 'AdvanceAmount') IS NOT NULL
                    THEN 1 ELSE 0
                END";

            using (var cmd = new SqlCommand(sql, conn, tx))
            {
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        // Расчёт зарплаты за месяц и год.
        // halfMode = 1 — 1-я половина месяца (аванс, выплата в середине месяца);
        // halfMode = 2 — окончательный расчёт за месяц с разделением на 1-ю и 2-ю половину.
        public DataTable CalculatePayroll(int month, int year = DefaultYear, int halfMode = 2)
        {
            if (month < 1 || month > 12)
                throw new ArgumentException("Указан некорректный месяц.");

            // Запрет расчёта за ещё не наступивший будущий месяц
            DateTime today = DateTime.Today;
            DateTime periodStart = new DateTime(year, month, 1);
            if (periodStart > new DateTime(today.Year, today.Month, 1))
            {
                throw new InvalidOperationException(
                    $"Расчётный период ({month:00}.{year}) ещё не наступил (текущая дата: {today:dd.MM.yyyy}).");
            }

            int defaultNormDays = TimesheetService.GetWorkingDaysInMonth(year, month);
            int firstHalfWorkDays = TimesheetService.GetFirstHalfWorkingDays(year, month);

            var groupedMap = new Dictionary<string, CombinedEmployeePayroll>(StringComparer.OrdinalIgnoreCase);
            var orderedList = new List<CombinedEmployeePayroll>();

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();

                bool hasHours;
                bool hasAbsence;
                TimesheetService.EnsureTimesheetColumns(conn, out hasHours, out hasAbsence);

                bool hasEmpType;
                bool hasBenefits;
                EmployeeService.EnsureEmployeeColumns(conn, out hasEmpType, out hasBenefits);

                string daysCol = TimesheetService.DetectDaysColumnName(conn);
                string empTypeExpr = hasEmpType ? "ISNULL(e.EmploymentType, N'Основное')" : "N'Основное'";
                string benExpr = hasBenefits
                    ? "ISNULL(e.BenefitCategory, 0) AS BenefitCategory, ISNULL(e.ChildrenBenefit, 0) AS ChildrenBenefit"
                    : "0 AS BenefitCategory, 0 AS ChildrenBenefit";
                string hoursExpr = hasHours
                    ? "t.HoursWorked, t.NormHours"
                    : "CAST(NULL AS DECIMAL(6, 2)) AS HoursWorked, CAST(NULL AS DECIMAL(6, 2)) AS NormHours";
                string absExpr = hasAbsence
                    ? "ISNULL(t.SickDays, 0) AS SickDays, ISNULL(t.VacationDays, 0) AS VacationDays"
                    : "0 AS SickDays, 0 AS VacationDays";

                string selectQuery = $@"
                    SELECT e.EmployeeID, e.FullName, p.Coefficient, ISNULL(p.Category, 1) AS Category,
                           e.ExperienceYears, e.HireDate, ISNULL(e.Rate, 1.0) AS Rate,
                           {empTypeExpr} AS EmploymentType, {benExpr},
                           t.TimesheetID, t.[{daysCol}] AS FactDays, t.NormDays,
                           {hoursExpr}, {absExpr}
                    FROM Employees e
                    JOIN Positions p ON e.PositionID = p.PositionID
                    LEFT JOIN Timesheets t ON e.EmployeeID = t.EmployeeID AND t.[Month] = @m AND t.[Year] = @y
                    ORDER BY e.FullName,
                             CASE WHEN {empTypeExpr} LIKE N'%совмест%' THEN 1 ELSE 0 END,
                             e.Rate DESC";

                using (var cmd = new SqlCommand(selectQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int empId = Convert.ToInt32(reader["EmployeeID"]);
                            string fullName = reader["FullName"].ToString().Trim();
                            string personKey = EmployeeService.NormalizePersonName(fullName);
                            decimal coefficient = Convert.ToDecimal(reader["Coefficient"]);
                            int category = Convert.ToInt32(reader["Category"]);
                            decimal rate = Convert.ToDecimal(reader["Rate"]);
                            if (rate <= 0m) rate = 1.0m;

                            bool isPartTime = EmployeeService.IsPartTime(reader["EmploymentType"].ToString());
                            int benefitCategory = Convert.ToInt32(reader["BenefitCategory"]);
                            int childrenBenefit = Convert.ToInt32(reader["ChildrenBenefit"]);

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

                            int sickDays = hasTimesheet ? Math.Max(0, Convert.ToInt32(reader["SickDays"])) : 0;
                            int vacationDays = hasTimesheet ? Math.Max(0, Convert.ToInt32(reader["VacationDays"])) : 0;

                            // Точный расчёт полных лет стажа
                            DateTime hireDate = Convert.ToDateTime(reader["HireDate"]);
                            int experience = CalculateFullYears(hireDate, today);
                            if (reader["ExperienceYears"] != DBNull.Value)
                            {
                                int dbExp = Convert.ToInt32(reader["ExperienceYears"]);
                                if (dbExp > experience) experience = dbExp;
                            }

                            // Надбавка за стаж работы в бюджетной организации
                            decimal bonusPercent = 0m;
                            if (childrenBenefit > 0)
                            {
                                if ((childrenBenefit & (1 << 0)) != 0) bonusPercent += 0.50m;  // За работу в отрасли (50%)
                                if ((childrenBenefit & (1 << 1)) != 0) bonusPercent += 0.45m;  // За работу по контракту (45%)
                                if ((childrenBenefit & (1 << 2)) != 0) bonusPercent += 0.30m;  // За специфику в сфере образования (30%)
                                if ((childrenBenefit & (1 << 3)) != 0) bonusPercent += 0.30m;  // Молодому специалисту (30%)
                                if ((childrenBenefit & (1 << 4)) != 0) bonusPercent += 0.20m;  // Премия за текущий месяц (20%)
                                if ((childrenBenefit & (1 << 5)) != 0) bonusPercent += 0.125m; // За высокие достижения (12,5%)
                                if ((childrenBenefit & (1 << 6)) != 0) bonusPercent += 0.10m;  // За стаж работы (10%)
                                if ((childrenBenefit & (1 << 7)) != 0) bonusPercent += 1.00m;  // За кураторство (100%)
                            }
                            else
                            {
                                bonusPercent = 0.10m;
                                if (experience >= 5 && experience < 10) bonusPercent = 0.15m;
                                else if (experience >= 10 && experience < 15) bonusPercent = 0.20m;
                                else if (experience >= 15) bonusPercent = 0.30m;
                            }

                            // Плановый месячный оклад и надбавка по данной ставке (для аванса и среднедневного заработка)
                            decimal plannedTariff = Math.Round(BaseRate * coefficient * rate, 2, MidpointRounding.AwayFromZero);
                            decimal plannedBonus = Math.Round(plannedTariff * bonusPercent, 2, MidpointRounding.AwayFromZero);
                            decimal firstHalfHours = TimesheetService.CalculateNormHours(firstHalfWorkDays, category, rate);

                            // Оклад и надбавка за фактически отработанное время (Strategy)
                            IPayrollStrategy strategy = (hasExplicitHours && normHours > 0m)
                                ? _hourlyStrategy
                                : _dailyStrategy;

                            decimal baseSalary = strategy.CalculateBaseSalary(
                                BaseRate, coefficient, rate, factDays, normDays, factHours, normHours);
                            decimal bonus = Math.Round(baseSalary * bonusPercent, 2, MidpointRounding.AwayFromZero);

                            // Расчёт пособия по больничному (Б) и отпускных (О) исходя из среднедневного заработка
                            decimal sickPay = 0m;
                            decimal vacationPay = 0m;
                            if (normDays > 0 && (sickDays > 0 || vacationDays > 0))
                            {
                                decimal dailyAvg = (plannedTariff + plannedBonus) / normDays;

                                if (sickDays > 0)
                                {
                                    // Первые 12 дней больничного — 80% среднего заработка, с 13-го дня — 100%
                                    int days80 = Math.Min(sickDays, 12);
                                    int days100 = Math.Max(0, sickDays - 12);
                                    sickPay = Math.Round(dailyAvg * ((days80 * 0.80m) + days100), 2, MidpointRounding.AwayFromZero);
                                }

                                if (vacationDays > 0)
                                {
                                    // Оплачиваемый отпуск — 100% среднедневного заработка за дни отпуска
                                    vacationPay = Math.Round(dailyAvg * vacationDays, 2, MidpointRounding.AwayFromZero);
                                }
                            }

                            // Объединяем основную ставку и внутреннее совместительство одного преподавателя (1 + 0,5 = 1,5)
                            CombinedEmployeePayroll combined;
                            if (!groupedMap.TryGetValue(personKey, out combined))
                            {
                                combined = new CombinedEmployeePayroll
                                {
                                    PrimaryEmployeeId = empId,
                                    HasMainRecord = !isPartTime,
                                    FullName = fullName,
                                    DisplayFactDays = factDays,
                                    DisplayNormDays = normDays
                                };
                                groupedMap[personKey] = combined;
                                orderedList.Add(combined);
                            }
                            else
                            {
                                if (!isPartTime && !combined.HasMainRecord)
                                {
                                    combined.PrimaryEmployeeId = empId;
                                    combined.HasMainRecord = true;
                                    combined.DisplayFactDays = factDays;
                                    combined.DisplayNormDays = normDays;
                                }
                                else if (factDays > combined.DisplayFactDays && !combined.HasMainRecord)
                                {
                                    combined.DisplayFactDays = factDays;
                                }
                            }

                            combined.TotalRate += rate;
                            combined.TotalFactHours += factHours;
                            combined.TotalNormHours += normHours;
                            combined.TotalFirstHalfNormHours += firstHalfHours;
                            if (sickDays > combined.MaxSickDays) combined.MaxSickDays = sickDays;
                            if (vacationDays > combined.MaxVacationDays) combined.MaxVacationDays = vacationDays;

                            combined.TotalBaseSalary += baseSalary;
                            combined.TotalBonus += bonus;
                            combined.TotalSickPay += sickPay;
                            combined.TotalVacationPay += vacationPay;
                            combined.PlannedMonthlyBase += plannedTariff;
                            combined.PlannedMonthlyBonus += plannedBonus;

                            if (benefitCategory > combined.BenefitCategory) combined.BenefitCategory = benefitCategory;
                            if (childrenBenefit > combined.ChildrenBenefit) combined.ChildrenBenefit = childrenBenefit;
                        }
                    }
                }
            }

            // Режим 1: Расчёт 1-й половины месяца (аванс в середине месяца — 50% плановой чистой выплаты)
            if (halfMode == 1)
            {
                return BuildAdvanceTable(orderedList, firstHalfWorkDays);
            }

            // Режим 2: Полный расчёт за месяц с разделением выплаты на 1-ю половину (аванс) и 2-ю половину (доплата)
            return BuildFullPayrollTable(orderedList);
        }

        private static decimal CalculatePlannedAdvance(CombinedEmployeePayroll emp)
        {
            decimal plannedGross = emp.PlannedMonthlyBase + emp.PlannedMonthlyBonus;
            decimal plannedDed = EmployeeService.CalculateTaxDeduction(emp.BenefitCategory, emp.ChildrenBenefit, plannedGross);
            decimal plannedTax = Math.Round(Math.Max(0m, plannedGross - plannedDed) * 0.13m, 2, MidpointRounding.AwayFromZero);
            decimal plannedFszen = Math.Round(plannedGross * 0.01m, 2, MidpointRounding.AwayFromZero);
            decimal plannedUnion = Math.Round(plannedGross * 0.01m, 2, MidpointRounding.AwayFromZero);
            decimal plannedNet = Math.Max(0m,
                Math.Round(plannedGross - plannedTax - plannedFszen - plannedUnion, 2, MidpointRounding.AwayFromZero));
            return Math.Round(plannedNet * 0.50m, 2, MidpointRounding.AwayFromZero);
        }

        private static DataTable BuildAdvanceTable(List<CombinedEmployeePayroll> list, int firstHalfWorkDays)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("EmployeeID", typeof(int));
            dt.Columns.Add("ФИО", typeof(string));
            dt.Columns.Add("Ставка", typeof(decimal));
            dt.Columns.Add("Дни (1-я пол.), дн.", typeof(int));
            dt.Columns.Add("Часы (1-я пол.), ч.", typeof(decimal));
            dt.Columns.Add("Оклад (50%), руб.", typeof(decimal));
            dt.Columns.Add("Надбавка (50%), руб.", typeof(decimal));
            dt.Columns.Add("Аванс (1-я пол.), руб.", typeof(decimal));
            dt.Columns.Add("Этап выплаты", typeof(string));

            foreach (CombinedEmployeePayroll emp in list)
            {
                decimal halfBase = Math.Round(emp.PlannedMonthlyBase * 0.50m, 2, MidpointRounding.AwayFromZero);
                decimal halfBonus = Math.Round(emp.PlannedMonthlyBonus * 0.50m, 2, MidpointRounding.AwayFromZero);
                decimal advancePayout = CalculatePlannedAdvance(emp);

                dt.Rows.Add(
                    emp.PrimaryEmployeeId,
                    emp.FullName,
                    Math.Round(emp.TotalRate, 2),
                    firstHalfWorkDays,
                    Math.Round(emp.TotalFirstHalfNormHours, 1),
                    halfBase,
                    halfBonus,
                    advancePayout,
                    "Середина месяца (аванс)");
            }

            return dt;
        }

        private static DataTable BuildFullPayrollTable(List<CombinedEmployeePayroll> list)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("EmployeeID", typeof(int));
            dt.Columns.Add("ФИО", typeof(string));
            dt.Columns.Add("Ставка", typeof(decimal));
            dt.Columns.Add("Дни, дн.", typeof(string));
            dt.Columns.Add("Часы, ч.", typeof(string));
            dt.Columns.Add("Б / О, дн.", typeof(string));
            dt.Columns.Add("Оклад, руб.", typeof(decimal));
            dt.Columns.Add("Надбавка, руб.", typeof(decimal));
            dt.Columns.Add("Б/О, руб.", typeof(decimal));
            dt.Columns.Add("Вычет, руб.", typeof(decimal));
            dt.Columns.Add("НДФЛ, руб.", typeof(decimal));
            dt.Columns.Add("ФСЗН, руб.", typeof(decimal));
            dt.Columns.Add("Профс., руб.", typeof(decimal));
            dt.Columns.Add("1-я пол., руб.", typeof(decimal));
            dt.Columns.Add("2-я пол., руб.", typeof(decimal));
            dt.Columns.Add("К выплате, руб.", typeof(decimal));

            foreach (CombinedEmployeePayroll emp in list)
            {
                decimal sickVacPay = Math.Round(emp.TotalSickPay + emp.TotalVacationPay, 2, MidpointRounding.AwayFromZero);
                decimal grossSalary = emp.TotalBaseSalary + emp.TotalBonus + sickVacPay;

                // Стандартный налоговый вычет (льготы) применяется один раз к общему доходу сотрудника
                decimal appliedDeduction = EmployeeService.CalculateTaxDeduction(
                    emp.BenefitCategory, emp.ChildrenBenefit, grossSalary);

                decimal taxableBase = Math.Max(0m, grossSalary - appliedDeduction);
                decimal taxAmount = Math.Round(taxableBase * 0.13m, 2, MidpointRounding.AwayFromZero);

                // С пособия по временной нетрудоспособности (Б) взнос ФСЗН 1% и профсоюзный 1% не удерживаются
                decimal fszenBase = emp.TotalBaseSalary + emp.TotalBonus + emp.TotalVacationPay;
                decimal fszenAmount = Math.Round(fszenBase * 0.01m, 2, MidpointRounding.AwayFromZero);
                decimal unionAmount = Math.Round(fszenBase * 0.01m, 2, MidpointRounding.AwayFromZero);

                decimal netSalary = Math.Max(0m,
                    Math.Round(grossSalary - taxAmount - fszenAmount - unionAmount, 2, MidpointRounding.AwayFromZero));

                // 1-я половина (аванс в середине месяца) и 2-я половина (окончательный расчёт в конце месяца)
                decimal plannedAdv = CalculatePlannedAdvance(emp);
                decimal firstHalfPay = (plannedAdv > 0m && plannedAdv < netSalary)
                    ? plannedAdv
                    : Math.Round(netSalary * 0.50m, 2, MidpointRounding.AwayFromZero);
                decimal secondHalfPay = Math.Round(netSalary - firstHalfPay, 2, MidpointRounding.AwayFromZero);

                string daysDisplay = $"{emp.DisplayFactDays}/{emp.DisplayNormDays}";
                string hoursDisplay = $"{emp.TotalFactHours:0.#}/{emp.TotalNormHours:0.#}";
                string boDisplay = (emp.MaxSickDays == 0 && emp.MaxVacationDays == 0)
                    ? "—"
                    : $"{emp.MaxSickDays} / {emp.MaxVacationDays}";

                dt.Rows.Add(
                    emp.PrimaryEmployeeId,
                    emp.FullName,
                    Math.Round(emp.TotalRate, 2),
                    daysDisplay,
                    hoursDisplay,
                    boDisplay,
                    emp.TotalBaseSalary,
                    emp.TotalBonus,
                    sickVacPay,
                    appliedDeduction,
                    taxAmount,
                    fszenAmount,
                    unionAmount,
                    firstHalfPay,
                    secondHalfPay,
                    netSalary);
            }

            return dt;
        }

        private static decimal ReadMoneyColumn(DataRow row, params string[] names)
        {
            foreach (string n in names)
            {
                if (row.Table.Columns.Contains(n) && row[n] != DBNull.Value)
                    return Convert.ToDecimal(row[n]);
            }
            return 0m;
        }

        // Сохранение расчёта зарплаты в БД в рамках транзакции
        public bool SavePayroll(DataTable payrollTable, int month, int year = DefaultYear)
        {
            if (payrollTable == null || payrollTable.Rows.Count == 0) return false;

            if (!payrollTable.Columns.Contains("К выплате, руб.") && !payrollTable.Columns.Contains("К выплате"))
            {
                throw new InvalidOperationException(
                    "В базу сохраняется итоговый расчёт за месяц. Выберите режим «Итог месяца (1-я + 2-я пол.)» и нажмите «Рассчитать».");
            }

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasExtra = EnsurePayrollExtraColumns(conn);

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        using (var delCmd = new SqlCommand(
                            "DELETE FROM PayrollCalculations WHERE [Month] = @m AND [Year] = @y", conn, tx))
                        {
                            delCmd.Parameters.AddWithValue("@m", month);
                            delCmd.Parameters.AddWithValue("@y", year);
                            delCmd.ExecuteNonQuery();
                        }

                        string insertQuery = hasExtra
                            ? @"INSERT INTO PayrollCalculations
                                (EmployeeID, [Month], [Year], BaseSalary, Bonus, SickVacationPay, TaxDeduction,
                                 TaxAmount, FszenAmount, UnionAmount, AdvanceAmount, NetSalary)
                                VALUES (@id, @m, @y, @base, @bonus, @sickVac, @ded, @tax, @fszen, @union, @adv, @net)"
                            : @"INSERT INTO PayrollCalculations
                                (EmployeeID, [Month], [Year], BaseSalary, Bonus, TaxAmount, FszenAmount, UnionAmount, NetSalary)
                                VALUES (@id, @m, @y, @base, @bonus, @tax, @fszen, @union, @net)";

                        foreach (DataRow row in payrollTable.Rows)
                        {
                            int empId = Convert.ToInt32(row["EmployeeID"]);
                            decimal baseSal = ReadMoneyColumn(row, "Оклад, руб.", "Оклад");
                            decimal bonus = ReadMoneyColumn(row, "Надбавка, руб.", "Надбавка");
                            decimal sickVac = ReadMoneyColumn(row, "Б/О, руб.");
                            decimal ded = ReadMoneyColumn(row, "Вычет, руб.");
                            decimal tax = ReadMoneyColumn(row, "НДФЛ, руб.", "НДФЛ (13%)");
                            decimal fszen = ReadMoneyColumn(row, "ФСЗН, руб.", "ФСЗН (1%)");
                            decimal union = ReadMoneyColumn(row, "Профс., руб.", "Профсоюз (1%)");
                            decimal adv = ReadMoneyColumn(row, "1-я пол., руб.");
                            decimal net = ReadMoneyColumn(row, "К выплате, руб.", "К выплате");

                            using (var insCmd = new SqlCommand(insertQuery, conn, tx))
                            {
                                insCmd.Parameters.AddWithValue("@id", empId);
                                insCmd.Parameters.AddWithValue("@m", month);
                                insCmd.Parameters.AddWithValue("@y", year);
                                insCmd.Parameters.AddWithValue("@base", baseSal);
                                insCmd.Parameters.AddWithValue("@bonus", hasExtra ? bonus : bonus + sickVac);
                                if (hasExtra)
                                {
                                    insCmd.Parameters.AddWithValue("@sickVac", sickVac);
                                    insCmd.Parameters.AddWithValue("@ded", ded);
                                    insCmd.Parameters.AddWithValue("@adv", adv);
                                }
                                insCmd.Parameters.AddWithValue("@tax", tax);
                                insCmd.Parameters.AddWithValue("@fszen", fszen);
                                insCmd.Parameters.AddWithValue("@union", union);
                                insCmd.Parameters.AddWithValue("@net", net);
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

            if (dt.Columns.Contains("Кафедра"))
            {
                foreach (DataRow row in dt.Rows)
                    row["Кафедра"] = EmployeeService.FormatDepartmentName(row["Кафедра"].ToString());
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
                bool hasExtra = EnsurePayrollExtraColumns(conn);
                string grossExpr = hasExtra
                    ? "pc.BaseSalary + pc.Bonus + ISNULL(pc.SickVacationPay, 0)"
                    : "pc.BaseSalary + pc.Bonus";

                // 1. Фонд оплаты труда по кафедрам (Начислено и К выплате)
                string deptSql = $@"
                    SELECT d.DepartmentName AS [Кафедра],
                           SUM({grossExpr}) AS [Начислено],
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

                // Убираем слово «Кафедра » из названий кафедр
                if (data.DepartmentPayroll.Columns.Contains("Кафедра"))
                {
                    foreach (DataRow row in data.DepartmentPayroll.Rows)
                        row["Кафедра"] = EmployeeService.FormatDepartmentName(row["Кафедра"].ToString());
                }

                // 2. Структура фонда оплаты труда за выбранный месяц
                string extraSelect = hasExtra
                    ? "ISNULL(SUM(ISNULL(SickVacationPay, 0)), 0) AS SumSickVac, ISNULL(SUM(ISNULL(TaxDeduction, 0)), 0) AS SumDed,"
                    : "CAST(0 AS DECIMAL(10, 2)) AS SumSickVac, CAST(0 AS DECIMAL(10, 2)) AS SumDed,";

                string structSql = $@"
                    SELECT COUNT(*) AS Cnt,
                           ISNULL(SUM(BaseSalary), 0) AS SumBase,
                           ISNULL(SUM(Bonus), 0) AS SumBonus,
                           {extraSelect}
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
                            data.TotalSickVacationPay = Convert.ToDecimal(reader["SumSickVac"]);
                            data.TotalTaxDeduction = Convert.ToDecimal(reader["SumDed"]);
                            data.TotalTax = Convert.ToDecimal(reader["SumTax"]);
                            data.TotalFszen = Convert.ToDecimal(reader["SumFszen"]);
                            data.TotalUnion = Convert.ToDecimal(reader["SumUnion"]);
                            data.TotalNetSalary = Convert.ToDecimal(reader["SumNet"]);
                        }
                    }
                }

                // 3. Помесячная динамика начислений и выплат за весь выбранный год
                string trendGross = hasExtra
                    ? "BaseSalary + Bonus + ISNULL(SickVacationPay, 0)"
                    : "BaseSalary + Bonus";
                string trendSql = $@"
                    SELECT [Month],
                           SUM({trendGross}) AS [Начислено],
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

                // 4. Отработка часов по сотрудникам (Факт / Норма) с суммированием совместительства
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

                var hoursByPerson = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);

                using (var cmd = new SqlCommand(hoursSql, conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string fullName = reader["FullName"].ToString().Trim();
                            string key = EmployeeService.NormalizePersonName(fullName);
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

                            DataRow existingRow;
                            if (hoursByPerson.TryGetValue(key, out existingRow))
                            {
                                existingRow["Ставка"] = Convert.ToDecimal(existingRow["Ставка"]) + rate;
                                existingRow["ЧасыФакт"] = Convert.ToDecimal(existingRow["ЧасыФакт"]) + factHours;
                                existingRow["ЧасыНорма"] = Convert.ToDecimal(existingRow["ЧасыНорма"]) + normHours;
                            }
                            else
                            {
                                DataRow newRow = data.EmployeeHours.Rows.Add(fullName, rate, factHours, normHours);
                                hoursByPerson[key] = newRow;
                            }
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

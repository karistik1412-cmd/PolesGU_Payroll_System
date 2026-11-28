using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using PayrollServer.Database;
using PayrollServer.Models;

namespace PayrollServer.Logic
{
    public class TimesheetService
    {
        private class EmployeeLookup
        {
            public int EmployeeId { get; set; }
            public string FullName { get; set; }
            public string NormalizedName { get; set; }
            public string Surname { get; set; }
            public string Initials { get; set; }
            public decimal Rate { get; set; }
            public int Category { get; set; }
            public bool IsPartTime { get; set; }
        }

        // Расчёт количества рабочих дней (пн-пт) в указанном месяце
        public static int GetWorkingDaysInMonth(int year, int month)
        {
            if (year < 2000 || year > 2100) year = 2026;
            if (month < 1 || month > 12) month = 1;

            int daysInMonth = DateTime.DaysInMonth(year, month);
            int workDays = 0;

            for (int day = 1; day <= daysInMonth; day++)
            {
                DayOfWeek dow = new DateTime(year, month, day).DayOfWeek;
                if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday)
                {
                    workDays++;
                }
            }

            return workDays;
        }

        // Рабочие дни первой половины месяца (1–15 число)
        public static int GetFirstHalfWorkingDays(int year, int month)
        {
            if (year < 2000 || year > 2100) year = 2026;
            if (month < 1 || month > 12) month = 1;

            int limit = Math.Min(15, DateTime.DaysInMonth(year, month));
            int workDays = 0;

            for (int day = 1; day <= limit; day++)
            {
                DayOfWeek dow = new DateTime(year, month, day).DayOfWeek;
                if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday)
                {
                    workDays++;
                }
            }

            return workDays;
        }

        // Дневная норма часов: 7.2 ч для ППС (36-часовая неделя, ст. 114 ТК РБ), 8.0 ч для прочего персонала
        public static decimal GetDailyNormHours(int category)
        {
            return category == 1 ? 7.2m : 8.0m;
        }

        // Расчёт месячной нормы часов с учётом категории персонала и ставки
        public static decimal CalculateNormHours(int normDays, int category, decimal rate)
        {
            if (rate <= 0m) rate = 1.0m;
            return Math.Round(normDays * GetDailyNormHours(category) * rate, 2, MidpointRounding.AwayFromZero);
        }

        // Определяем реальное имя столбца отработанных дней в БД (DaysWorked или FactDays)
        public static string DetectDaysColumnName(SqlConnection conn, SqlTransaction tx = null)
        {
            string sql = @"
                SELECT CASE
                    WHEN COL_LENGTH('dbo.Timesheets', 'DaysWorked') IS NOT NULL THEN 'DaysWorked'
                    WHEN COL_LENGTH('dbo.Timesheets', 'FactDays') IS NOT NULL THEN 'FactDays'
                    ELSE 'DaysWorked'
                END";

            using (var cmd = new SqlCommand(sql, conn, tx))
            {
                object res = cmd.ExecuteScalar();
                string col = res != null ? res.ToString() : "DaysWorked";
                return col == "FactDays" ? "FactDays" : "DaysWorked";
            }
        }

        // Проверяем и автоматически добавляем столбцы часов (HoursWorked, NormHours) и неявок (SickDays, VacationDays)
        public static void EnsureTimesheetColumns(SqlConnection conn, out bool hasHours, out bool hasAbsence)
        {
            hasHours = HasHoursColumns(conn);
            hasAbsence = HasAbsenceColumns(conn);

            if (hasHours && hasAbsence) return;

            try
            {
                string alterSql = @"
                    IF COL_LENGTH('dbo.Timesheets', 'HoursWorked') IS NULL
                        ALTER TABLE dbo.Timesheets ADD HoursWorked DECIMAL(6, 2) NULL;
                    IF COL_LENGTH('dbo.Timesheets', 'NormHours') IS NULL
                        ALTER TABLE dbo.Timesheets ADD NormHours DECIMAL(6, 2) NULL;
                    IF COL_LENGTH('dbo.Timesheets', 'SickDays') IS NULL
                        ALTER TABLE dbo.Timesheets ADD SickDays INT NOT NULL CONSTRAINT DF_Ts_SickDays DEFAULT(0);
                    IF COL_LENGTH('dbo.Timesheets', 'VacationDays') IS NULL
                        ALTER TABLE dbo.Timesheets ADD VacationDays INT NOT NULL CONSTRAINT DF_Ts_VacationDays DEFAULT(0);";

                using (var cmd = new SqlCommand(alterSql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ТАБЕЛЬ] Не удалось автоматически обновить столбцы Timesheets: " + ex.Message);
            }

            hasHours = HasHoursColumns(conn);
            hasAbsence = HasAbsenceColumns(conn);
        }

        public static bool EnsureHoursColumns(SqlConnection conn)
        {
            bool hasHours;
            bool hasAbsence;
            EnsureTimesheetColumns(conn, out hasHours, out hasAbsence);
            return hasHours;
        }

        private static bool HasHoursColumns(SqlConnection conn, SqlTransaction tx = null)
        {
            string checkSql = @"
                SELECT CASE
                    WHEN COL_LENGTH('dbo.Timesheets', 'HoursWorked') IS NOT NULL
                     AND COL_LENGTH('dbo.Timesheets', 'NormHours') IS NOT NULL
                    THEN 1 ELSE 0
                END";

            using (var cmd = new SqlCommand(checkSql, conn, tx))
            {
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        private static bool HasAbsenceColumns(SqlConnection conn, SqlTransaction tx = null)
        {
            string checkSql = @"
                SELECT CASE
                    WHEN COL_LENGTH('dbo.Timesheets', 'SickDays') IS NOT NULL
                     AND COL_LENGTH('dbo.Timesheets', 'VacationDays') IS NOT NULL
                    THEN 1 ELSE 0
                END";

            using (var cmd = new SqlCommand(checkSql, conn, tx))
            {
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        // Получение табеля за выбранный месяц и год (без лишних столбцов Должность/Ставка на экране)
        public DataTable GetTimesheets(int month, int year)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("EmployeeID", typeof(int));
            dt.Columns.Add("Category", typeof(int));
            dt.Columns.Add("Rate", typeof(decimal));
            dt.Columns.Add("RawFullName", typeof(string));
            dt.Columns.Add("ФИО", typeof(string));
            dt.Columns.Add("Отработано, дн.", typeof(int));
            dt.Columns.Add("Норма, дн.", typeof(int));
            dt.Columns.Add("Отработано, ч.", typeof(decimal));
            dt.Columns.Add("Норма, ч.", typeof(decimal));
            dt.Columns.Add("Больничный (Б), дн.", typeof(int));
            dt.Columns.Add("Отпуск (О), дн.", typeof(int));
            dt.Columns.Add("Статус", typeof(string));

            int defaultNormDays = GetWorkingDaysInMonth(year, month);

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasHours;
                bool hasAbsence;
                EnsureTimesheetColumns(conn, out hasHours, out hasAbsence);

                bool hasEmpType;
                bool hasBenefits;
                EmployeeService.EnsureEmployeeColumns(conn, out hasEmpType, out hasBenefits);

                string daysCol = DetectDaysColumnName(conn);
                string empTypeExpr = hasEmpType ? "ISNULL(e.EmploymentType, N'Основное')" : "N'Основное'";
                string hoursExpr = hasHours
                    ? "t.HoursWorked, t.NormHours"
                    : "CAST(NULL AS DECIMAL(6, 2)) AS HoursWorked, CAST(NULL AS DECIMAL(6, 2)) AS NormHours";
                string absenceExpr = hasAbsence
                    ? "ISNULL(t.SickDays, 0) AS SickDays, ISNULL(t.VacationDays, 0) AS VacationDays"
                    : "0 AS SickDays, 0 AS VacationDays";

                string query = $@"
                    SELECT e.EmployeeID, e.FullName, ISNULL(p.Category, 1) AS Category,
                           ISNULL(e.Rate, 1.0) AS Rate, {empTypeExpr} AS EmploymentType,
                           t.TimesheetID, t.[{daysCol}] AS FactDays, t.NormDays,
                           {hoursExpr}, {absenceExpr}
                    FROM Employees e
                    JOIN Positions p ON e.PositionID = p.PositionID
                    LEFT JOIN Timesheets t ON e.EmployeeID = t.EmployeeID AND t.[Month] = @m AND t.[Year] = @y
                    ORDER BY e.FullName,
                             CASE WHEN {empTypeExpr} LIKE N'%совмест%' THEN 1 ELSE 0 END,
                             e.Rate DESC";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int empId = Convert.ToInt32(reader["EmployeeID"]);
                            int category = Convert.ToInt32(reader["Category"]);
                            string rawFullName = reader["FullName"].ToString();
                            decimal rate = Convert.ToDecimal(reader["Rate"]);
                            string empType = reader["EmploymentType"].ToString();
                            bool isPartTime = EmployeeService.IsPartTime(empType);

                            // Пометку «(совм.)» в ФИО не добавляем: в табеле уже есть столбец
                            // «Статус», а клиент переводит его на выбранный язык интерфейса.
                            string displayName = rawFullName;

                            bool hasRecord = reader["TimesheetID"] != DBNull.Value;
                            int normDays = (hasRecord && reader["NormDays"] != DBNull.Value && Convert.ToInt32(reader["NormDays"]) > 0)
                                ? Convert.ToInt32(reader["NormDays"])
                                : defaultNormDays;

                            int factDays = (hasRecord && reader["FactDays"] != DBNull.Value)
                                ? Convert.ToInt32(reader["FactDays"])
                                : normDays;

                            decimal normHours = (hasRecord && reader["NormHours"] != DBNull.Value && Convert.ToDecimal(reader["NormHours"]) > 0m)
                                ? Convert.ToDecimal(reader["NormHours"])
                                : CalculateNormHours(normDays, category, rate);

                            decimal factHours = (hasRecord && reader["HoursWorked"] != DBNull.Value)
                                ? Convert.ToDecimal(reader["HoursWorked"])
                                : CalculateNormHours(factDays, category, rate);

                            int sickDays = hasRecord ? Math.Max(0, Convert.ToInt32(reader["SickDays"])) : 0;
                            int vacationDays = hasRecord ? Math.Max(0, Convert.ToInt32(reader["VacationDays"])) : 0;

                            string status = hasRecord ? "Из табеля" : "По норме";

                            dt.Rows.Add(
                                empId, category, rate, rawFullName, displayName,
                                factDays, normDays, factHours, normHours,
                                sickDays, vacationDays, status);
                        }
                    }
                }
            }

            return dt;
        }

        // Импорт и сохранение данных табеля в БД (включая больничные Б и отпуска О)
        public TimesheetImportResult ImportTimesheets(int month, int year, List<TimesheetEntry> entries)
        {
            var result = new TimesheetImportResult();
            if (entries == null || entries.Count == 0)
            {
                result.Message = "В файле табеля не найдено записей для импорта.";
                return result;
            }

            if (month < 1 || month > 12)
                throw new ArgumentException("Некорректный номер месяца.");
            if (year < 2000 || year > 2100)
                throw new ArgumentException("Некорректный год.");

            result.TotalCount = entries.Count;
            int defaultNormDays = GetWorkingDaysInMonth(year, month);

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasHours;
                bool hasAbsence;
                EnsureTimesheetColumns(conn, out hasHours, out hasAbsence);

                bool hasEmpType;
                bool hasBenefits;
                EmployeeService.EnsureEmployeeColumns(conn, out hasEmpType, out hasBenefits);

                string daysCol = DetectDaysColumnName(conn);
                List<EmployeeLookup> employees = LoadEmployeeLookup(conn, hasEmpType);

                var matchedEmployeeIds = new HashSet<int>();

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        foreach (TimesheetEntry entry in entries)
                        {
                            EmployeeLookup matched = FindMatchingEmployee(entry, employees, matchedEmployeeIds);
                            if (matched == null)
                            {
                                string label = !string.IsNullOrWhiteSpace(entry.FullName)
                                    ? entry.FullName.Trim()
                                    : $"ID {entry.EmployeeId}";
                                if (!result.UnmatchedNames.Contains(label))
                                    result.UnmatchedNames.Add(label);
                                continue;
                            }

                            matchedEmployeeIds.Add(matched.EmployeeId);

                            int normDays = entry.NormDays > 0 ? entry.NormDays : defaultNormDays;
                            int daysWorked = entry.DaysWorked < 0 ? 0 : entry.DaysWorked;
                            int sickDays = entry.SickDays < 0 ? 0 : Math.Min(31, entry.SickDays);
                            int vacationDays = entry.VacationDays < 0 ? 0 : Math.Min(31, entry.VacationDays);

                            decimal normHours = entry.NormHours > 0m
                                ? Math.Round(entry.NormHours, 2, MidpointRounding.AwayFromZero)
                                : CalculateNormHours(normDays, matched.Category, matched.Rate);

                            decimal hoursWorked;
                            if (entry.HasHours || entry.HoursWorked > 0m || sickDays > 0 || vacationDays > 0)
                            {
                                hoursWorked = entry.HoursWorked < 0m
                                    ? 0m
                                    : Math.Round(entry.HoursWorked, 2, MidpointRounding.AwayFromZero);
                            }
                            else
                            {
                                hoursWorked = CalculateNormHours(daysWorked, matched.Category, matched.Rate);
                            }

                            // Если в файле были указаны только часы, оцениваем отработанные дни
                            if (daysWorked == 0 && hoursWorked > 0m)
                            {
                                decimal dailyForEmployee = GetDailyNormHours(matched.Category) * (matched.Rate > 0m ? matched.Rate : 1.0m);
                                if (dailyForEmployee > 0m)
                                {
                                    daysWorked = Math.Max(1, (int)Math.Round(hoursWorked / dailyForEmployee, MidpointRounding.AwayFromZero));
                                    if (daysWorked > 31) daysWorked = normDays;
                                }
                            }

                            // Если в файле были указаны только дни без часов, но есть Б/О — считаем часы по факту дней
                            if (!entry.HasHours && hoursWorked == 0m && daysWorked > 0)
                            {
                                hoursWorked = CalculateNormHours(daysWorked, matched.Category, matched.Rate);
                            }

                            UpsertTimesheet(conn, tx, daysCol, hasHours, hasAbsence, matched.EmployeeId, month, year,
                                daysWorked, normDays, hoursWorked, normHours, sickDays, vacationDays);
                            result.SavedCount++;
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

            var sb = new StringBuilder();
            sb.Append($"Загружено записей табеля: {result.SavedCount} из {result.TotalCount}.");
            if (result.UnmatchedNames.Count > 0)
            {
                sb.Append("\nНе найдены в базе сотрудников: ");
                sb.Append(string.Join(", ", result.UnmatchedNames.Take(10)));
                if (result.UnmatchedNames.Count > 10)
                    sb.Append($" (и ещё {result.UnmatchedNames.Count - 10})");
            }
            result.Message = sb.ToString();

            return result;
        }

        public int ClearTimesheets(int month, int year)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                using (var cmd = new SqlCommand("DELETE FROM Timesheets WHERE [Month] = @m AND [Year] = @y", conn))
                {
                    cmd.Parameters.AddWithValue("@m", month);
                    cmd.Parameters.AddWithValue("@y", year);
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        private void UpsertTimesheet(
            SqlConnection conn,
            SqlTransaction tx,
            string daysCol,
            bool hasHours,
            bool hasAbsence,
            int employeeId,
            int month,
            int year,
            int daysWorked,
            int normDays,
            decimal hoursWorked,
            decimal normHours,
            int sickDays,
            int vacationDays)
        {
            string setClause = $"[{daysCol}] = @days, NormDays = @normDays";
            if (hasHours) setClause += ", HoursWorked = @hours, NormHours = @normHours";
            if (hasAbsence) setClause += ", SickDays = @sick, VacationDays = @vac";

            string updateSql = $@"UPDATE Timesheets SET {setClause}
                                  WHERE EmployeeID = @id AND [Month] = @m AND [Year] = @y";

            int affected;
            using (var updCmd = new SqlCommand(updateSql, conn, tx))
            {
                updCmd.Parameters.AddWithValue("@id", employeeId);
                updCmd.Parameters.AddWithValue("@m", month);
                updCmd.Parameters.AddWithValue("@y", year);
                updCmd.Parameters.AddWithValue("@days", daysWorked);
                updCmd.Parameters.AddWithValue("@normDays", normDays);
                if (hasHours)
                {
                    updCmd.Parameters.AddWithValue("@hours", hoursWorked);
                    updCmd.Parameters.AddWithValue("@normHours", normHours);
                }
                if (hasAbsence)
                {
                    updCmd.Parameters.AddWithValue("@sick", sickDays);
                    updCmd.Parameters.AddWithValue("@vac", vacationDays);
                }
                affected = updCmd.ExecuteNonQuery();
            }

            if (affected == 0)
            {
                string cols = $"EmployeeID, [Month], [Year], [{daysCol}], NormDays";
                string vals = "@id, @m, @y, @days, @normDays";
                if (hasHours)
                {
                    cols += ", HoursWorked, NormHours";
                    vals += ", @hours, @normHours";
                }
                if (hasAbsence)
                {
                    cols += ", SickDays, VacationDays";
                    vals += ", @sick, @vac";
                }

                string insertSql = $"INSERT INTO Timesheets ({cols}) VALUES ({vals})";
                using (var insCmd = new SqlCommand(insertSql, conn, tx))
                {
                    insCmd.Parameters.AddWithValue("@id", employeeId);
                    insCmd.Parameters.AddWithValue("@m", month);
                    insCmd.Parameters.AddWithValue("@y", year);
                    insCmd.Parameters.AddWithValue("@days", daysWorked);
                    insCmd.Parameters.AddWithValue("@normDays", normDays);
                    if (hasHours)
                    {
                        insCmd.Parameters.AddWithValue("@hours", hoursWorked);
                        insCmd.Parameters.AddWithValue("@normHours", normHours);
                    }
                    if (hasAbsence)
                    {
                        insCmd.Parameters.AddWithValue("@sick", sickDays);
                        insCmd.Parameters.AddWithValue("@vac", vacationDays);
                    }
                    insCmd.ExecuteNonQuery();
                }
            }
        }

        private List<EmployeeLookup> LoadEmployeeLookup(SqlConnection conn, bool hasEmpType)
        {
            var list = new List<EmployeeLookup>();
            string empTypeExpr = hasEmpType ? "ISNULL(e.EmploymentType, N'Основное')" : "N'Основное'";
            string sql = $@"
                SELECT e.EmployeeID, e.FullName, ISNULL(e.Rate, 1.0) AS Rate,
                       ISNULL(p.Category, 1) AS Category, {empTypeExpr} AS EmploymentType
                FROM Employees e
                JOIN Positions p ON e.PositionID = p.PositionID
                ORDER BY CASE WHEN {empTypeExpr} LIKE N'%совмест%' THEN 1 ELSE 0 END,
                         e.Rate DESC";

            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string fullName = reader["FullName"].ToString();
                    string surname;
                    string initials;
                    ExtractSurnameAndInitials(fullName, out surname, out initials);

                    list.Add(new EmployeeLookup
                    {
                        EmployeeId = Convert.ToInt32(reader["EmployeeID"]),
                        FullName = fullName,
                        NormalizedName = NormalizeName(fullName),
                        Surname = surname,
                        Initials = initials,
                        Rate = Convert.ToDecimal(reader["Rate"]),
                        Category = Convert.ToInt32(reader["Category"]),
                        IsPartTime = EmployeeService.IsPartTime(reader["EmploymentType"].ToString())
                    });
                }
            }

            return list;
        }

        private EmployeeLookup FindMatchingEmployee(
            TimesheetEntry entry,
            List<EmployeeLookup> employees,
            HashSet<int> alreadyMatched)
        {
            bool wantsPartTime = !string.IsNullOrWhiteSpace(entry.FullName) &&
                                 entry.FullName.IndexOf("совм", StringComparison.OrdinalIgnoreCase) >= 0;
            string entryNorm = NormalizeName(entry.FullName);

            // 1. Точное совпадение по EmployeeID
            if (entry.EmployeeId > 0)
            {
                EmployeeLookup byId = employees.FirstOrDefault(e => e.EmployeeId == entry.EmployeeId);
                if (byId != null && (string.IsNullOrEmpty(entryNorm) || byId.NormalizedName == entryNorm))
                    return byId;
            }

            if (string.IsNullOrEmpty(entryNorm))
                return null;

            // 2. Если в ФИО явно помечено «(совм.)» — сопоставляем строго с записью по совместительству
            if (wantsPartTime)
            {
                EmployeeLookup byPartTime = employees.FirstOrDefault(
                    e => !alreadyMatched.Contains(e.EmployeeId) && e.NormalizedName == entryNorm && e.IsPartTime);
                if (byPartTime != null) return byPartTime;

                if (employees.Any(e => e.NormalizedName == entryNorm))
                    return null;
            }

            // 3. Точное совпадение по ФИО + ставке (различает основную 1,0 и совместительство 0,5)
            if (entry.Rate.HasValue)
            {
                decimal r = entry.Rate.Value;
                EmployeeLookup byNameAndRate = employees.FirstOrDefault(
                    e => !alreadyMatched.Contains(e.EmployeeId)
                         && e.NormalizedName == entryNorm
                         && Math.Abs(e.Rate - r) < 0.01m);
                if (byNameAndRate != null) return byNameAndRate;
            }

            // 4. Точное совпадение по нормализованному ФИО (сначала основное место, затем совместительство)
            EmployeeLookup byName = employees.FirstOrDefault(
                e => !alreadyMatched.Contains(e.EmployeeId) && e.NormalizedName == entryNorm);
            if (byName != null) return byName;

            // 5. Совпадение по фамилии и инициалам (например, «Харитонюк Е.Н.» или родительный падеж из ИП)
            string entrySurname;
            string entryInitials;
            ExtractSurnameAndInitials(entry.FullName, out entrySurname, out entryInitials);

            if (!string.IsNullOrEmpty(entrySurname))
            {
                var surnameCandidates = employees
                    .Where(e => SurnamesMatch(e.Surname, entrySurname)
                                && (string.IsNullOrEmpty(entryInitials)
                                    || string.IsNullOrEmpty(e.Initials)
                                    || e.Initials.StartsWith(entryInitials)
                                    || entryInitials.StartsWith(e.Initials)))
                    .ToList();

                if (wantsPartTime)
                {
                    EmployeeLookup pt = surnameCandidates.FirstOrDefault(
                        e => !alreadyMatched.Contains(e.EmployeeId) && e.IsPartTime);
                    if (pt != null) return pt;
                }

                if (entry.Rate.HasValue)
                {
                    decimal r = entry.Rate.Value;
                    EmployeeLookup withRate = surnameCandidates.FirstOrDefault(
                        e => !alreadyMatched.Contains(e.EmployeeId) && Math.Abs(e.Rate - r) < 0.01m);
                    if (withRate != null) return withRate;
                }

                EmployeeLookup unmatchedCandidate = surnameCandidates.FirstOrDefault(e => !alreadyMatched.Contains(e.EmployeeId));
                if (unmatchedCandidate != null)
                    return unmatchedCandidate;
            }

            return null;
        }

        // Очищает ФИО от служебных пометок вроде «(совм.)» и лишних пробелов
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            string cleaned = name.Trim().ToLowerInvariant().Replace('ё', 'е');
            int parenIdx = cleaned.IndexOf('(');
            if (parenIdx > 0)
            {
                cleaned = cleaned.Substring(0, parenIdx).Trim();
            }

            string[] parts = cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        private static void ExtractSurnameAndInitials(string name, out string surname, out string initials)
        {
            surname = string.Empty;
            initials = string.Empty;
            string norm = NormalizeName(name);
            if (string.IsNullOrEmpty(norm)) return;

            string cleaned = norm.Replace('.', ' ');
            string[] parts = cleaned.Split(new[] { ' ', '\t', '\r', '\n', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            if (parts[0].Length == 1 && parts.Length >= 2 && parts[parts.Length - 1].Length > 1)
            {
                surname = parts[parts.Length - 1];
                var sb = new StringBuilder();
                for (int i = 0; i < parts.Length - 1 && sb.Length < 2; i++)
                {
                    if (char.IsLetter(parts[i][0])) sb.Append(parts[i][0]);
                }
                initials = sb.ToString();
            }
            else
            {
                surname = parts[0];
                var sb = new StringBuilder();
                for (int i = 1; i < parts.Length && sb.Length < 2; i++)
                {
                    if (char.IsLetter(parts[i][0])) sb.Append(parts[i][0]);
                }
                initials = sb.ToString();
            }
        }

        private static bool SurnamesMatch(string dbSurname, string fileSurname)
        {
            if (string.IsNullOrEmpty(dbSurname) || string.IsNullOrEmpty(fileSurname))
                return false;
            if (dbSurname == fileSurname)
                return true;

            if (fileSurname == dbSurname + "а" || fileSurname == dbSurname + "я")
                return true;
            if (dbSurname.EndsWith("а") && fileSurname == dbSurname.Substring(0, dbSurname.Length - 1) + "ой")
                return true;
            if (dbSurname.EndsWith("ая") && fileSurname == dbSurname.Substring(0, dbSurname.Length - 2) + "ой")
                return true;
            if (dbSurname.EndsWith("ий") && fileSurname == dbSurname.Substring(0, dbSurname.Length - 2) + "ого")
                return true;
            if (dbSurname.EndsWith("ый") && fileSurname == dbSurname.Substring(0, dbSurname.Length - 2) + "ого")
                return true;

            return false;
        }
    }
}

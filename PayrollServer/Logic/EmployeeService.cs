using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    public class EmployeeService
    {
        // Названия льготных категорий работника (ст. 209 НК РБ)
        public static readonly string[] BenefitCategoryNames =
        {
            "Нет",
            "Стандартный вычет (192 руб.)",
            "Инвалид I–II гр. / ветеран / ЧАЭС (272 руб.)",
            "Молодой специалист (620 руб.)"
        };

        // Названия вычетов на детей и иждивенцев (п. 1.2 ст. 209 НК РБ)
        public static readonly string[] ChildrenBenefitNames =
        {
            "Нет",
            "1 ребёнок (56 руб.)",
            "2 ребёнка (214 руб.)",
            "3 и более детей (321 руб.)",
            "Одинокий родитель / ребёнок-инвалид (107 руб.)"
        };

        // Расчёт суммы стандартных налоговых вычетов (руб.) по ст. 209 НК РБ
        public static decimal CalculateTaxDeduction(int benefitCategory, int childrenBenefit, decimal grossSalary)
        {
            if (grossSalary <= 0m) return 0m;

            decimal deduction = 0m;

            switch (benefitCategory)
            {
                case 1:
                    // Стандартный вычет предоставляется, если месячный доход не превышает 1 164 руб.
                    if (grossSalary <= 1164.00m) deduction += 192.00m;
                    break;
                case 2:
                    // Повышенный вычет для инвалидов I–II гр., ветеранов, пострадавших от ЧАЭС
                    deduction += 272.00m;
                    break;
                case 3:
                    // Вычет для молодых специалистов
                    deduction += 620.00m;
                    break;
            }

            switch (childrenBenefit)
            {
                case 1: deduction += 56.00m; break;
                case 2: deduction += 214.00m; break;
                case 3: deduction += 321.00m; break;
                case 4: deduction += 107.00m; break;
            }

            // Применённый вычет не может превышать саму начисленную сумму
            return Math.Min(grossSalary, deduction);
        }

        // Компактная подпись льгот для таблицы «Сотрудники»
        public static string FormatBenefitsSummary(int benefitCategory, int childrenBenefit)
        {
            var parts = new List<string>();

            switch (benefitCategory)
            {
                case 1: parts.Add("Стандартный (192 р.)"); break;
                case 2: parts.Add("Инвалид/ветеран (272 р.)"); break;
                case 3: parts.Add("Молодой спец. (620 р.)"); break;
            }

            switch (childrenBenefit)
            {
                case 1: parts.Add("1 реб. (56 р.)"); break;
                case 2: parts.Add("2 реб. (214 р.)"); break;
                case 3: parts.Add("3+ дет. (321 р.)"); break;
                case 4: parts.Add("Льгота на реб. (107 р.)"); break;
            }

            return parts.Count > 0 ? string.Join("; ", parts) : "Нет";
        }

        // Убирает повторяющееся слово «Кафедра » в начале названия, оставляя специализацию с заглавной буквы
        public static string FormatDepartmentName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;

            string trimmed = rawName.Trim();
            const string prefix = "кафедра ";
            if (trimmed.Length > prefix.Length &&
                trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string rest = trimmed.Substring(prefix.Length).TrimStart();
                if (rest.Length == 0) return trimmed;
                if (rest.Length == 1) return rest.ToUpper();
                return char.ToUpper(rest[0]) + rest.Substring(1);
            }

            return trimmed;
        }

        public static string NormalizePersonName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
            string cleaned = fullName.Trim().ToLowerInvariant().Replace('ё', 'е');
            string[] parts = cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        public static string NormalizeEmploymentType(string empType)
        {
            if (string.IsNullOrWhiteSpace(empType)) return "Основное";
            string lower = empType.Trim().ToLowerInvariant();
            if (lower.Contains("совмест")) return "Внутреннее совместительство";
            return "Основное";
        }

        public static bool IsPartTime(string empType)
        {
            return !string.IsNullOrWhiteSpace(empType) &&
                   empType.IndexOf("совмест", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Автоматическое добавление недостающих столбцов совместительства и льгот в Employees
        public static void EnsureEmployeeColumns(SqlConnection conn, out bool hasEmpType, out bool hasBenefits)
        {
            hasEmpType = CheckColumn(conn, "EmploymentType");
            hasBenefits = CheckColumn(conn, "BenefitCategory") && CheckColumn(conn, "ChildrenBenefit");

            if (hasEmpType && hasBenefits) return;

            try
            {
                string alterSql = @"
                    IF COL_LENGTH('dbo.Employees', 'EmploymentType') IS NULL
                        ALTER TABLE dbo.Employees ADD EmploymentType NVARCHAR(60) NOT NULL CONSTRAINT DF_Emp_EmpType DEFAULT(N'Основное');
                    IF COL_LENGTH('dbo.Employees', 'BenefitCategory') IS NULL
                        ALTER TABLE dbo.Employees ADD BenefitCategory INT NOT NULL CONSTRAINT DF_Emp_BenCat DEFAULT(0);
                    IF COL_LENGTH('dbo.Employees', 'ChildrenBenefit') IS NULL
                        ALTER TABLE dbo.Employees ADD ChildrenBenefit INT NOT NULL CONSTRAINT DF_Emp_ChildBen DEFAULT(0);";

                using (var cmd = new SqlCommand(alterSql, conn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[СОТРУДНИКИ] Не удалось автоматически обновить столбцы Employees: " + ex.Message);
            }

            hasEmpType = CheckColumn(conn, "EmploymentType");
            hasBenefits = CheckColumn(conn, "BenefitCategory") && CheckColumn(conn, "ChildrenBenefit");
        }

        private static bool CheckColumn(SqlConnection conn, string colName)
        {
            using (var cmd = new SqlCommand(
                "SELECT CASE WHEN COL_LENGTH('dbo.Employees', @col) IS NOT NULL THEN 1 ELSE 0 END", conn))
            {
                cmd.Parameters.AddWithValue("@col", colName);
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        public DataTable GetEmployees()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("EmployeeID", typeof(int));
            dt.Columns.Add("PositionID", typeof(int));
            dt.Columns.Add("DepartmentID", typeof(int));
            dt.Columns.Add("BenefitCategory", typeof(int));
            dt.Columns.Add("ChildrenBenefit", typeof(int));
            dt.Columns.Add("ФИО", typeof(string));
            dt.Columns.Add("Кафедра", typeof(string));
            dt.Columns.Add("Должность", typeof(string));
            dt.Columns.Add("Стаж, лет", typeof(int));
            dt.Columns.Add("Ставка", typeof(decimal));
            dt.Columns.Add("Тип занятости", typeof(string));
            dt.Columns.Add("Льготы", typeof(string));
            dt.Columns.Add("Дата приёма", typeof(DateTime));

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasEmpType;
                bool hasBenefits;
                EnsureEmployeeColumns(conn, out hasEmpType, out hasBenefits);

                string empTypeExpr = hasEmpType ? "ISNULL(e.EmploymentType, N'Основное')" : "N'Основное'";
                string benCatExpr = hasBenefits ? "ISNULL(e.BenefitCategory, 0)" : "0";
                string childBenExpr = hasBenefits ? "ISNULL(e.ChildrenBenefit, 0)" : "0";

                string query = $@"
                    SELECT e.EmployeeID, e.PositionID, e.DepartmentID,
                           e.FullName, d.DepartmentName, p.PositionName,
                           ISNULL(e.ExperienceYears, 0) AS ExperienceYears,
                           ISNULL(e.Rate, 1.0) AS Rate,
                           {empTypeExpr} AS EmploymentType,
                           {benCatExpr} AS BenefitCategory,
                           {childBenExpr} AS ChildrenBenefit,
                           e.HireDate
                    FROM Employees e
                    JOIN Positions p ON e.PositionID = p.PositionID
                    JOIN Departments d ON e.DepartmentID = d.DepartmentID
                    ORDER BY e.FullName,
                             CASE WHEN {empTypeExpr} LIKE N'%совмест%' THEN 1 ELSE 0 END,
                             e.Rate DESC";

                using (var cmd = new SqlCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int empId = Convert.ToInt32(reader["EmployeeID"]);
                        int posId = Convert.ToInt32(reader["PositionID"]);
                        int deptId = Convert.ToInt32(reader["DepartmentID"]);
                        int benCat = Convert.ToInt32(reader["BenefitCategory"]);
                        int childBen = Convert.ToInt32(reader["ChildrenBenefit"]);
                        string fullName = reader["FullName"].ToString();
                        string dept = FormatDepartmentName(reader["DepartmentName"].ToString());
                        string pos = reader["PositionName"].ToString();
                        decimal rate = Convert.ToDecimal(reader["Rate"]);
                        string empType = NormalizeEmploymentType(reader["EmploymentType"].ToString());
                        DateTime hireDate = Convert.ToDateTime(reader["HireDate"]);

                        int exp = CalculateFullYears(hireDate, DateTime.Today);
                        int dbExp = Convert.ToInt32(reader["ExperienceYears"]);
                        if (dbExp > exp) exp = dbExp;

                        string benefitsLabel = FormatBenefitsSummary(benCat, childBen);

                        dt.Rows.Add(
                            empId, posId, deptId, benCat, childBen,
                            fullName, dept, pos, exp, rate, empType, benefitsLabel, hireDate);
                    }
                }
            }

            return dt;
        }

        public bool AddEmployee(
            string fullName,
            int positionId,
            int departmentId,
            decimal rate,
            string empType,
            int benefitCategory = 0,
            int childrenBenefit = 0)
        {
            string cleanName = (fullName ?? string.Empty).Trim();
            string normalizedType = NormalizeEmploymentType(empType);

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasEmpType;
                bool hasBenefits;
                EnsureEmployeeColumns(conn, out hasEmpType, out hasBenefits);

                ValidateEmploymentRules(conn, null, cleanName, departmentId, positionId, normalizedType, hasEmpType);

                // Если добавляется совместительство существующему сотруднику и льготы не выбраны — наследуем его льготы
                if (hasBenefits && IsPartTime(normalizedType) && benefitCategory == 0 && childrenBenefit == 0)
                {
                    LoadExistingBenefitsForPerson(conn, cleanName, ref benefitCategory, ref childrenBenefit);
                }

                string query;
                if (hasEmpType && hasBenefits)
                {
                    query = @"INSERT INTO Employees
                              (FullName, HireDate, ExperienceYears, PositionID, DepartmentID, Rate, EmploymentType, BenefitCategory, ChildrenBenefit)
                              VALUES (@name, GETDATE(), 0, @pos, @dept, @rate, @type, @ben, @child)";
                }
                else if (hasEmpType)
                {
                    query = @"INSERT INTO Employees
                              (FullName, HireDate, ExperienceYears, PositionID, DepartmentID, Rate, EmploymentType)
                              VALUES (@name, GETDATE(), 0, @pos, @dept, @rate, @type)";
                }
                else
                {
                    query = @"INSERT INTO Employees
                              (FullName, HireDate, ExperienceYears, PositionID, DepartmentID, Rate)
                              VALUES (@name, GETDATE(), 0, @pos, @dept, @rate)";
                }

                int inserted;
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@name", cleanName);
                    cmd.Parameters.AddWithValue("@pos", positionId);
                    cmd.Parameters.AddWithValue("@dept", departmentId);
                    cmd.Parameters.AddWithValue("@rate", rate);
                    if (hasEmpType) cmd.Parameters.AddWithValue("@type", normalizedType);
                    if (hasBenefits)
                    {
                        cmd.Parameters.AddWithValue("@ben", benefitCategory);
                        cmd.Parameters.AddWithValue("@child", childrenBenefit);
                    }
                    inserted = cmd.ExecuteNonQuery();
                }

                // Синхронизируем персональные льготы между основным местом и совместительством одного сотрудника
                if (inserted > 0 && hasBenefits && (!IsPartTime(normalizedType) || benefitCategory > 0 || childrenBenefit > 0))
                {
                    SyncBenefitsForPerson(conn, cleanName, benefitCategory, childrenBenefit);
                }

                return inserted > 0;
            }
        }

        public bool UpdateEmployee(
            int empId,
            string fullName,
            int positionId,
            int departmentId,
            decimal rate,
            string empType,
            int benefitCategory = 0,
            int childrenBenefit = 0)
        {
            string cleanName = (fullName ?? string.Empty).Trim();
            string normalizedType = NormalizeEmploymentType(empType);

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasEmpType;
                bool hasBenefits;
                EnsureEmployeeColumns(conn, out hasEmpType, out hasBenefits);

                ValidateEmploymentRules(conn, empId, cleanName, departmentId, positionId, normalizedType, hasEmpType);

                string query;
                if (hasEmpType && hasBenefits)
                {
                    query = @"UPDATE Employees
                              SET FullName = @name, PositionID = @pos, DepartmentID = @dept,
                                  Rate = @rate, EmploymentType = @type,
                                  BenefitCategory = @ben, ChildrenBenefit = @child
                              WHERE EmployeeID = @id";
                }
                else if (hasEmpType)
                {
                    query = @"UPDATE Employees
                              SET FullName = @name, PositionID = @pos, DepartmentID = @dept,
                                  Rate = @rate, EmploymentType = @type
                              WHERE EmployeeID = @id";
                }
                else
                {
                    query = @"UPDATE Employees
                              SET FullName = @name, PositionID = @pos, DepartmentID = @dept, Rate = @rate
                              WHERE EmployeeID = @id";
                }

                int updated;
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", empId);
                    cmd.Parameters.AddWithValue("@name", cleanName);
                    cmd.Parameters.AddWithValue("@pos", positionId);
                    cmd.Parameters.AddWithValue("@dept", departmentId);
                    cmd.Parameters.AddWithValue("@rate", rate);
                    if (hasEmpType) cmd.Parameters.AddWithValue("@type", normalizedType);
                    if (hasBenefits)
                    {
                        cmd.Parameters.AddWithValue("@ben", benefitCategory);
                        cmd.Parameters.AddWithValue("@child", childrenBenefit);
                    }
                    updated = cmd.ExecuteNonQuery();
                }

                if (updated > 0 && hasBenefits)
                {
                    SyncBenefitsForPerson(conn, cleanName, benefitCategory, childrenBenefit);
                }

                return updated > 0;
            }
        }

        // Проверка правил основного места работы и внутреннего совместительства
        private static void ValidateEmploymentRules(
            SqlConnection conn,
            int? editingEmpId,
            string fullName,
            int departmentId,
            int positionId,
            string normalizedType,
            bool hasEmpType)
        {
            if (!hasEmpType) return;

            string targetNorm = NormalizePersonName(fullName);
            if (string.IsNullOrEmpty(targetNorm)) return;

            string sql = @"SELECT EmployeeID, FullName, DepartmentID, PositionID,
                                  ISNULL(EmploymentType, N'Основное') AS EmploymentType
                           FROM Employees";

            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    int existingId = Convert.ToInt32(reader["EmployeeID"]);
                    if (editingEmpId.HasValue && existingId == editingEmpId.Value) continue;

                    string existingNorm = NormalizePersonName(reader["FullName"].ToString());
                    if (existingNorm != targetNorm) continue;

                    string existingType = NormalizeEmploymentType(reader["EmploymentType"].ToString());
                    int existingDept = Convert.ToInt32(reader["DepartmentID"]);
                    int existingPos = Convert.ToInt32(reader["PositionID"]);

                    // У сотрудника может быть только одно «Основное» место работы, а вторая запись — «Внутреннее совместительство»
                    if (!IsPartTime(normalizedType) && !IsPartTime(existingType))
                    {
                        throw new InvalidOperationException(
                            $"У сотрудника «{fullName}» уже оформлено основное место работы.\n" +
                            "Чтобы добавить вторую ставку (на этой же или другой кафедре), выберите тип занятости «Внутреннее совместительство».");
                    }

                    // Защита от случайного полного дубля совместительства на той же кафедре и должности
                    if (IsPartTime(normalizedType) && IsPartTime(existingType)
                        && existingDept == departmentId && existingPos == positionId)
                    {
                        throw new InvalidOperationException(
                            $"У сотрудника «{fullName}» уже оформлено внутреннее совместительство на этой кафедре по данной должности.\n" +
                            "При необходимости измените ставку через кнопку «Редактировать».");
                    }
                }
            }
        }

        private static void LoadExistingBenefitsForPerson(
            SqlConnection conn,
            string fullName,
            ref int benefitCategory,
            ref int childrenBenefit)
        {
            string targetNorm = NormalizePersonName(fullName);
            string sql = "SELECT FullName, ISNULL(BenefitCategory, 0) AS B, ISNULL(ChildrenBenefit, 0) AS C FROM Employees";
            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (NormalizePersonName(reader["FullName"].ToString()) == targetNorm)
                    {
                        int b = Convert.ToInt32(reader["B"]);
                        int c = Convert.ToInt32(reader["C"]);
                        if (b > benefitCategory) benefitCategory = b;
                        if (c > childrenBenefit) childrenBenefit = c;
                    }
                }
            }
        }

        private static void SyncBenefitsForPerson(SqlConnection conn, string fullName, int benefitCategory, int childrenBenefit)
        {
            string targetNorm = NormalizePersonName(fullName);
            var ids = new List<int>();

            using (var cmd = new SqlCommand("SELECT EmployeeID, FullName FROM Employees", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (NormalizePersonName(reader["FullName"].ToString()) == targetNorm)
                        ids.Add(Convert.ToInt32(reader["EmployeeID"]));
                }
            }

            foreach (int id in ids)
            {
                using (var upd = new SqlCommand(
                    "UPDATE Employees SET BenefitCategory = @b, ChildrenBenefit = @c WHERE EmployeeID = @id", conn))
                {
                    upd.Parameters.AddWithValue("@b", benefitCategory);
                    upd.Parameters.AddWithValue("@c", childrenBenefit);
                    upd.Parameters.AddWithValue("@id", id);
                    upd.ExecuteNonQuery();
                }
            }
        }

        public DataTable GetPositions()
        {
            DataTable dt = new DataTable();
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = "SELECT PositionID, PositionName, Category FROM Positions";
                using (var adapter = new SqlDataAdapter(query, conn))
                {
                    adapter.Fill(dt);
                }
            }
            return dt;
        }

        public DataTable GetDepartments()
        {
            DataTable dt = new DataTable();
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = "SELECT DepartmentID, DepartmentName, DepartmentType FROM Departments";
                using (var adapter = new SqlDataAdapter(query, conn))
                {
                    adapter.Fill(dt);
                }
            }

            // Убираем повторяющееся слово «Кафедра » из начала названий кафедр
            if (dt.Columns.Contains("DepartmentName"))
            {
                foreach (DataRow row in dt.Rows)
                {
                    if (row["DepartmentName"] != DBNull.Value)
                        row["DepartmentName"] = FormatDepartmentName(row["DepartmentName"].ToString());
                }
            }

            return dt;
        }

        public bool DeleteEmployee(int empId)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd1 = new SqlCommand("DELETE FROM PayrollCalculations WHERE EmployeeID = @id", conn, tx))
                        {
                            cmd1.Parameters.AddWithValue("@id", empId);
                            cmd1.ExecuteNonQuery();
                        }

                        using (var cmd2 = new SqlCommand("DELETE FROM Timesheets WHERE EmployeeID = @id", conn, tx))
                        {
                            cmd2.Parameters.AddWithValue("@id", empId);
                            cmd2.ExecuteNonQuery();
                        }

                        int deleted;
                        using (var cmd = new SqlCommand("DELETE FROM Employees WHERE EmployeeID = @id", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@id", empId);
                            deleted = cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                        return deleted > 0;
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
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

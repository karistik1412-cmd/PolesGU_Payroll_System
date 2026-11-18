using System;
using System.Data;
using System.Data.SqlClient;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    public class PayrollService
    {
        private const decimal BaseRate = 250.00m;
        private const int CurrentYear = 2026; // Год пока оставим фиксированным

        // ТЕПЕРЬ МЕТОД ПРИНИМАЕТ МЕСЯЦ
        public DataTable CalculatePayroll(int month)
        {
            DataTable resultTable = new DataTable();

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                // Берем ставку и данные табеля (если табеля нет, считаем, что норма = факт = 21 день)
                string selectQuery = @"
                    SELECT e.EmployeeID, e.FullName, p.Coefficient, e.ExperienceYears, e.HireDate, e.Rate, 
                           ISNULL(t.FactDays, 21) AS FactDays, 
                           ISNULL(t.NormDays, 21) AS NormDays
                    FROM Employees e
                    JOIN Positions p ON e.PositionID = p.PositionID
                    LEFT JOIN Timesheets t ON e.EmployeeID = t.EmployeeID AND t.Month = @m AND t.Year = @y";

                SqlCommand cmd = new SqlCommand(selectQuery, conn);
                cmd.Parameters.AddWithValue("@m", month);
                cmd.Parameters.AddWithValue("@y", CurrentYear);
                SqlDataReader reader = cmd.ExecuteReader();

                resultTable.Columns.Add("EmployeeID", typeof(int));
                resultTable.Columns.Add("ФИО", typeof(string));
                resultTable.Columns.Add("Оклад", typeof(decimal));
                resultTable.Columns.Add("Надбавка", typeof(decimal));
                resultTable.Columns.Add("НДФЛ (13%)", typeof(decimal));
                resultTable.Columns.Add("ФСЗН (1%)", typeof(decimal));
                resultTable.Columns.Add("Профсоюз (1%)", typeof(decimal));
                resultTable.Columns.Add("К выплате", typeof(decimal));

                while (reader.Read())
                {
                    int empId = Convert.ToInt32(reader["EmployeeID"]);
                    string fullName = reader["FullName"].ToString();
                    decimal coefficient = Convert.ToDecimal(reader["Coefficient"]);
                    decimal rate = reader["Rate"] != DBNull.Value ? Convert.ToDecimal(reader["Rate"]) : 1.0m;

                    int factDays = Convert.ToInt32(reader["FactDays"]);
                    int normDays = Convert.ToInt32(reader["NormDays"]);

                    int hireYear = Convert.ToDateTime(reader["HireDate"]).Year;
                    int experience = DateTime.Now.Year - hireYear;

                    // Считаем коэффициент отработки (сколько отработал от нормы)
                    decimal daysRatio = (decimal)factDays / normDays;

                    // Оклад = БазоваяСтавка * Коэффициент * Ставка * Отработка
                    decimal baseSalary = BaseRate * coefficient * rate * daysRatio;

                    // Надбавка за стаж (считается от оклада)
                    decimal bonusPercent = 0.10m;
                    if (experience >= 5 && experience < 10) bonusPercent = 0.15m;
                    else if (experience >= 10 && experience < 15) bonusPercent = 0.20m;
                    else if (experience >= 15) bonusPercent = 0.30m;

                    decimal bonus = baseSalary * bonusPercent;
                    decimal grossSalary = baseSalary + bonus;

                    decimal taxAmount = grossSalary * 0.13m;
                    decimal fszenAmount = grossSalary * 0.01m;
                    decimal unionAmount = grossSalary * 0.01m;
                    decimal netSalary = grossSalary - taxAmount - fszenAmount - unionAmount;

                    resultTable.Rows.Add(empId, fullName, baseSalary, bonus, taxAmount, fszenAmount, unionAmount, netSalary);
                }
                reader.Close();
            }
            return resultTable;
        }

        // МЕТОД СОХРАНЕНИЯ ТОЖЕ ПРИНИМАЕТ МЕСЯЦ
        public bool SavePayroll(DataTable payrollTable, int month)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();

                string deleteQuery = "DELETE FROM PayrollCalculations WHERE Month = @m AND Year = @y";
                using (var delCmd = new SqlCommand(deleteQuery, conn))
                {
                    delCmd.Parameters.AddWithValue("@m", month);
                    delCmd.Parameters.AddWithValue("@y", CurrentYear);
                    delCmd.ExecuteNonQuery();
                }

                foreach (DataRow row in payrollTable.Rows)
                {
                    // БЕРЕМ ID НАПРЯМУЮ ИЗ ТАБЛИЦЫ! Больше никаких поисков по ФИО.
                    int empId = Convert.ToInt32(row["EmployeeID"]);

                    string insertQuery = @"
                        INSERT INTO PayrollCalculations 
                        (EmployeeID, Month, Year, BaseSalary, Bonus, TaxAmount, FszenAmount, UnionAmount, NetSalary) 
                        VALUES (@id, @m, @y, @base, @bonus, @tax, @fszen, @union, @net)";

                    using (var insCmd = new SqlCommand(insertQuery, conn))
                    {
                        insCmd.Parameters.AddWithValue("@id", empId);
                        insCmd.Parameters.AddWithValue("@m", month);
                        insCmd.Parameters.AddWithValue("@y", CurrentYear);
                        insCmd.Parameters.AddWithValue("@base", row["Оклад"]);
                        insCmd.Parameters.AddWithValue("@bonus", row["Надбавка"]);
                        insCmd.Parameters.AddWithValue("@tax", row["НДФЛ (13%)"]);
                        insCmd.Parameters.AddWithValue("@fszen", row["ФСЗН (1%)"]);
                        insCmd.Parameters.AddWithValue("@union", row["Профсоюз (1%)"]);
                        insCmd.Parameters.AddWithValue("@net", row["К выплате"]);
                        insCmd.ExecuteNonQuery();
                    }
                }
            }
            return true;
        }

        public DataTable GetAnalyticsData(int month)
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
                    WHERE pc.Month = @m AND pc.Year = @y
                    GROUP BY d.DepartmentName";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@m", month);
                cmd.Parameters.AddWithValue("@y", CurrentYear);

                SqlDataAdapter adapter = new SqlDataAdapter(cmd);
                adapter.Fill(dt);
            }
            return dt;
        }
    }
}
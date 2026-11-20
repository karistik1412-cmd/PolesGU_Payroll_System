using System;
using System.Data.SqlClient;
using System.Globalization;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    public class DashboardService
    {
        public string GetStats()
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();

                // Количество уникальных сотрудников в штате (совместитель считается как 1 человек)
                string empSql = "SELECT COUNT(DISTINCT LTRIM(RTRIM(LOWER(FullName)))) FROM Employees";
                int empCount;
                using (var cmd1 = new SqlCommand(empSql, conn))
                {
                    empCount = Convert.ToInt32(cmd1.ExecuteScalar());
                }

                int userCount;
                using (var cmd2 = new SqlCommand("SELECT COUNT(*) FROM Users", conn))
                {
                    userCount = Convert.ToInt32(cmd2.ExecuteScalar());
                }

                decimal totalFund;
                using (var cmd3 = new SqlCommand("SELECT SUM(NetSalary) FROM PayrollCalculations", conn))
                {
                    object sumObj = cmd3.ExecuteScalar();
                    totalFund = (sumObj != null && sumObj != DBNull.Value) ? Convert.ToDecimal(sumObj) : 0m;
                }

                // Считаем учебные кафедры (DepartmentType = 1), если столбец есть, иначе все записи Departments
                bool hasDeptType;
                using (var chk = new SqlCommand(
                    "SELECT CASE WHEN COL_LENGTH('dbo.Departments', 'DepartmentType') IS NOT NULL THEN 1 ELSE 0 END", conn))
                {
                    hasDeptType = Convert.ToInt32(chk.ExecuteScalar()) == 1;
                }

                int deptCount;
                string deptSql = hasDeptType
                    ? "SELECT COUNT(*) FROM Departments WHERE DepartmentType = 1"
                    : "SELECT COUNT(*) FROM Departments";
                using (var cmd4 = new SqlCommand(deptSql, conn))
                {
                    deptCount = Convert.ToInt32(cmd4.ExecuteScalar());
                    if (deptCount == 0 && hasDeptType)
                    {
                        using (var fallback = new SqlCommand("SELECT COUNT(*) FROM Departments", conn))
                        {
                            deptCount = Convert.ToInt32(fallback.ExecuteScalar());
                        }
                    }
                }

                return $"{empCount}|{userCount}|{totalFund.ToString(CultureInfo.InvariantCulture)}|{deptCount}";
            }
        }
    }
}

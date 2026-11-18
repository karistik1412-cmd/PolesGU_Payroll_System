using System.Data.SqlClient;
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

                var cmd1 = new SqlCommand("SELECT COUNT(*) FROM Employees", conn);
                int empCount = (int)cmd1.ExecuteScalar();

                var cmd2 = new SqlCommand("SELECT COUNT(*) FROM Users", conn);
                int userCount = (int)cmd2.ExecuteScalar();

                var cmd3 = new SqlCommand("SELECT SUM(NetSalary) FROM PayrollCalculations", conn);
                object sumObj = cmd3.ExecuteScalar();
                decimal totalFund = sumObj != System.DBNull.Value ? (decimal)sumObj : 0;

                var cmd4 = new SqlCommand("SELECT COUNT(*) FROM Departments", conn);
                int deptCount = (int)cmd4.ExecuteScalar();

                // Возвращаем строку, которую клиент разобьет (сумма в InvariantCulture)
                return $"{empCount}|{userCount}|{totalFund.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{deptCount}";
            }
        }
    }
}
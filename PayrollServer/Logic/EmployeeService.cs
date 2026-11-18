using System.Data;
using System.Data.SqlClient;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    public class EmployeeService
    {
        private static bool HasEmploymentTypeColumn(SqlConnection conn)
        {
            using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('dbo.Employees', 'EmploymentType') IS NOT NULL THEN 1 ELSE 0 END", conn))
            {
                return System.Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        public DataTable GetEmployees()
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasView;
                using (var chk = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.View_EmployeeDetails', 'V') IS NOT NULL THEN 1 ELSE 0 END", conn))
                {
                    hasView = System.Convert.ToInt32(chk.ExecuteScalar()) == 1;
                }

                string query;
                if (hasView)
                {
                    // Используем представление (View), созданное в SQL
                    query = "SELECT * FROM View_EmployeeDetails";
                }
                else
                {
                    query = @"
                        SELECT e.EmployeeID, e.PositionID, e.DepartmentID,
                               e.FullName AS [ФИО], d.DepartmentName AS [Кафедра],
                               p.PositionName AS [Должность], e.ExperienceYears AS [Стаж],
                               ISNULL(e.Rate, 1.0) AS [Ставка], e.HireDate AS [Дата приема]
                        FROM Employees e
                        JOIN Positions p ON e.PositionID = p.PositionID
                        JOIN Departments d ON e.DepartmentID = d.DepartmentID";
                }

                using (var adapter = new SqlDataAdapter(query, conn))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    return dt;
                }
            }
        }

        public bool AddEmployee(string fullName, int positionId, int departmentId, decimal rate, string empType)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasEmpType = HasEmploymentTypeColumn(conn);
                string query = hasEmpType
                    ? @"INSERT INTO Employees (FullName, HireDate, ExperienceYears, PositionID, DepartmentID, Rate, EmploymentType) 
                        VALUES (@name, GETDATE(), 0, @pos, @dept, @rate, @type)"
                    : @"INSERT INTO Employees (FullName, HireDate, ExperienceYears, PositionID, DepartmentID, Rate) 
                        VALUES (@name, GETDATE(), 0, @pos, @dept, @rate)";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@name", fullName);
                    cmd.Parameters.AddWithValue("@pos", positionId);
                    cmd.Parameters.AddWithValue("@dept", departmentId);
                    cmd.Parameters.AddWithValue("@rate", rate);
                    if (hasEmpType) cmd.Parameters.AddWithValue("@type", empType);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool UpdateEmployee(int empId, string fullName, int positionId, int departmentId, decimal rate, string empType)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                bool hasEmpType = HasEmploymentTypeColumn(conn);
                string query = hasEmpType
                    ? @"UPDATE Employees 
                        SET FullName = @name, PositionID = @pos, DepartmentID = @dept, Rate = @rate, EmploymentType = @type 
                        WHERE EmployeeID = @id"
                    : @"UPDATE Employees 
                        SET FullName = @name, PositionID = @pos, DepartmentID = @dept, Rate = @rate 
                        WHERE EmployeeID = @id";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", empId);
                    cmd.Parameters.AddWithValue("@name", fullName);
                    cmd.Parameters.AddWithValue("@pos", positionId);
                    cmd.Parameters.AddWithValue("@dept", departmentId);
                    cmd.Parameters.AddWithValue("@rate", rate);
                    if (hasEmpType) cmd.Parameters.AddWithValue("@type", empType);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }
        public DataTable GetPositions()
        {
            DataTable dt = new DataTable();
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                // ДОБАВИЛИ Category в запрос
                string query = "SELECT PositionID, PositionName, Category FROM Positions";
                SqlDataAdapter adapter = new SqlDataAdapter(query, conn);
                adapter.Fill(dt);
            }
            return dt;
        }
        public DataTable GetDepartments()
        {
            DataTable dt = new DataTable();
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                // ДОБАВИЛИ DepartmentType
                string query = "SELECT DepartmentID, DepartmentName, DepartmentType FROM Departments";
                SqlDataAdapter adapter = new SqlDataAdapter(query, conn);
                adapter.Fill(dt);
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
                        // 1. Сначала удаляем все расчеты этого сотрудника
                        using (var cmd1 = new SqlCommand("DELETE FROM PayrollCalculations WHERE EmployeeID = @id", conn, tx))
                        {
                            cmd1.Parameters.AddWithValue("@id", empId);
                            cmd1.ExecuteNonQuery();
                        }

                        // 2. Удаляем его табели
                        using (var cmd2 = new SqlCommand("DELETE FROM Timesheets WHERE EmployeeID = @id", conn, tx))
                        {
                            cmd2.Parameters.AddWithValue("@id", empId);
                            cmd2.ExecuteNonQuery();
                        }

                        // 3. Теперь удаляем самого сотрудника
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
    }
}
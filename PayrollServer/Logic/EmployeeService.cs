using System.Data;
using System.Data.SqlClient;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    public class EmployeeService
    {
        public DataTable GetEmployees()
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                // Используем наше представление (View), которое мы создали в SQL
                string query = "SELECT * FROM View_EmployeeDetails";

                using (var adapter = new SqlDataAdapter(query, conn))
                {
                    DataTable dt = new DataTable();
                    adapter.Fill(dt);
                    return dt; // Возвращаем таблицу
                }
            }
        }
        public bool AddEmployee(string fullName, int positionId, int departmentId, decimal rate, string empType)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = @"INSERT INTO Employees (FullName, HireDate, ExperienceYears, PositionID, DepartmentID, Rate, EmploymentType) 
                                 VALUES (@name, GETDATE(), 0, @pos, @dept, @rate, @type)";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@name", fullName);
                    cmd.Parameters.AddWithValue("@pos", positionId);
                    cmd.Parameters.AddWithValue("@dept", departmentId);
                    cmd.Parameters.AddWithValue("@rate", rate);
                    cmd.Parameters.AddWithValue("@type", empType);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool UpdateEmployee(int empId, string fullName, int positionId, int departmentId, decimal rate, string empType)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = @"UPDATE Employees 
                                 SET FullName = @name, PositionID = @pos, DepartmentID = @dept, Rate = @rate, EmploymentType = @type 
                                 WHERE EmployeeID = @id";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", empId);
                    cmd.Parameters.AddWithValue("@name", fullName);
                    cmd.Parameters.AddWithValue("@pos", positionId);
                    cmd.Parameters.AddWithValue("@dept", departmentId);
                    cmd.Parameters.AddWithValue("@rate", rate);
                    cmd.Parameters.AddWithValue("@type", empType);
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

                // 1. Сначала удаляем все расчеты этого сотрудника
                using (var cmd1 = new SqlCommand("DELETE FROM PayrollCalculations WHERE EmployeeID = @id", conn))
                {
                    cmd1.Parameters.AddWithValue("@id", empId);
                    cmd1.ExecuteNonQuery();
                }

                // 2. Удаляем его табели
                using (var cmd2 = new SqlCommand("DELETE FROM Timesheets WHERE EmployeeID = @id", conn))
                {
                    cmd2.Parameters.AddWithValue("@id", empId);
                    cmd2.ExecuteNonQuery();
                }

                // 3. Теперь удаляем самого сотрудника
                string query = "DELETE FROM Employees WHERE EmployeeID = @id";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", empId);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }
    }
}
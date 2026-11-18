using System.Data;
using System.Data.SqlClient;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    public class UserService
    {
        public DataTable GetUsers()
        {
            DataTable dt = new DataTable();
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = @"SELECT u.UserID, u.Login AS [Логин], r.RoleName AS [Роль] 
                                 FROM Users u JOIN Roles r ON u.RoleID = r.RoleID";
                SqlDataAdapter adapter = new SqlDataAdapter(query, conn);
                adapter.Fill(dt);
            }
            return dt;
        }

        public bool AddUser(string login, string password, int roleId)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = "INSERT INTO Users (Login, PasswordHash, RoleID) VALUES (@l, @p, @r)";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@l", login);
                    cmd.Parameters.AddWithValue("@p", SecurityHelper.ComputeHash(password));
                    cmd.Parameters.AddWithValue("@r", roleId);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool DeleteUser(int userId)
        {
            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                // База данных САМА обнулит UserID в таблице Employees благодаря ON DELETE SET NULL
                string query = "DELETE FROM Users WHERE UserID = @id";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", userId);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }
    }
}
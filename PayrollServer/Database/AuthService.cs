using System.Data.SqlClient;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    public class AuthService
    {
        public string Authenticate(string login, string password)
        {
            // Хэшируем введенный пароль (без вывода в консоль!)
            string passwordHash = SecurityHelper.ComputeHash(password);

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = @"SELECT r.RoleName FROM Users u 
                                 JOIN Roles r ON u.RoleID = r.RoleID 
                                 WHERE u.Login = @login AND u.PasswordHash = @pass";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@login", login);
                    cmd.Parameters.AddWithValue("@pass", passwordHash);

                    var result = cmd.ExecuteScalar();
                    return result != null ? result.ToString() : null;
                }
            }
        }
    }
}
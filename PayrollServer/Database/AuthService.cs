using System;
using System.Data.SqlClient;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    public class AuthService
    {
        // Возвращает строку "Роль|UserID|Логин" при успешном входе, иначе null
        public string Authenticate(string login, string password)
        {
            string passwordHash = SecurityHelper.ComputeHash(password);

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();
                string query = @"SELECT u.UserID, u.Login, r.RoleName
                                 FROM Users u
                                 JOIN Roles r ON u.RoleID = r.RoleID
                                 WHERE u.Login = @login AND u.PasswordHash = @pass";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@login", login);
                    cmd.Parameters.AddWithValue("@pass", passwordHash);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int userId = Convert.ToInt32(reader["UserID"]);
                            string dbLogin = reader["Login"].ToString();
                            string roleName = reader["RoleName"].ToString();
                            return $"{roleName}|{userId}|{dbLogin}";
                        }
                    }
                }
            }

            return null;
        }
    }
}

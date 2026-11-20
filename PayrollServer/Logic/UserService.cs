using System;
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
                                 FROM Users u
                                 JOIN Roles r ON u.RoleID = r.RoleID
                                 ORDER BY u.UserID";
                using (var adapter = new SqlDataAdapter(query, conn))
                {
                    adapter.Fill(dt);
                }
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

        public bool DeleteUser(int userId, int currentUserId = -1)
        {
            // Запрет удаления собственной учётной записи администратора
            if (currentUserId > 0 && userId == currentUserId)
            {
                throw new InvalidOperationException(
                    "Вы не можете удалить собственную учётную запись, под которой выполнен вход.");
            }

            using (var conn = DbConnection.GetInstance().GetConnection())
            {
                conn.Open();

                // Проверяем, не является ли удаляемый пользователь единственным администратором
                string roleQuery = @"SELECT r.RoleName
                                     FROM Users u
                                     JOIN Roles r ON u.RoleID = r.RoleID
                                     WHERE u.UserID = @id";
                string targetRole = null;
                using (var roleCmd = new SqlCommand(roleQuery, conn))
                {
                    roleCmd.Parameters.AddWithValue("@id", userId);
                    object r = roleCmd.ExecuteScalar();
                    if (r != null && r != DBNull.Value) targetRole = r.ToString();
                }

                if (string.Equals(targetRole, "Администратор", StringComparison.OrdinalIgnoreCase))
                {
                    string countAdminsSql = @"SELECT COUNT(*)
                                              FROM Users u
                                              JOIN Roles r ON u.RoleID = r.RoleID
                                              WHERE r.RoleName = N'Администратор'";
                    using (var cntCmd = new SqlCommand(countAdminsSql, conn))
                    {
                        int adminCount = Convert.ToInt32(cntCmd.ExecuteScalar());
                        if (adminCount <= 1)
                        {
                            throw new InvalidOperationException(
                                "Нельзя удалить единственного администратора системы.");
                        }
                    }
                }

                // Снимаем привязку у сотрудников на случай, если в БД нет ON DELETE SET NULL
                using (var unlinkCmd = new SqlCommand("UPDATE Employees SET UserID = NULL WHERE UserID = @id", conn))
                {
                    unlinkCmd.Parameters.AddWithValue("@id", userId);
                    unlinkCmd.ExecuteNonQuery();
                }

                using (var cmd = new SqlCommand("DELETE FROM Users WHERE UserID = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", userId);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }
    }
}

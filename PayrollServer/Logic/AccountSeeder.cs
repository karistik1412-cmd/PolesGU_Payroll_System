using System;
using System.Data.SqlClient;
using PayrollServer.Database;

namespace PayrollServer.Logic
{
    // Первичное наполнение справочника ролей и создание учётной записи администратора.
    // Нужно, чтобы после развёртывания базы можно было войти в систему:
    // без хотя бы одной записи в Users вход невозможен (и добавить её некому —
    // экран «Пользователи» доступен только администратору).
    public static class AccountSeeder
    {
        // Учётная запись по умолчанию. Пароль сразу меняется в разделе «Пользователи».
        public const string DefaultAdminLogin = "admin";
        public const string DefaultAdminPassword = "admin123";

        public static void EnsureSeedAccounts()
        {
            try
            {
                using (SqlConnection conn = DbConnection.GetInstance().GetConnection())
                {
                    conn.Open();

                    EnsureRoles(conn);

                    int usersCount = CountUsers(conn);
                    if (usersCount > 0)
                    {
                        Console.WriteLine("[СЕРВЕР] Учётные записи найдены: " + usersCount);
                        return;
                    }

                    // Пользователей нет — создаём администратора, иначе войти некому
                    int adminRoleId = GetRoleId(conn, "Администратор");
                    if (adminRoleId <= 0) adminRoleId = 1;

                    string query = @"INSERT INTO Users (Login, PasswordHash, RoleID)
                                     VALUES (@login, @hash, @role)";
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@login", DefaultAdminLogin);
                        cmd.Parameters.AddWithValue("@hash", SecurityHelper.ComputeHash(DefaultAdminPassword));
                        cmd.Parameters.AddWithValue("@role", adminRoleId);
                        cmd.ExecuteNonQuery();
                    }

                    Console.WriteLine("[СЕРВЕР] Создана учётная запись администратора:");
                    Console.WriteLine("         логин:  " + DefaultAdminLogin);
                    Console.WriteLine("         пароль: " + DefaultAdminPassword);
                    Console.WriteLine("         Смените пароль в разделе «Пользователи».");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[СЕРВЕР] Не удалось подготовить учётные записи: " + ex.Message);
            }
        }

        // Роли приложения. RoleID 1..4 — те же значения, что использует экран «Пользователи».
        private static void EnsureRoles(SqlConnection conn)
        {
            string[,] roles =
            {
                { "1", "Администратор" },
                { "2", "Бухгалтер" },
                { "3", "Экономист" },
                { "4", "Сотрудник" }
            };

            for (int i = 0; i < roles.GetLength(0); i++)
            {
                int id = int.Parse(roles[i, 0]);
                string name = roles[i, 1];

                string exists = "SELECT COUNT(*) FROM Roles WHERE RoleID = @id OR RoleName = @name";
                using (SqlCommand check = new SqlCommand(exists, conn))
                {
                    check.Parameters.AddWithValue("@id", id);
                    check.Parameters.AddWithValue("@name", name);
                    if (Convert.ToInt32(check.ExecuteScalar()) > 0) continue;
                }

                string insert = "INSERT INTO Roles (RoleName) VALUES (@name)";
                using (SqlCommand cmd = new SqlCommand(insert, conn))
                {
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static int CountUsers(SqlConnection conn)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT COUNT(*) FROM Users", conn))
                return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private static int GetRoleId(SqlConnection conn, string roleName)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT RoleID FROM Roles WHERE RoleName = @name", conn))
            {
                cmd.Parameters.AddWithValue("@name", roleName);
                object result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? -1 : Convert.ToInt32(result);
            }
        }
    }
}

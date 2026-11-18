using System.Data.SqlClient; // Или Microsoft.Data.SqlClient, если ставили его
using System;

namespace PayrollServer.Database
{
    public class DbConnection
    {
        // Строка подключения. Обратите внимание на DESKTOP-E08I7VT\SQLEXPRESS (из вашего скриншота)
        // Если база называется иначе, поменяйте PolesGU_Payroll
        private static string connectionString = @"Server=localhost\SQLEXPRESS;Database=PolesGU_Payroll;Trusted_Connection=True;";

        // Паттерн Singleton (требование методички - 1 из 2 паттернов)
        private static DbConnection _instance;

        private DbConnection() { }

        public static DbConnection GetInstance()
        {
            if (_instance == null)
                _instance = new DbConnection();
            return _instance;
        }

        // Метод получения подключения
        public SqlConnection GetConnection()
        {
            return new SqlConnection(connectionString);
        }

        // Проверка подключения
        public bool TestConnection()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    conn.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка подключения к БД: " + ex.Message);
                return false;
            }
        }
    }
}
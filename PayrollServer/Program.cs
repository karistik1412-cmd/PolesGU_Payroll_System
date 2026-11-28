using System;
using PayrollServer.Database;
using PayrollServer.Logic;
using PayrollServer.Network;

namespace PayrollServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== СЕРВЕР СИСТЕМЫ НАЧИСЛЕНИЯ ЗАРПЛАТЫ ПОЛЕСГУ ===");

            if (DbConnection.GetInstance().TestConnection())
            {
                Console.WriteLine("[УСПЕХ] Подключение к БД установлено.");

                // Без учётной записи вход в систему невозможен, а создать её из
                // интерфейса некому (раздел «Пользователи» — только для администратора).
                AccountSeeder.EnsureSeedAccounts();
            }
            else
            {
                Console.WriteLine("[ОШИБКА] Нет подключения к БД. Проверьте строку подключения");
                Console.WriteLine("         в PayrollServer/Database/DbConnection.cs");
                Console.WriteLine("         и выполните Database/SeedAccounts.sql");
            }

            TcpServer server = new TcpServer();
            server.Start(8888); // Запускаем сервер
        }
    }
}
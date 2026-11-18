using System;
using PayrollServer.Database;
using PayrollServer.Network;

namespace PayrollServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== СЕРВЕР СИСТЕМЫ НАЧИСЛЕНИЯ ЗАРПЛАТЫ ПОЛЕСГУ ===");

            if (DbConnection.GetInstance().TestConnection())
                Console.WriteLine("[УСПЕХ] Подключение к БД установлено.");

            TcpServer server = new TcpServer();
            server.Start(8888); // Запускаем сервер
        }
    }
}
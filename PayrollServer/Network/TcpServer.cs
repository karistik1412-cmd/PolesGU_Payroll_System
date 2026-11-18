using System;
using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using PayrollServer.Logic;
using PayrollServer.Models;

namespace PayrollServer.Network
{
    public class TcpServer
    {
        private TcpListener _listener;
        private bool _isRunning;

        // Подключаем все наши сервисы
        private readonly AuthService _authService = new AuthService();
        private readonly EmployeeService _employeeService = new EmployeeService();
        private readonly PayrollService _payrollService = new PayrollService();
        private readonly TimesheetService _timesheetService = new TimesheetService();
        private readonly UserService _userService = new UserService();
        private readonly DashboardService _dashboardService = new DashboardService();

        public void Start(int port = 8888)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;
            Console.WriteLine($"[СЕРВЕР] Запущен на порту {port}. Ожидание клиентов...");

            while (_isRunning)
            {
                TcpClient client = _listener.AcceptTcpClient();
                Console.WriteLine("[СЕРВЕР] Подключился новый клиент!");
                Thread clientThread = new Thread(HandleClient);
                clientThread.IsBackground = true;
                clientThread.Start(client);
            }
        }

        private static void ParseMonthAndYear(string data, out int month, out int year)
        {
            month = DateTime.Now.Month;
            year = 2026;

            if (string.IsNullOrWhiteSpace(data)) return;

            string[] parts = data.Split('|');
            if (parts.Length >= 1) int.TryParse(parts[0], out month);
            if (parts.Length >= 2) int.TryParse(parts[1], out year);
        }

        private void HandleClient(object obj)
        {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();

            try
            {
                while (client.Connected)
                {
                    // 1. Читаем 4 байта, в которых зашифрована ДЛИНА сообщения
                    byte[] lengthBuffer = new byte[4];
                    int totalRead = 0;
                    while (totalRead < 4)
                    {
                        int read = stream.Read(lengthBuffer, totalRead, 4 - totalRead);
                        if (read == 0) return; // Клиент отключился
                        totalRead += read;
                    }
                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (messageLength <= 0 || messageLength > 20 * 1024 * 1024)
                        throw new InvalidOperationException("Некорректный размер сетевого пакета.");

                    // 2. Читаем само сообщение ровно той длины, которую узнали
                    byte[] buffer = new byte[messageLength];
                    totalRead = 0;
                    while (totalRead < messageLength)
                    {
                        int read = stream.Read(buffer, totalRead, messageLength - totalRead);
                        if (read == 0) return;
                        totalRead += read;
                    }

                    string jsonRequest = Encoding.UTF8.GetString(buffer);
                    var request = JsonConvert.DeserializeObject<NetworkMessage>(jsonRequest);
                    var response = new NetworkMessage();

                    try
                    {
                        Console.WriteLine($"[СЕРВЕР] Получен запрос: {request.Command}");
                        ProcessRequest(request, response);
                    }
                    catch (Exception cmdEx)
                    {
                        Console.WriteLine($"[ОШИБКА КОМАНДЫ {request?.Command}] {cmdEx.Message}");
                        response.IsSuccess = false;
                        response.ErrorMessage = cmdEx.Message;
                    }

                    // 3. Отправляем ответ с указанием длины
                    string jsonResponse = JsonConvert.SerializeObject(response);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(jsonResponse);
                    byte[] respLength = BitConverter.GetBytes(responseBytes.Length);

                    stream.Write(respLength, 0, 4);
                    stream.Write(responseBytes, 0, responseBytes.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ОШИБКА КЛИЕНТА] {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        private void ProcessRequest(NetworkMessage request, NetworkMessage response)
        {
            if (request == null || string.IsNullOrEmpty(request.Command))
            {
                response.IsSuccess = false;
                response.ErrorMessage = "Пустой запрос.";
                return;
            }

            if (request.Command == "LOGIN")
            {
                string[] parts = request.Data.Split(new[] { '|' }, 2);
                string role = _authService.Authenticate(parts[0], parts[1]);
                if (role != null) { response.IsSuccess = true; response.Data = role; }
                else { response.IsSuccess = false; response.ErrorMessage = "Неверный логин или пароль!"; }
            }
            else if (request.Command == "GET_EMPLOYEES")
            {
                var employeesTable = _employeeService.GetEmployees();
                response.Data = JsonConvert.SerializeObject(employeesTable);
                response.IsSuccess = true;
            }
            else if (request.Command == "GET_POSITIONS")
            {
                response.Data = JsonConvert.SerializeObject(_employeeService.GetPositions());
                response.IsSuccess = true;
            }
            else if (request.Command == "GET_DEPARTMENTS")
            {
                response.Data = JsonConvert.SerializeObject(_employeeService.GetDepartments());
                response.IsSuccess = true;
            }
            else if (request.Command == "ADD_EMPLOYEE")
            {
                string[] parts = request.Data.Split('|');
                decimal rate = Convert.ToDecimal(parts[3], CultureInfo.InvariantCulture);
                bool ok = _employeeService.AddEmployee(parts[0], int.Parse(parts[1]), int.Parse(parts[2]), rate, parts[4]);
                response.IsSuccess = ok;
                response.Data = ok ? "Сотрудник принят на работу!" : "Ошибка";
                if (!ok) response.ErrorMessage = "Не удалось добавить сотрудника.";
            }
            else if (request.Command == "DELETE_EMPLOYEE")
            {
                bool ok = _employeeService.DeleteEmployee(int.Parse(request.Data));
                response.IsSuccess = ok;
                response.Data = ok ? "Сотрудник уволен" : "Ошибка";
                if (!ok) response.ErrorMessage = "Сотрудник не найден.";
            }
            else if (request.Command == "UPDATE_EMPLOYEE")
            {
                // Формат Data: "ID|ФИО|ID_Должности|ID_Кафедры|Ставка|Тип"
                string[] parts = request.Data.Split('|');
                decimal rate = Convert.ToDecimal(parts[4], CultureInfo.InvariantCulture);
                bool ok = _employeeService.UpdateEmployee(int.Parse(parts[0]), parts[1], int.Parse(parts[2]), int.Parse(parts[3]), rate, parts[5]);
                response.IsSuccess = ok;
                response.Data = ok ? "Данные сотрудника обновлены!" : "Ошибка";
                if (!ok) response.ErrorMessage = "Не удалось обновить данные сотрудника.";
            }
            else if (request.Command == "GET_TIMESHEETS")
            {
                int month;
                int year;
                ParseMonthAndYear(request.Data, out month, out year);
                DataTable dt = _timesheetService.GetTimesheets(month, year);
                response.Data = JsonConvert.SerializeObject(dt);
                response.IsSuccess = true;
            }
            else if (request.Command == "IMPORT_TIMESHEETS")
            {
                var importReq = JsonConvert.DeserializeObject<TimesheetImportRequest>(request.Data);
                TimesheetImportResult importRes = _timesheetService.ImportTimesheets(
                    importReq.Month, importReq.Year, importReq.Entries);
                response.Data = JsonConvert.SerializeObject(importRes);
                response.IsSuccess = true;
            }
            else if (request.Command == "CLEAR_TIMESHEETS")
            {
                int month;
                int year;
                ParseMonthAndYear(request.Data, out month, out year);
                int removed = _timesheetService.ClearTimesheets(month, year);
                response.IsSuccess = true;
                response.Data = $"Табель очищен (удалено записей: {removed}).";
            }
            else if (request.Command == "CALCULATE_PAYROLL")
            {
                int month;
                int year;
                ParseMonthAndYear(request.Data, out month, out year);
                var payrollTable = _payrollService.CalculatePayroll(month, year);
                response.Data = JsonConvert.SerializeObject(payrollTable);
                response.IsSuccess = true;
            }
            else if (request.Command == "SAVE_PAYROLL")
            {
                // Поддерживаем формат "month|year|json" и старый "month|json"
                string[] firstSplit = request.Data.Split(new[] { '|' }, 2);
                int month = Convert.ToInt32(firstSplit[0]);
                int year = 2026;
                string tableJson = firstSplit[1];

                if (!tableJson.TrimStart().StartsWith("["))
                {
                    string[] secondSplit = tableJson.Split(new[] { '|' }, 2);
                    year = Convert.ToInt32(secondSplit[0]);
                    tableJson = secondSplit[1];
                }

                var tableToSave = JsonConvert.DeserializeObject<DataTable>(tableJson);
                _payrollService.SavePayroll(tableToSave, month, year);
                response.IsSuccess = true;
                response.Data = "Расчет сохранен!";
            }
            else if (request.Command == "GET_ANALYTICS")
            {
                int month;
                int year;
                ParseMonthAndYear(request.Data, out month, out year);
                var analyticsTable = _payrollService.GetAnalyticsData(month, year);
                response.Data = JsonConvert.SerializeObject(analyticsTable);
                response.IsSuccess = true;
            }
            else if (request.Command == "GET_ANALYTICS_FULL")
            {
                int month;
                int year;
                ParseMonthAndYear(request.Data, out month, out year);
                AnalyticsData fullAnalytics = _payrollService.GetFullAnalytics(month, year);
                response.Data = JsonConvert.SerializeObject(fullAnalytics);
                response.IsSuccess = true;
            }
            else if (request.Command == "GET_USERS")
            {
                response.Data = JsonConvert.SerializeObject(_userService.GetUsers());
                response.IsSuccess = true;
            }
            else if (request.Command == "ADD_USER")
            {
                string[] parts = request.Data.Split('|');
                bool ok = _userService.AddUser(parts[0], parts[1], int.Parse(parts[2]));
                response.IsSuccess = ok;
                response.Data = ok ? "Пользователь добавлен" : "Ошибка";
                if (!ok) response.ErrorMessage = "Не удалось добавить пользователя.";
            }
            else if (request.Command == "DELETE_USER")
            {
                bool ok = _userService.DeleteUser(int.Parse(request.Data));
                response.IsSuccess = ok;
                response.Data = ok ? "Пользователь удален" : "Ошибка";
                if (!ok) response.ErrorMessage = "Не удалось удалить пользователя.";
            }
            else if (request.Command == "GET_DASHBOARD")
            {
                response.Data = _dashboardService.GetStats();
                response.IsSuccess = true;
            }
            else
            {
                response.IsSuccess = false;
                response.ErrorMessage = $"Неизвестная команда: {request.Command}";
            }
        }
    }
}

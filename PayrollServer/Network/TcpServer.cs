using System;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using PayrollServer.Logic;

namespace PayrollServer.Network
{
    public class TcpServer
    {
        private TcpListener _listener;
        private bool _isRunning;

        // Подключаем все наши сервисы
        private AuthService _authService = new AuthService();
        private EmployeeService _employeeService = new EmployeeService();
        private PayrollService _payrollService = new PayrollService();
        private UserService _userService = new UserService();
        private DashboardService _dashboardService = new DashboardService();

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
                clientThread.Start(client);
            }
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

                    Console.WriteLine($"[СЕРВЕР] Получен запрос: {request.Command}");

                    // ОБРАБОТКА КОМАНД
                    if (request.Command == "LOGIN")
                    {
                        string[] parts = request.Data.Split('|');
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
                    // НОВАЯ КОМАНДА: ДОБАВЛЕНИЕ СОТРУДНИКА
                    else if (request.Command == "ADD_EMPLOYEE")
                    {
                        string[] parts = request.Data.Split('|');
                        // Читаем ставку тоже с InvariantCulture
                        decimal rate = Convert.ToDecimal(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                        bool ok = _employeeService.AddEmployee(parts[0], int.Parse(parts[1]), int.Parse(parts[2]), rate, parts[4]);
                        response.IsSuccess = ok;
                        response.Data = ok ? "Сотрудник принят на работу!" : "Ошибка";
                    }
                    else if (request.Command == "DELETE_EMPLOYEE")
                    {
                        bool ok = _employeeService.DeleteEmployee(int.Parse(request.Data));
                        response.IsSuccess = ok;
                        response.Data = ok ? "Сотрудник уволен" : "Ошибка";
                    }
                    else if (request.Command == "UPDATE_EMPLOYEE")
                    {
                        // Формат Data: "ID|ФИО|ID_Должности|ID_Кафедры|Ставка|Тип"
                        string[] parts = request.Data.Split('|');
                        decimal rate = Convert.ToDecimal(parts[4], System.Globalization.CultureInfo.InvariantCulture);
                        bool ok = _employeeService.UpdateEmployee(int.Parse(parts[0]), parts[1], int.Parse(parts[2]), int.Parse(parts[3]), rate, parts[5]);
                        response.IsSuccess = ok;
                        response.Data = ok ? "Данные сотрудника обновлены!" : "Ошибка";
                    }
                    else if (request.Command == "CALCULATE_PAYROLL")
                    {
                        int month = Convert.ToInt32(request.Data);
                        var payrollTable = _payrollService.CalculatePayroll(month);
                        response.Data = JsonConvert.SerializeObject(payrollTable);
                        response.IsSuccess = true;
                    }
                    else if (request.Command == "SAVE_PAYROLL")
                    {
                        string[] parts = request.Data.Split(new[] { '|' }, 2);
                        int month = Convert.ToInt32(parts[0]);
                        var tableToSave = JsonConvert.DeserializeObject<DataTable>(parts[1]);
                        _payrollService.SavePayroll(tableToSave, month);
                        response.IsSuccess = true;
                        response.Data = "Расчет сохранен!";
                    }
                    else if (request.Command == "GET_ANALYTICS")
                    {
                        int month = Convert.ToInt32(request.Data);
                        var analyticsTable = _payrollService.GetAnalyticsData(month);
                        response.Data = JsonConvert.SerializeObject(analyticsTable);
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
                    }
                    else if (request.Command == "DELETE_USER")
                    {
                        bool ok = _userService.DeleteUser(int.Parse(request.Data));
                        response.IsSuccess = ok;
                        response.Data = ok ? "Пользователь удален" : "Ошибка";
                    }
                    else if (request.Command == "GET_DASHBOARD")
                    {
                        response.Data = _dashboardService.GetStats();
                        response.IsSuccess = true;
                    }

                    // 3. ОТПРАВЛЯЕМ ОТВЕТ С УКАЗАНИЕМ ДЛИНЫ
                    string jsonResponse = JsonConvert.SerializeObject(response);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(jsonResponse);
                    byte[] respLength = BitConverter.GetBytes(responseBytes.Length);

                    stream.Write(respLength, 0, 4); // Сначала отправляем длину
                    stream.Write(responseBytes, 0, responseBytes.Length); // Потом сам JSON
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
    }
}
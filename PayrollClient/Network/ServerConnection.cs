using System;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;

namespace PayrollClient.Network
{
    // ВОТ ЗДЕСЬ МЫ ВОЗВРАЩАЕМ СВОЙСТВА КЛАССА
    public class NetworkMessage
    {
        public string Command { get; set; }
        public string Data { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class ServerConnection
    {
        private string _ip = "127.0.0.1";
        private int _port = 8888;

        public NetworkMessage SendRequest(NetworkMessage request)
        {
            using (TcpClient client = new TcpClient())
            {
                // Таймаут подключения (3 секунды)
                client.ReceiveTimeout = 3000;
                client.SendTimeout = 3000;

                client.Connect(_ip, _port);
                using (NetworkStream stream = client.GetStream())
                {
                    string json = JsonConvert.SerializeObject(request);
                    byte[] data = Encoding.UTF8.GetBytes(json);

                    // Отправляем размер пакета, а затем сам пакет (Защита от разрыва больших таблиц)
                    byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                    stream.Write(lengthBytes, 0, 4);
                    stream.Write(data, 0, data.Length);

                    // Читаем размер ответа
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = stream.Read(lengthBuffer, 0, 4);
                    if (bytesRead < 4) throw new Exception("Сервер разорвал соединение.");

                    int responseLength = BitConverter.ToInt32(lengthBuffer, 0);
                    byte[] responseBytes = new byte[responseLength];

                    int totalRead = 0;
                    while (totalRead < responseLength)
                    {
                        int read = stream.Read(responseBytes, totalRead, responseLength - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }

                    string jsonResponse = Encoding.UTF8.GetString(responseBytes, 0, totalRead);
                    return JsonConvert.DeserializeObject<NetworkMessage>(jsonResponse);
                }
            }
        }
    }
}
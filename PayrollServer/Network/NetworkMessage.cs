namespace PayrollServer.Network
{
    public class NetworkMessage
    {
        public string Command { get; set; } // Например: "LOGIN"
        public string Data { get; set; }    // Логин и пароль через "|"
        public bool IsSuccess { get; set; } // Успешно ли выполнено
        public string ErrorMessage { get; set; } // Текст ошибки, если есть
    }
}
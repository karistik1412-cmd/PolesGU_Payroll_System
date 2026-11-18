using System;

namespace PayrollServer.Models
{
    // Абстрактный класс (требование методички)
    public abstract class Person
    {
        public int Id { get; set; }
        public string FullName { get; set; }
    }

    // Наследование
    public class User : Person
    {
        public string Login { get; set; }
        public string PasswordHash { get; set; }
        public int RoleId { get; set; }

        // Переопределение метода
        public override string ToString()
        {
            return $"{FullName} (Логин: {Login})";
        }
    }
}
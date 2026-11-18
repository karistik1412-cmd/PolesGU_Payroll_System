namespace PayrollServer.Models
{
    public class Employee : Person
    {
        public int ExperienceYears { get; set; }
        public int DepartmentId { get; set; }
        public int PositionId { get; set; }

        // Перегрузка метода (требование методички)
        public string GetInfo()
        {
            return $"Сотрудник: {FullName}";
        }

        public string GetInfo(bool includeExperience)
        {
            if (includeExperience)
                return $"Сотрудник: {FullName}, Стаж: {ExperienceYears} лет";
            return GetInfo();
        }
    }
}
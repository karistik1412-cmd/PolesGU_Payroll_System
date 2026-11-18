using System.Collections.Generic;

namespace PayrollServer.Models
{
    // Модель строки табеля учета рабочего времени
    public class TimesheetEntry
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; }
        public decimal? Rate { get; set; }
        public int DaysWorked { get; set; }
        public int NormDays { get; set; }
        public decimal HoursWorked { get; set; }
        public decimal NormHours { get; set; }
        public bool HasHours { get; set; }
    }

    // Запрос на импорт/сохранение табеля за выбранный период
    public class TimesheetImportRequest
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public List<TimesheetEntry> Entries { get; set; }
    }

    // Результат импорта табеля
    public class TimesheetImportResult
    {
        public int SavedCount { get; set; }
        public int TotalCount { get; set; }
        public List<string> UnmatchedNames { get; set; } = new List<string>();
        public string Message { get; set; }
    }
}

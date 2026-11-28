using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace PayrollClient.Services
{
    public class TimesheetEntryDto
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; }
        public decimal? Rate { get; set; }
        public int DaysWorked { get; set; }
        public int NormDays { get; set; }
        public decimal HoursWorked { get; set; }
        public decimal NormHours { get; set; }
        public bool HasHours { get; set; }
        public int SickDays { get; set; }
        public int VacationDays { get; set; }
    }

    public class TimesheetImportRequestDto
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public List<TimesheetEntryDto> Entries { get; set; }
    }

    public class TimesheetImportResultDto
    {
        public int SavedCount { get; set; }
        public int TotalCount { get; set; }
        public List<string> UnmatchedNames { get; set; }
        public string Message { get; set; }
    }

    public class ExcelParseResult
    {
        public List<TimesheetEntryDto> Entries { get; set; } = new List<TimesheetEntryDto>();
        public int? DetectedMonth { get; set; }
        public int? DetectedYear { get; set; }
        public string SheetName { get; set; }
        public bool IsIndividualPlan { get; set; }
        public string InfoMessage { get; set; }
    }

    // Модель расширенной аналитики для вкладки «Графики»
    public class AnalyticsDataDto
    {
        public DataTable DepartmentPayroll { get; set; }
        public int PaidEmployeesCount { get; set; }
        public decimal TotalBaseSalary { get; set; }
        public decimal TotalBonus { get; set; }
        public decimal TotalSickVacationPay { get; set; }
        public decimal TotalTaxDeduction { get; set; }
        public decimal TotalTax { get; set; }
        public decimal TotalFszen { get; set; }
        public decimal TotalUnion { get; set; }
        public decimal TotalNetSalary { get; set; }
        public DataTable MonthlyTrend { get; set; }
        public DataTable EmployeeHours { get; set; }
    }

    // Сервис импорта табеля учета рабочего времени из внешнего Excel-файла
    public static class ExcelTimesheetService
    {
        public static readonly string[] MonthNames =
        {
            "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
            "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
        };

        private static readonly string[] MonthRoots =
        {
            "январ", "феврал", "март", "апрел", "май", "июн",
            "июл", "август", "сентябр", "октябр", "ноябр", "декабр"
        };

        // Чтение Excel-файла табеля (поддерживает полный табель 1..31, сводный табель и Карточку ИП преподавателя)
        public static ExcelParseResult ParseTimesheetFile(string filePath, int targetMonth, int targetYear)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Файл табеля не найден.", filePath);

            // FileShare.ReadWrite позволяет импортировать файл даже если он открыт в Excel
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var workbook = new XLWorkbook(stream))
            {
                // 1. Проверяем, не открыт ли Индивидуальный план преподавателя (лист "Карточка")
                IXLWorksheet cardSheet;
                if (workbook.TryGetWorksheet("Карточка", out cardSheet))
                {
                    return ParseIndividualPlanCard(workbook, cardSheet, filePath, targetMonth, targetYear);
                }

                // 2. Ищем лист табеля: сначала по названию выбранного месяца, иначе первый подходящий лист
                IXLWorksheet selectedSheet = FindSheetForMonth(workbook, targetMonth);
                if (selectedSheet != null)
                {
                    ExcelParseResult monthResult = TryParseTimesheetWorksheet(selectedSheet);
                    if (monthResult != null && monthResult.Entries.Count > 0)
                        return monthResult;
                }

                foreach (IXLWorksheet ws in workbook.Worksheets)
                {
                    ExcelParseResult parsed = TryParseTimesheetWorksheet(ws);
                    if (parsed != null && parsed.Entries.Count > 0)
                        return parsed;
                }

                throw new InvalidOperationException(
                    "Не удалось распознать структуру табеля в выбранном Excel-файле.\n" +
                    "Убедитесь, что в таблице есть столбец «ФИО» и столбцы дней (1..31) либо «Отработано дней» / «Отработано часов».");
            }
        }

        private static IXLWorksheet FindSheetForMonth(IXLWorkbook workbook, int month)
        {
            if (month < 1 || month > 12) return null;
            string root = MonthRoots[month - 1];

            foreach (IXLWorksheet ws in workbook.Worksheets)
            {
                string lower = ws.Name.Trim().ToLowerInvariant();
                if (lower.Contains(root) || (month == 5 && (lower.Contains("май") || lower.Contains("мая"))))
                    return ws;
            }
            return null;
        }

        private static ExcelParseResult TryParseTimesheetWorksheet(IXLWorksheet ws)
        {
            IXLRow lastRowObj = ws.LastRowUsed();
            IXLColumn lastColObj = ws.LastColumnUsed();
            if (lastRowObj == null || lastColObj == null) return null;

            int lastRow = lastRowObj.RowNumber();
            int lastCol = Math.Min(lastColObj.ColumnNumber(), 60);

            int headerRow = -1;
            int idCol = -1;
            int nameCol = -1;
            int rateCol = -1;
            int factDaysCol = -1;
            int normDaysCol = -1;
            int factHoursCol = -1;
            int normHoursCol = -1;
            int sickDaysCol = -1;
            int vacDaysCol = -1;
            var dayCols = new Dictionary<int, int>();

            // Ищем строку заголовков в первых 20 строках
            int scanLimit = Math.Min(lastRow, 20);
            for (int r = 1; r <= scanLimit; r++)
            {
                for (int c = 1; c <= lastCol; c++)
                {
                    string text = NormalizeHeader(ReadText(ws.Cell(r, c)));
                    if (IsNameHeader(text))
                    {
                        headerRow = r;
                        break;
                    }
                }
                if (headerRow > 0) break;
            }

            if (headerRow < 0) return null;

            // Сначала определяем столбец ФИО в найденной строке заголовков
            for (int c = 1; c <= lastCol; c++)
            {
                string topText = NormalizeHeader(ReadText(ws.Cell(headerRow, c)));
                if (nameCol < 0 && IsNameHeader(topText))
                {
                    nameCol = c;
                    break;
                }
            }

            if (nameCol < 0) return null;

            // Двухуровневая шапка есть только если под ячейкой «ФИО» пусто (объединенная ячейка заголовка)
            bool hasSubHeader = (headerRow + 1 <= lastRow)
                                && string.IsNullOrWhiteSpace(ReadText(ws.Cell(headerRow + 1, nameCol)));

            int dataStartRow = headerRow + 1;

            for (int c = 1; c <= lastCol; c++)
            {
                string topText = NormalizeHeader(ReadText(ws.Cell(headerRow, c)));
                string subText = hasSubHeader ? NormalizeHeader(ReadText(ws.Cell(headerRow + 1, c))) : string.Empty;

                MapHeaderColumn(topText, c, ref idCol, ref nameCol, ref rateCol,
                    ref factDaysCol, ref normDaysCol, ref factHoursCol, ref normHoursCol,
                    ref sickDaysCol, ref vacDaysCol, dayCols);

                if (hasSubHeader && !string.IsNullOrEmpty(subText))
                {
                    int dayNum;
                    if (int.TryParse(subText, out dayNum) && dayNum >= 1 && dayNum <= 31 && !dayCols.ContainsKey(dayNum))
                    {
                        dayCols[dayNum] = c;
                        dataStartRow = headerRow + 2;
                    }
                    else
                    {
                        int prevDays = factDaysCol;
                        int prevNormDays = normDaysCol;
                        int prevHours = factHoursCol;
                        int prevNormHours = normHoursCol;
                        int prevSick = sickDaysCol;
                        int prevVac = vacDaysCol;
                        MapHeaderColumn(subText, c, ref idCol, ref nameCol, ref rateCol,
                            ref factDaysCol, ref normDaysCol, ref factHoursCol, ref normHoursCol,
                            ref sickDaysCol, ref vacDaysCol, dayCols);
                        if (factDaysCol != prevDays || normDaysCol != prevNormDays ||
                            factHoursCol != prevHours || normHoursCol != prevNormHours ||
                            sickDaysCol != prevSick || vacDaysCol != prevVac)
                        {
                            dataStartRow = headerRow + 2;
                        }
                    }
                }
            }

            // Проверяем, есть ли вообще столбцы с днями или часами
            if (nameCol < 0 || (dayCols.Count == 0 && factDaysCol < 0 && factHoursCol < 0))
                return null;

            var result = new ExcelParseResult
            {
                SheetName = ws.Name
            };

            // Пытаемся определить месяц и год из шапки и названия листа
            DetectPeriodFromWorksheet(ws, headerRow, lastCol, result);

            // Читаем строки сотрудников
            for (int r = dataStartRow; r <= lastRow; r++)
            {
                string rawName = ReadText(ws.Cell(r, nameCol));
                if (string.IsNullOrWhiteSpace(rawName)) continue;

                string lowerName = rawName.Trim().ToLowerInvariant();

                // Пропускаем техническую строку нумерации столбцов (1, 2, 3...)
                int dummyIndex;
                if (int.TryParse(lowerName, out dummyIndex)) continue;

                // Останавливаемся на итоговой строке или подписях
                if (lowerName.StartsWith("итого") || lowerName.StartsWith("всего") ||
                    lowerName.StartsWith("условные обозначения") || lowerName.StartsWith("заведующий") ||
                    lowerName.StartsWith("ответственный") || lowerName.StartsWith("руководитель") ||
                    lowerName.StartsWith("бухгалтер") || lowerName.StartsWith("декан"))
                {
                    break;
                }

                var entry = new TimesheetEntryDto
                {
                    FullName = rawName.Trim()
                };

                if (idCol > 0)
                {
                    decimal idVal;
                    if (ReadDecimal(ws.Cell(r, idCol), out idVal) && idVal > 0m)
                        entry.EmployeeId = (int)Math.Round(idVal);
                }

                if (rateCol > 0)
                {
                    decimal rateVal;
                    if (ReadDecimal(ws.Cell(r, rateCol), out rateVal) && rateVal > 0m && rateVal <= 3.0m)
                        entry.Rate = Math.Round(rateVal, 2);
                }

                // Считываем дни, часы, больничные (Б) и отпуска (О) по сетке чисел месяца (1..31)
                bool usedDailyGrid = false;
                if (dayCols.Count >= 10)
                {
                    int countedDays = 0;
                    int countedSick = 0;
                    int countedVacation = 0;
                    decimal summedHours = 0m;
                    bool hasAnyDayContent = false;

                    foreach (KeyValuePair<int, int> kv in dayCols)
                    {
                        IXLCell dCell = ws.Cell(r, kv.Value);
                        decimal h;
                        if (ReadDecimal(dCell, out h))
                        {
                            hasAnyDayContent = true;
                            if (h > 0m)
                            {
                                countedDays++;
                                summedHours += h;
                            }
                        }
                        else
                        {
                            string mark = ReadText(dCell);
                            if (!string.IsNullOrWhiteSpace(mark))
                            {
                                hasAnyDayContent = true;
                                string normMark = mark.Trim().ToUpperInvariant();
                                // Явка (Я), больничный (Б / БЛ / Т), оплачиваемый отпуск (О / ОТ / ОТПУСК)
                                if (normMark == "Я")
                                    countedDays++;
                                else if (normMark == "Б" || normMark == "БЛ" || normMark == "Т")
                                    countedSick++;
                                else if (normMark == "О" || normMark == "ОТ" || normMark == "ОТПУСК")
                                    countedVacation++;
                            }
                        }
                    }

                    if (hasAnyDayContent)
                    {
                        entry.DaysWorked = countedDays;
                        entry.SickDays = countedSick;
                        entry.VacationDays = countedVacation;
                        if (summedHours > 0m)
                        {
                            entry.HoursWorked = Math.Round(summedHours, 2);
                            entry.HasHours = true;
                        }
                        else if (countedDays == 0)
                        {
                            entry.HoursWorked = 0m;
                            entry.HasHours = true;
                        }
                        usedDailyGrid = true;
                    }
                }

                // Если отдельных отметок Б/О в сетке нет, но есть итоговые столбцы «Больничный» / «Отпуск» — считываем их
                if (entry.SickDays == 0 && sickDaysCol > 0)
                {
                    decimal sVal;
                    if (ReadDecimal(ws.Cell(r, sickDaysCol), out sVal) && sVal > 0m)
                        entry.SickDays = (int)Math.Round(sVal, MidpointRounding.AwayFromZero);
                }

                if (entry.VacationDays == 0 && vacDaysCol > 0)
                {
                    decimal vVal;
                    if (ReadDecimal(ws.Cell(r, vacDaysCol), out vVal) && vVal > 0m)
                        entry.VacationDays = (int)Math.Round(vVal, MidpointRounding.AwayFromZero);
                }

                // Если сетки по дням нет или она пустая — берем итоговые столбцы
                if (!usedDailyGrid)
                {
                    if (factDaysCol > 0)
                    {
                        decimal dVal;
                        if (ReadDecimal(ws.Cell(r, factDaysCol), out dVal) && dVal >= 0m)
                            entry.DaysWorked = (int)Math.Round(dVal, MidpointRounding.AwayFromZero);
                    }

                    if (factHoursCol > 0)
                    {
                        decimal hVal;
                        if (ReadDecimal(ws.Cell(r, factHoursCol), out hVal) && hVal >= 0m)
                        {
                            entry.HoursWorked = Math.Round(hVal, 2);
                            entry.HasHours = true;
                        }
                    }
                }
                else if (!entry.HasHours && factHoursCol > 0)
                {
                    decimal hVal;
                    if (ReadDecimal(ws.Cell(r, factHoursCol), out hVal) && hVal >= 0m)
                    {
                        entry.HoursWorked = Math.Round(hVal, 2);
                        entry.HasHours = true;
                    }
                }

                if (normDaysCol > 0)
                {
                    decimal ndVal;
                    if (ReadDecimal(ws.Cell(r, normDaysCol), out ndVal) && ndVal > 0m)
                        entry.NormDays = (int)Math.Round(ndVal, MidpointRounding.AwayFromZero);
                }

                if (normHoursCol > 0)
                {
                    decimal nhVal;
                    if (ReadDecimal(ws.Cell(r, normHoursCol), out nhVal) && nhVal > 0m)
                        entry.NormHours = Math.Round(nhVal, 2);
                }

                result.Entries.Add(entry);
            }

            return result;
        }

        // Поддержка импорта часов и дней из листа «Карточка» Индивидуального плана преподавателя ПолесГУ
        private static ExcelParseResult ParseIndividualPlanCard(
            IXLWorkbook workbook,
            IXLWorksheet cardSheet,
            string filePath,
            int targetMonth,
            int targetYear)
        {
            var result = new ExcelParseResult
            {
                SheetName = "Карточка",
                IsIndividualPlan = true,
                DetectedMonth = targetMonth,
                DetectedYear = targetYear
            };

            string teacherName = ExtractTeacherNameFromPlan(workbook, cardSheet, filePath);
            decimal? rate = ExtractRateFromFileName(filePath);

            IXLRow lastRowObj = cardSheet.LastRowUsed();
            int lastRow = lastRowObj != null ? lastRowObj.RowNumber() : 0;

            var matchedDates = new HashSet<DateTime>();
            decimal totalHours = 0m;
            var availablePeriods = new Dictionary<string, decimal>();

            for (int r = 10; r <= lastRow; r++)
            {
                DateTime lessonDate;
                if (!ReadDate(cardSheet.Cell(r, 2), out lessonDate))
                    continue;

                // Столбец Z (26) — «Итого» часов по занятию; если пусто, суммируем G..Y (7..25)
                decimal rowHours = 0m;
                decimal zVal;
                if (ReadDecimal(cardSheet.Cell(r, 26), out zVal) && zVal > 0m)
                {
                    rowHours = zVal;
                }
                else
                {
                    for (int c = 7; c <= 25; c++)
                    {
                        decimal part;
                        if (ReadDecimal(cardSheet.Cell(r, c), out part) && part > 0m)
                            rowHours += part;
                    }
                }

                if (rowHours <= 0m) continue;

                string periodKey = $"{MonthNames[lessonDate.Month - 1]} {lessonDate.Year}";
                if (!availablePeriods.ContainsKey(periodKey))
                    availablePeriods[periodKey] = 0m;
                availablePeriods[periodKey] += rowHours;

                if (lessonDate.Month == targetMonth && lessonDate.Year == targetYear)
                {
                    matchedDates.Add(lessonDate.Date);
                    totalHours += rowHours;
                }
            }

            if (matchedDates.Count == 0)
            {
                string periods = availablePeriods.Count > 0
                    ? string.Join(", ", availablePeriods.Select(p => $"{p.Key} ({p.Value:0.#} ч.)"))
                    : "нет заполненных занятий";

                throw new InvalidOperationException(
                    $"В карточке индивидуального плана нет занятий за {MonthNames[targetMonth - 1]} {targetYear} г.\n" +
                    $"Доступные периоды в файле: {periods}.");
            }

            result.Entries.Add(new TimesheetEntryDto
            {
                FullName = teacherName,
                Rate = rate,
                DaysWorked = matchedDates.Count,
                NormDays = CountWorkingDays(targetYear, targetMonth),
                HoursWorked = Math.Round(totalHours, 2),
                HasHours = true
            });

            result.InfoMessage =
                $"Импортирована карточка учебной работы ({teacherName}): {matchedDates.Count} дн., {totalHours:0.#} ч.";

            return result;
        }

        private static string ExtractTeacherNameFromPlan(IXLWorkbook workbook, IXLWorksheet cardSheet, string filePath)
        {
            // 1. Пробуем строку 4 листа «Карточка»: "преподавателя ___Харитонюк Евгении Николаевны___"
            string row4 = ReadText(cardSheet.Cell(4, 1));
            if (!string.IsNullOrWhiteSpace(row4))
            {
                string cleaned = row4.Replace("преподавателя", "").Replace("_", " ").Trim();
                if (!string.IsNullOrWhiteSpace(cleaned))
                    return cleaned;
            }

            // 2. Пробуем «Титульный лист» (строка 15 — ФИО преподавателя)
            IXLWorksheet titleSheet;
            if (workbook.TryGetWorksheet("Титульный лист", out titleSheet))
            {
                for (int r = 14; r <= 16; r++)
                {
                    for (int c = 1; c <= 5; c++)
                    {
                        string val = ReadText(titleSheet.Cell(r, c));
                        if (!string.IsNullOrWhiteSpace(val) && val.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                            return val.Trim();
                    }
                }
            }

            // 3. Пробуем подпись преподавателя внизу листа «Карточка»
            IXLRow lastRowObj = cardSheet.LastRowUsed();
            int lastRow = lastRowObj != null ? lastRowObj.RowNumber() : 0;
            for (int r = Math.Max(10, lastRow - 5); r <= lastRow; r++)
            {
                string line = ReadText(cardSheet.Cell(r, 1));
                if (line.IndexOf("Преподаватель", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string after = line.Substring(line.IndexOf("Преподаватель", StringComparison.OrdinalIgnoreCase) + "Преподаватель".Length)
                        .Replace("_", " ").Trim();
                    if (!string.IsNullOrWhiteSpace(after))
                        return after;
                }
            }

            return Path.GetFileNameWithoutExtension(filePath);
        }

        private static decimal? ExtractRateFromFileName(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
            string lower = name.ToLowerInvariant().Replace(',', '.');

            if (lower.Contains("0.25")) return 0.25m;
            if (lower.Contains("0.75")) return 0.75m;
            if (lower.Contains("1.5")) return 1.5m;
            if (lower.Contains("0.5")) return 0.5m;
            if (lower.Contains("1_ставка") || lower.Contains("1 ставка") || lower.Contains("1.0")) return 1.0m;

            return null;
        }

        private static void DetectPeriodFromWorksheet(IXLWorksheet ws, int headerRow, int lastCol, ExcelParseResult result)
        {
            // Проверяем название листа
            int? sheetMonth = TryParseMonthName(ws.Name);
            if (sheetMonth.HasValue) result.DetectedMonth = sheetMonth;

            int? sheetYear = TryExtractYear(ws.Name);
            if (sheetYear.HasValue) result.DetectedYear = sheetYear;

            // Проверяем ячейки шапки над таблицей
            for (int r = 1; r < headerRow; r++)
            {
                for (int c = 1; c <= Math.Min(lastCol, 15); c++)
                {
                    string text = ReadText(ws.Cell(r, c));
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    string lower = text.Trim().ToLowerInvariant();
                    if (lower.StartsWith("месяц"))
                    {
                        string next = c + 1 <= lastCol ? ReadText(ws.Cell(r, c + 1)) : string.Empty;
                        int? m = TryParseMonthName(text) ?? TryParseMonthName(next);
                        if (m.HasValue) result.DetectedMonth = m;
                    }
                    else if (lower.StartsWith("год"))
                    {
                        string next = c + 1 <= lastCol ? ReadText(ws.Cell(r, c + 1)) : string.Empty;
                        int? y = TryExtractYear(text) ?? TryExtractYear(next);
                        if (y.HasValue) result.DetectedYear = y;
                    }
                    else
                    {
                        if (!result.DetectedMonth.HasValue)
                        {
                            int? m = TryParseMonthName(text);
                            if (m.HasValue) result.DetectedMonth = m;
                        }
                        if (!result.DetectedYear.HasValue)
                        {
                            int? y = TryExtractYear(text);
                            if (y.HasValue) result.DetectedYear = y;
                        }
                    }
                }
            }
        }

        private static int? TryParseMonthName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string[] words = text.ToLowerInvariant().Split(
                new[] { ' ', '\t', '.', ',', ':', ';', '-', '/', '_', '(', ')', '«', '»', '"' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string w in words)
            {
                for (int i = 0; i < MonthRoots.Length; i++)
                {
                    if (w.StartsWith(MonthRoots[i]))
                        return i + 1;
                }
                if (w == "мая" || w == "мае")
                    return 5;
            }

            return null;
        }

        private static int? TryExtractYear(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string[] tokens = text.Split(new[] { ' ', '.', ',', ':', ';', '-', '/', '_', 'г', '(', ')' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                int y;
                if (token.Length == 4 && int.TryParse(token, out y) && y >= 2020 && y <= 2035)
                    return y;
            }
            return null;
        }

        private static bool IsNameHeader(string h)
        {
            if (string.IsNullOrEmpty(h)) return false;
            return h == "фио" || h.StartsWith("фио ") || h.Contains("ф.и.о") ||
                   h.Contains("фамилия") || h == "сотрудник" || h == "преподаватель" ||
                   h == "fullname";
        }

        private static void MapHeaderColumn(
            string h,
            int col,
            ref int idCol,
            ref int nameCol,
            ref int rateCol,
            ref int factDaysCol,
            ref int normDaysCol,
            ref int factHoursCol,
            ref int normHoursCol,
            ref int sickDaysCol,
            ref int vacDaysCol,
            Dictionary<int, int> dayCols)
        {
            if (string.IsNullOrEmpty(h)) return;

            int dayNum;
            if (int.TryParse(h, out dayNum) && dayNum >= 1 && dayNum <= 31)
            {
                if (!dayCols.ContainsKey(dayNum))
                    dayCols[dayNum] = col;
                return;
            }

            if (idCol < 0 && (h == "employeeid" || h == "id" || h.StartsWith("таб.") || h.StartsWith("таб ") || h.Contains("табельный")))
            {
                idCol = col;
            }
            else if (nameCol < 0 && IsNameHeader(h))
            {
                nameCol = col;
            }
            else if (rateCol < 0 && (h == "ставка" || h.Contains("доля ставки") || h == "rate"))
            {
                rateCol = col;
            }
            else if (sickDaysCol < 0 && (h.Contains("больнич") || h.Contains("нетрудосп") || h == "б" || h == "дни б" || h == "sickdays"))
            {
                sickDaysCol = col;
            }
            else if (vacDaysCol < 0 && (h.Contains("отпуск") || h == "о" || h == "дни о" || h == "vacationdays"))
            {
                vacDaysCol = col;
            }
            else if (normDaysCol < 0 && ((h.Contains("норм") && h.Contains("дн")) || h == "normdays"))
            {
                normDaysCol = col;
            }
            else if (normHoursCol < 0 && ((h.Contains("норм") && (h.Contains("час") || h.EndsWith("ч.") || h.EndsWith(" ч"))) || h == "normhours"))
            {
                normHoursCol = col;
            }
            else if (factDaysCol < 0 && (
                (h.Contains("отраб") && h.Contains("дн")) ||
                (h.Contains("факт") && h.Contains("дн")) ||
                h.Contains("явок") || h == "дни" || h == "дней" ||
                h == "daysworked" || h == "factdays"))
            {
                factDaysCol = col;
            }
            else if (factHoursCol < 0 && (
                (h.Contains("отраб") && (h.Contains("час") || h.EndsWith("ч.") || h.EndsWith(" ч"))) ||
                (h.Contains("факт") && (h.Contains("час") || h.EndsWith("ч."))) ||
                (h.Contains("всего") && h.Contains("час")) ||
                (h.Contains("итого") && h.Contains("час")) ||
                h == "часы" || h == "часов" || h == "hoursworked"))
            {
                factHoursCol = col;
            }
        }

        private static string NormalizeHeader(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string cleaned = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\u00A0', ' ').Trim().ToLowerInvariant();
            string[] parts = cleaned.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        private static XLCellValue GetSafeCellValue(IXLCell cell)
        {
            if (cell == null) return Blank.Value;
            if (cell.HasFormula)
            {
                try
                {
                    XLCellValue evaluated = cell.Value;
                    if (!evaluated.IsError && !evaluated.IsBlank)
                        return evaluated;
                }
                catch
                {
                    // Если формула ссылается на внешнюю книгу, используем сохраненное значение
                }
                return cell.CachedValue;
            }
            return cell.Value;
        }

        private static string ReadText(IXLCell cell)
        {
            XLCellValue val = GetSafeCellValue(cell);
            if (val.IsBlank || val.IsError) return string.Empty;
            return val.ToString(CultureInfo.CurrentCulture).Replace('\u00A0', ' ').Trim();
        }

        private static bool ReadDecimal(IXLCell cell, out decimal result)
        {
            result = 0m;
            XLCellValue val = GetSafeCellValue(cell);
            if (val.IsBlank || val.IsError) return false;

            if (val.IsNumber)
            {
                result = Convert.ToDecimal(val.GetUnifiedNumber());
                return true;
            }

            if (val.IsText)
            {
                string s = val.ToString(CultureInfo.InvariantCulture)
                    .Replace(" ", "")
                    .Replace("\u00A0", "")
                    .Replace(',', '.')
                    .Trim();
                if (string.IsNullOrEmpty(s)) return false;
                return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            }

            return false;
        }

        private static bool ReadDate(IXLCell cell, out DateTime date)
        {
            date = DateTime.MinValue;
            XLCellValue val = GetSafeCellValue(cell);
            if (val.IsBlank || val.IsError) return false;

            if (val.IsDateTime)
            {
                date = val.GetDateTime();
                return true;
            }

            if (val.IsNumber)
            {
                double oa = val.GetUnifiedNumber();
                if (oa > 30000 && oa < 70000)
                {
                    date = DateTime.FromOADate(oa);
                    return true;
                }
            }

            if (val.IsText)
            {
                string s = val.ToString(CultureInfo.CurrentCulture).Trim();
                if (DateTime.TryParse(s, new CultureInfo("ru-RU"), DateTimeStyles.None, out date))
                    return true;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    return true;
            }

            return false;
        }

        private static int CountWorkingDays(int year, int month)
        {
            int total = DateTime.DaysInMonth(year, month);
            int count = 0;
            for (int d = 1; d <= total; d++)
            {
                DayOfWeek dow = new DateTime(year, month, d).DayOfWeek;
                if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday) count++;
            }
            return count;
        }
    }
}

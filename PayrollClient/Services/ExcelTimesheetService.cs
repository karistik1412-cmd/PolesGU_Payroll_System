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

    // Сервис импорта табеля из Excel и генерации «табеля-болванки»
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

        // Генерация готового Excel «табеля-болванки» со случайными отработанными днями и часами
        public static void GenerateDummyTimesheet(string filePath, DataTable employees, int month, int year)
        {
            if (month < 1 || month > 12) month = DateTime.Now.Month;
            if (year < 2000 || year > 2100) year = 2026;

            int daysInMonth = DateTime.DaysInMonth(year, month);
            int normDays = CountWorkingDays(year, month);
            string monthName = MonthNames[month - 1];
            var rng = new Random();

            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet ws = workbook.Worksheets.Add(monthName);
                ws.Style.Font.FontName = "Segoe UI";
                ws.Style.Font.FontSize = 9;

                int firstDayCol = 5; // E
                int lastDayCol = firstDayCol + daysInMonth - 1;
                int factDaysCol = lastDayCol + 1;
                int normDaysCol = lastDayCol + 2;
                int factHoursCol = lastDayCol + 3;
                int normHoursCol = lastDayCol + 4;

                // Шапка документа
                ws.Cell(1, 1).Value = "Учреждение образования «Полесский государственный университет»";
                ws.Range(1, 1, 1, normHoursCol).Merge();
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(1, 1).Style.Font.FontSize = 11;
                ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(2, 1).Value = "ТАБЕЛЬ УЧЕТА РАБОЧЕГО ВРЕМЕНИ (БОЛВАНКА)";
                ws.Range(2, 1, 2, normHoursCol).Merge();
                ws.Cell(2, 1).Style.Font.Bold = true;
                ws.Cell(2, 1).Style.Font.FontSize = 13;
                ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromArgb(46, 139, 87);
                ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(4, 1).Value = "Месяц:";
                ws.Cell(4, 1).Style.Font.Bold = true;
                ws.Cell(4, 2).Value = monthName;
                ws.Cell(4, 2).Style.Font.Bold = true;

                ws.Cell(4, 3).Value = "Год:";
                ws.Cell(4, 3).Style.Font.Bold = true;
                ws.Cell(4, 4).Value = year;
                ws.Cell(4, 4).Style.Font.Bold = true;

                // Заголовки таблицы (строки 6-7)
                int headerTop = 6;
                int headerSub = 7;

                ws.Cell(headerTop, 1).Value = "Таб. №";
                ws.Range(headerTop, 1, headerSub, 1).Merge();

                ws.Cell(headerTop, 2).Value = "ФИО";
                ws.Range(headerTop, 2, headerSub, 2).Merge();

                ws.Cell(headerTop, 3).Value = "Должность";
                ws.Range(headerTop, 3, headerSub, 3).Merge();

                ws.Cell(headerTop, 4).Value = "Ставка";
                ws.Range(headerTop, 4, headerSub, 4).Merge();

                ws.Cell(headerTop, firstDayCol).Value = "Отметки о явках и неявках по числам месяца (часы)";
                ws.Range(headerTop, firstDayCol, headerTop, lastDayCol).Merge();

                for (int d = 1; d <= daysInMonth; d++)
                {
                    ws.Cell(headerSub, firstDayCol + d - 1).Value = d;
                }

                ws.Cell(headerTop, factDaysCol).Value = "Отработано дней";
                ws.Range(headerTop, factDaysCol, headerSub, factDaysCol).Merge();

                ws.Cell(headerTop, normDaysCol).Value = "Норма дней";
                ws.Range(headerTop, normDaysCol, headerSub, normDaysCol).Merge();

                ws.Cell(headerTop, factHoursCol).Value = "Отработано часов";
                ws.Range(headerTop, factHoursCol, headerSub, factHoursCol).Merge();

                ws.Cell(headerTop, normHoursCol).Value = "Норма часов";
                ws.Range(headerTop, normHoursCol, headerSub, normHoursCol).Merge();

                IXLRange headerRange = ws.Range(headerTop, 1, headerSub, normHoursCol);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(46, 139, 87);
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                headerRange.Style.Alignment.WrapText = true;
                ws.Row(headerTop).Height = 22;
                ws.Row(headerSub).Height = 20;

                // Подготавливаем список сотрудников (из БД или демонстрационный набор)
                List<DummyEmployeeRow> rows = BuildEmployeeRows(employees);

                int startDataRow = 8;
                int currentRow = startDataRow;
                string firstDayLetter = GetColumnLetter(firstDayCol);
                string lastDayLetter = GetColumnLetter(lastDayCol);

                double[] teacherDailyOptions = { 4.0, 6.0, 6.0, 7.0, 7.2, 8.0, 8.0, 8.0 };
                double[] staffDailyOptions = { 7.0, 8.0, 8.0, 8.0, 8.0, 8.0 };

                foreach (DummyEmployeeRow emp in rows)
                {
                    if (emp.EmployeeId > 0)
                        ws.Cell(currentRow, 1).Value = emp.EmployeeId;
                    else
                        ws.Cell(currentRow, 1).Value = currentRow - startDataRow + 1;

                    ws.Cell(currentRow, 2).Value = emp.FullName;
                    ws.Cell(currentRow, 3).Value = emp.Position;
                    ws.Cell(currentRow, 4).Value = emp.Rate;
                    ws.Cell(currentRow, 4).Style.NumberFormat.Format = "0.##";

                    // Выбираем случайный сценарий посещаемости для сотрудника
                    int scenario = rng.Next(100);
                    int sickStart = -1;
                    int sickLen = 0;
                    if (scenario >= 75) // ~25% — больничный на 3-5 дней
                    {
                        sickStart = rng.Next(3, Math.Max(4, daysInMonth - 6));
                        sickLen = rng.Next(3, 6);
                    }

                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        int col = firstDayCol + d - 1;
                        IXLCell dayCell = ws.Cell(currentRow, col);
                        dayCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                        DayOfWeek dow = new DateTime(year, month, d).DayOfWeek;
                        if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday)
                        {
                            dayCell.Value = "В";
                            dayCell.Style.Fill.BackgroundColor = XLColor.FromArgb(236, 240, 241);
                            dayCell.Style.Font.FontColor = XLColor.FromArgb(127, 140, 141);
                            continue;
                        }

                        if (sickStart > 0 && d >= sickStart && d < sickStart + sickLen)
                        {
                            dayCell.Value = "Б";
                            dayCell.Style.Fill.BackgroundColor = XLColor.FromArgb(253, 237, 236);
                            dayCell.Style.Font.FontColor = XLColor.FromArgb(192, 57, 43);
                            continue;
                        }

                        if (scenario >= 35 && scenario < 75 && rng.Next(100) < 12)
                        {
                            string code = rng.Next(2) == 0 ? "К" : "О";
                            dayCell.Value = code;
                            dayCell.Style.Fill.BackgroundColor = XLColor.FromArgb(254, 249, 231);
                            dayCell.Style.Font.FontColor = XLColor.FromArgb(183, 149, 11);
                            continue;
                        }

                        // Случайное реалистичное количество отработанных часов в рабочий день
                        double baseHours = emp.Category == 1
                            ? teacherDailyOptions[rng.Next(teacherDailyOptions.Length)]
                            : staffDailyOptions[rng.Next(staffDailyOptions.Length)];

                        double scaled = baseHours * (double)emp.Rate;
                        double dayHours = Math.Max(1.0, Math.Round(scaled * 2.0, MidpointRounding.AwayFromZero) / 2.0);
                        dayCell.Value = dayHours;
                    }

                    double dailyNorm = emp.Category == 1 ? 7.2 : 8.0;
                    double empNormHours = Math.Round(normDays * dailyNorm * (double)emp.Rate, 1);

                    ws.Cell(currentRow, factDaysCol).FormulaA1 = $"COUNTIF({firstDayLetter}{currentRow}:{lastDayLetter}{currentRow},\">0\")";
                    ws.Cell(currentRow, factDaysCol).Style.Font.Bold = true;
                    ws.Cell(currentRow, factDaysCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(currentRow, normDaysCol).Value = normDays;
                    ws.Cell(currentRow, normDaysCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(currentRow, factHoursCol).FormulaA1 = $"SUM({firstDayLetter}{currentRow}:{lastDayLetter}{currentRow})";
                    ws.Cell(currentRow, factHoursCol).Style.Font.Bold = true;
                    ws.Cell(currentRow, factHoursCol).Style.NumberFormat.Format = "0.#";
                    ws.Cell(currentRow, factHoursCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(currentRow, normHoursCol).Value = empNormHours;
                    ws.Cell(currentRow, normHoursCol).Style.NumberFormat.Format = "0.#";
                    ws.Cell(currentRow, normHoursCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Row(currentRow).Height = 20;

                    currentRow++;
                }

                // Итоговая строка
                int lastDataRow = currentRow - 1;
                if (lastDataRow >= startDataRow)
                {
                    ws.Cell(currentRow, 2).Value = "ИТОГО:";
                    ws.Cell(currentRow, 2).Style.Font.Bold = true;
                    ws.Cell(currentRow, factDaysCol).FormulaA1 =
                        $"SUM({GetColumnLetter(factDaysCol)}{startDataRow}:{GetColumnLetter(factDaysCol)}{lastDataRow})";
                    ws.Cell(currentRow, factHoursCol).FormulaA1 =
                        $"SUM({GetColumnLetter(factHoursCol)}{startDataRow}:{GetColumnLetter(factHoursCol)}{lastDataRow})";
                    ws.Cell(currentRow, normHoursCol).FormulaA1 =
                        $"SUM({GetColumnLetter(normHoursCol)}{startDataRow}:{GetColumnLetter(normHoursCol)}{lastDataRow})";

                    IXLRange totalRange = ws.Range(currentRow, 1, currentRow, normHoursCol);
                    totalRange.Style.Font.Bold = true;
                    totalRange.Style.Fill.BackgroundColor = XLColor.FromArgb(232, 245, 233);
                    ws.Cell(currentRow, factDaysCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(currentRow, factHoursCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(currentRow, normHoursCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    currentRow++;
                }

                // Границы таблицы
                IXLRange tableRange = ws.Range(headerTop, 1, currentRow - 1, normHoursCol);
                tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                // Легенда и подписи
                int legendRow = currentRow + 1;
                ws.Cell(legendRow, 1).Value =
                    "Условные обозначения: число — отработанные часы за день; В — выходной; Б — больничный; О — отпуск; К — командировка.";
                ws.Cell(legendRow, 1).Style.Font.Italic = true;
                ws.Cell(legendRow, 1).Style.Font.FontColor = XLColor.FromArgb(100, 100, 100);

                ws.Cell(legendRow + 2, 2).Value = "Заведующий кафедрой _____________________";
                ws.Cell(legendRow + 2, 12).Value = "Ответственный за табель _____________________";

                // Настройка ширины столбцов
                ws.Column(1).Width = 7;
                ws.Column(2).Width = 30;
                ws.Column(3).Width = 22;
                ws.Column(4).Width = 8;
                for (int d = 1; d <= daysInMonth; d++)
                {
                    ws.Column(firstDayCol + d - 1).Width = 4.2;
                }
                ws.Column(factDaysCol).Width = 13;
                ws.Column(normDaysCol).Width = 11;
                ws.Column(factHoursCol).Width = 14;
                ws.Column(normHoursCol).Width = 12;

                workbook.SaveAs(filePath);
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
                    ref factDaysCol, ref normDaysCol, ref factHoursCol, ref normHoursCol, dayCols);

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
                        MapHeaderColumn(subText, c, ref idCol, ref nameCol, ref rateCol,
                            ref factDaysCol, ref normDaysCol, ref factHoursCol, ref normHoursCol, dayCols);
                        if (factDaysCol != prevDays || normDaysCol != prevNormDays ||
                            factHoursCol != prevHours || normHoursCol != prevNormHours)
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

                // Считываем дни и часы по сетке чисел месяца (1..31), если она заполнена
                bool usedDailyGrid = false;
                if (dayCols.Count >= 10)
                {
                    int countedDays = 0;
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
                                // Если стоит отметка явки "Я" без числа часов
                                if (normMark == "Я")
                                    countedDays++;
                            }
                        }
                    }

                    if (hasAnyDayContent)
                    {
                        entry.DaysWorked = countedDays;
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
            else if (normDaysCol < 0 && ((h.Contains("норм") && h.Contains("дн")) || h == "normdays"))
            {
                normDaysCol = col;
            }
            else if (normHoursCol < 0 && ((h.Contains("норм") && h.Contains("час")) || h == "normhours"))
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
                (h.Contains("отраб") && h.Contains("час")) ||
                (h.Contains("факт") && h.Contains("час")) ||
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

        private static string GetColumnLetter(int columnNumber)
        {
            string result = string.Empty;
            while (columnNumber > 0)
            {
                int modulo = (columnNumber - 1) % 26;
                result = Convert.ToChar('A' + modulo) + result;
                columnNumber = (columnNumber - modulo) / 26;
            }
            return result;
        }

        private class DummyEmployeeRow
        {
            public int EmployeeId { get; set; }
            public string FullName { get; set; }
            public string Position { get; set; }
            public decimal Rate { get; set; }
            public int Category { get; set; }
        }

        private static List<DummyEmployeeRow> BuildEmployeeRows(DataTable employees)
        {
            var list = new List<DummyEmployeeRow>();

            if (employees != null && employees.Rows.Count > 0)
            {
                foreach (DataRow row in employees.Rows)
                {
                    int empId = employees.Columns.Contains("EmployeeID") && row["EmployeeID"] != DBNull.Value
                        ? Convert.ToInt32(row["EmployeeID"])
                        : 0;
                    string name = employees.Columns.Contains("ФИО") && row["ФИО"] != DBNull.Value
                        ? row["ФИО"].ToString()
                        : string.Empty;
                    string pos = employees.Columns.Contains("Должность") && row["Должность"] != DBNull.Value
                        ? row["Должность"].ToString()
                        : "Преподаватель";
                    decimal rate = employees.Columns.Contains("Ставка") && row["Ставка"] != DBNull.Value
                        ? Convert.ToDecimal(row["Ставка"])
                        : 1.0m;
                    int category = employees.Columns.Contains("Category") && row["Category"] != DBNull.Value
                        ? Convert.ToInt32(row["Category"])
                        : 1;

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        list.Add(new DummyEmployeeRow
                        {
                            EmployeeId = empId,
                            FullName = name,
                            Position = pos,
                            Rate = rate > 0m ? rate : 1.0m,
                            Category = category
                        });
                    }
                }
            }

            if (list.Count == 0)
            {
                list.Add(new DummyEmployeeRow { FullName = "Харитонюк Евгения Николаевна", Position = "Преподаватель", Rate = 1.0m, Category = 1 });
                list.Add(new DummyEmployeeRow { FullName = "Харитонюк Евгения Николаевна", Position = "Преподаватель (совм.)", Rate = 0.5m, Category = 1 });
                list.Add(new DummyEmployeeRow { FullName = "Володько Людмила Павловна", Position = "Заведующий кафедрой", Rate = 1.0m, Category = 1 });
                list.Add(new DummyEmployeeRow { FullName = "Ковальчук Андрей Сергеевич", Position = "Доцент", Rate = 1.0m, Category = 1 });
                list.Add(new DummyEmployeeRow { FullName = "Савчук Ольга Викторовна", Position = "Старший преподаватель", Rate = 0.75m, Category = 1 });
            }

            return list;
        }
    }
}

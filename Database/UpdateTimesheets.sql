-- ============================================================================
-- Скрипт обновления БД PolesGU_Payroll для поддержки импорта табеля (дни + часы)
-- Безопасен для повторного запуска (проверяет наличие столбцов перед добавлением)
-- ============================================================================

USE PolesGU_Payroll;
GO

-- 1. Добавляем столбец фактически отработанных часов
IF COL_LENGTH('dbo.Timesheets', 'HoursWorked') IS NULL
BEGIN
    ALTER TABLE dbo.Timesheets
    ADD HoursWorked DECIMAL(6, 2) NULL;
    PRINT N'Столбец HoursWorked успешно добавлен в таблицу Timesheets.';
END
ELSE
BEGIN
    PRINT N'Столбец HoursWorked уже существует.';
END
GO

-- 2. Добавляем столбец нормы часов за месяц
IF COL_LENGTH('dbo.Timesheets', 'NormHours') IS NULL
BEGIN
    ALTER TABLE dbo.Timesheets
    ADD NormHours DECIMAL(6, 2) NULL;
    PRINT N'Столбец NormHours успешно добавлен в таблицу Timesheets.';
END
ELSE
BEGIN
    PRINT N'Столбец NormHours уже существует.';
END
GO

-- 3. (Рекомендуется) Уникальный индекс: один табель на сотрудника за месяц и год
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_Timesheets_Employee_Period'
      AND object_id = OBJECT_ID('dbo.Timesheets')
)
BEGIN
    -- Удаляем возможные дубликаты перед созданием индекса, оставляя последнюю запись
    ;WITH Duplicates AS (
        SELECT TimesheetID,
               ROW_NUMBER() OVER (
                   PARTITION BY EmployeeID, [Month], [Year]
                   ORDER BY TimesheetID DESC
               ) AS rn
        FROM dbo.Timesheets
    )
    DELETE FROM Duplicates WHERE rn > 1;

    CREATE UNIQUE INDEX UX_Timesheets_Employee_Period
        ON dbo.Timesheets (EmployeeID, [Month], [Year]);
    PRINT N'Уникальный индекс UX_Timesheets_Employee_Period создан.';
END
GO

-- 4. (Рекомендуется) Уникальный индекс для расчетов зарплаты за период
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_PayrollCalculations_Employee_Period'
      AND object_id = OBJECT_ID('dbo.PayrollCalculations')
)
BEGIN
    ;WITH Duplicates AS (
        SELECT PayrollID,
               ROW_NUMBER() OVER (
                   PARTITION BY EmployeeID, [Month], [Year]
                   ORDER BY PayrollID DESC
               ) AS rn
        FROM dbo.PayrollCalculations
    )
    DELETE FROM Duplicates WHERE rn > 1;

    CREATE UNIQUE INDEX UX_PayrollCalculations_Employee_Period
        ON dbo.PayrollCalculations (EmployeeID, [Month], [Year]);
    PRINT N'Уникальный индекс UX_PayrollCalculations_Employee_Period создан.';
END
GO

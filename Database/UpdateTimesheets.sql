-- ============================================================================
-- Скрипт обновления БД PolesGU_Payroll
-- (дни/часы табеля, больничные Б и отпуска О, совместительство, налоговые льготы,
--  разделение выплаты на аванс и окончательный расчёт)
-- Безопасен для повторного запуска (все изменения также выполняются сервером автоматически)
-- ============================================================================

USE PolesGU_Payroll;
GO

-- 1. Таблица Timesheets: часы, больничные (Б) и отпускные дни (О)
IF COL_LENGTH('dbo.Timesheets', 'HoursWorked') IS NULL
    ALTER TABLE dbo.Timesheets ADD HoursWorked DECIMAL(6, 2) NULL;

IF COL_LENGTH('dbo.Timesheets', 'NormHours') IS NULL
    ALTER TABLE dbo.Timesheets ADD NormHours DECIMAL(6, 2) NULL;

IF COL_LENGTH('dbo.Timesheets', 'SickDays') IS NULL
    ALTER TABLE dbo.Timesheets ADD SickDays INT NOT NULL CONSTRAINT DF_Timesheets_SickDays DEFAULT(0);

IF COL_LENGTH('dbo.Timesheets', 'VacationDays') IS NULL
    ALTER TABLE dbo.Timesheets ADD VacationDays INT NOT NULL CONSTRAINT DF_Timesheets_VacationDays DEFAULT(0);
GO

-- 2. Таблица Employees: тип занятости и налоговые льготы (ст. 209 НК РБ)
IF COL_LENGTH('dbo.Employees', 'EmploymentType') IS NULL
    ALTER TABLE dbo.Employees ADD EmploymentType NVARCHAR(60) NOT NULL CONSTRAINT DF_Employees_EmploymentType DEFAULT(N'Основное');

IF COL_LENGTH('dbo.Employees', 'BenefitCategory') IS NULL
    ALTER TABLE dbo.Employees ADD BenefitCategory INT NOT NULL CONSTRAINT DF_Employees_BenefitCategory DEFAULT(0);

IF COL_LENGTH('dbo.Employees', 'ChildrenBenefit') IS NULL
    ALTER TABLE dbo.Employees ADD ChildrenBenefit INT NOT NULL CONSTRAINT DF_Employees_ChildrenBenefit DEFAULT(0);
GO

-- 3. Таблица PayrollCalculations: выплаты по Б/О, применённый вычет и сумма аванса
IF COL_LENGTH('dbo.PayrollCalculations', 'SickVacationPay') IS NULL
    ALTER TABLE dbo.PayrollCalculations ADD SickVacationPay DECIMAL(10, 2) NOT NULL CONSTRAINT DF_Payroll_SickVac DEFAULT(0);

IF COL_LENGTH('dbo.PayrollCalculations', 'TaxDeduction') IS NULL
    ALTER TABLE dbo.PayrollCalculations ADD TaxDeduction DECIMAL(10, 2) NOT NULL CONSTRAINT DF_Payroll_TaxDed DEFAULT(0);

IF COL_LENGTH('dbo.PayrollCalculations', 'AdvanceAmount') IS NULL
    ALTER TABLE dbo.PayrollCalculations ADD AdvanceAmount DECIMAL(10, 2) NOT NULL CONSTRAINT DF_Payroll_Advance DEFAULT(0);
GO

-- 4. Уникальный индекс: один табель на запись сотрудника за месяц и год
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_Timesheets_Employee_Period'
      AND object_id = OBJECT_ID('dbo.Timesheets')
)
BEGIN
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
END
GO

-- 5. Уникальный индекс для расчётов зарплаты за период
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
END
GO

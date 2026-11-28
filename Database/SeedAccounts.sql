-- ============================================================================
-- Учётные записи для входа в систему PolesGU_Payroll
--
-- Скрипт безопасен для повторного запуска:
--   • роли добавляются, только если их ещё нет;
--   • администратор создаётся, только если таблица Users пуста.
--
-- По умолчанию:  логин  admin
--                пароль admin123   (SHA-256, нижний регистр)
--
-- После первого входа смените пароль в разделе «Пользователи».
-- ============================================================================

USE PolesGU_Payroll;
GO

-- 1. Справочник ролей (RoleID 1..4 совпадают с экраном «Пользователи»)
SET IDENTITY_INSERT dbo.Roles ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleID = 1)
    INSERT INTO dbo.Roles (RoleID, RoleName) VALUES (1, N'Администратор');

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleID = 2)
    INSERT INTO dbo.Roles (RoleID, RoleName) VALUES (2, N'Бухгалтер');

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleID = 3)
    INSERT INTO dbo.Roles (RoleID, RoleName) VALUES (3, N'Экономист');

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleID = 4)
    INSERT INTO dbo.Roles (RoleID, RoleName) VALUES (4, N'Сотрудник');

SET IDENTITY_INSERT dbo.Roles OFF;
GO

-- 2. Администратор по умолчанию (только если пользователей нет совсем)
IF NOT EXISTS (SELECT 1 FROM dbo.Users)
BEGIN
    INSERT INTO dbo.Users (Login, PasswordHash, RoleID)
    VALUES ('admin',
            '240be518fabd2724ddb6f04eeb1da5967448d7e831c08c8fa822809f74c720a9', -- admin123
            1);

    PRINT 'Создана учётная запись: admin / admin123';
END
ELSE
BEGIN
    PRINT 'Пользователи уже есть — администратор не создавался.';
END
GO

-- 3. Контроль: что получилось
SELECT u.UserID, u.Login, r.RoleName
FROM dbo.Users u
JOIN dbo.Roles r ON u.RoleID = r.RoleID
ORDER BY u.UserID;
GO

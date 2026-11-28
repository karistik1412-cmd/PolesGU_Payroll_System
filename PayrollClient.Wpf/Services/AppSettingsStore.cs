using System;
using Microsoft.Win32;

namespace PayrollClient.Services
{
    // Сохранение выбранного языка между запусками.
    // Пишем в HKCU — не требует прав администратора и не мешает установке
    // (в отличие от записи рядом с exe в Program Files).
    public static class AppSettingsStore
    {
        private const string RegistryPath = @"Software\PolesGU\PayrollSystem";
        private const string LanguageValue = "UiLanguage";

        public static string LoadLanguage()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key == null) return "ru";
                    object value = key.GetValue(LanguageValue);
                    return value == null ? "ru" : value.ToString();
                }
            }
            catch (Exception)
            {
                // Реестр недоступен — работаем на языке по умолчанию
                return "ru";
            }
        }

        public static void SaveLanguage(string code)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null) key.SetValue(LanguageValue, code ?? "ru");
                }
            }
            catch (Exception)
            {
                // Молча игнорируем: язык просто не запомнится до следующего запуска
            }
        }
    }
}

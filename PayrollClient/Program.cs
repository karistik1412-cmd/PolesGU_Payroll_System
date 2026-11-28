using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PayrollClient.Services;

namespace PayrollClient
{
    internal static class Program
    {
        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Восстанавливаем выбранный ранее язык интерфейса
            Localization.Current = AppLanguage.Russian;
            Localization.SetByCode(AppSettingsStore.LoadLanguage());

            Application.Run(new LoginForm());
        }
    }
}

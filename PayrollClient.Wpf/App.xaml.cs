using System;
using System.Windows;
using System.Windows.Threading;
using PayrollClient.Services;
using PayrollClient.Views;
using Localization = PayrollClient.Services.Localization; // снимает конфликт с System.Windows.Localization

namespace PayrollClient
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Любая необработанная ошибка интерфейса не должна закрывать приложение молча
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Восстанавливаем выбранный ранее язык интерфейса
            Localization.Current = AppLanguage.Russian;
            Localization.SetByCode(AppSettingsStore.LoadLanguage());

            LoginWindow login = new LoginWindow();
            login.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                string.Format("{0}\n\n{1}", Localization.T("common.error"), e.Exception.Message),
                Localization.T("common.error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}

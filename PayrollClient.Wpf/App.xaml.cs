using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PayrollClient.Services;
using PayrollClient.Views;
using Localization = PayrollClient.Services.Localization; // снимает конфликт с System.Windows.Localization

namespace PayrollClient
{
    public partial class App : Application
    {
        // Журнал запуска. Пишем в %LOCALAPPDATA%\PolesGU\PayrollSystem\client.log —
        // по нему видно, на каком шаге клиент остановился, если окно не появилось.
        internal static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"PolesGU\PayrollSystem\client.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ловим всё: исключения интерфейса, фоновых потоков и задачи
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            Log("=== Запуск клиента ===");

            try
            {
                // Тема подключается здесь, а не в App.xaml: ошибка в словаре стилей
                // иначе убивает приложение ещё до первого окна и без сообщения.
                LoadTheme();
                Log("Тема загружена");

                RegisterConverters();
                Log("Конвертеры зарегистрированы");

                // Восстанавливаем выбранный ранее язык интерфейса
                Localization.SetByCode(AppSettingsStore.LoadLanguage());
                Log("Язык интерфейса: " + Localization.Code);

                LoginWindow login = new LoginWindow();
                MainWindow = login;
                login.Show();
                Log("Окно входа показано");
            }
            catch (Exception ex)
            {
                Log("ОШИБКА ЗАПУСКА: " + ex);
                ShowFatal("Не удалось открыть окно входа в систему.", ex);
            }
        }

        private void LoadTheme()
        {
            ResourceDictionary theme = new ResourceDictionary
            {
                Source = new Uri("Themes/Theme.xaml", UriKind.Relative)
            };
            Resources.MergedDictionaries.Add(theme);
        }

        // Конвертеры для XAML: раньше объявлялись в App.xaml, но экземпляры
        // создаются при разборе словаря — ошибка там же гасила приложение молча.
        private void RegisterConverters()
        {
            Resources["Term"] = new Converters.TermConverter();
            Resources["ColumnHeader"] = new Converters.ColumnHeaderConverter();
            Resources["BoolToVisibility"] = new Converters.BoolToVisibilityConverter();
            Resources["Money"] = new Converters.MoneyConverter();
            Resources["Rate"] = new Converters.RateConverter();
            Resources["EmptyToVisibility"] = new Converters.EmptyToVisibilityConverter();
        }

        // Ошибка в интерфейсе: показываем текст и продолжаем работу
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log("ОШИБКА ИНТЕРФЕЙСА: " + e.Exception);
            MessageBox.Show(
                "Произошла ошибка интерфейса:\n\n" + Describe(e.Exception) +
                "\n\nПодробности записаны в файл:\n" + LogFile,
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        // Ошибка в фоновом потоке: приложение всё равно завершится, но причину оставим в журнале
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            Log("НЕОБРАБОТАННАЯ ОШИБКА: " + (ex == null ? e.ExceptionObject.ToString() : ex.ToString()));
        }

        // Окно с текстом ошибки без использования темы и словарей перевода:
        // оно должно открыться даже если сломаны ресурсы или локализация.
        private void ShowFatal(string caption, Exception ex)
        {
            string text = caption + "\r\n\r\n" + Describe(ex) +
                          "\r\n\r\nПодробности записаны в файл:\r\n" + LogFile;

            try
            {
                Window window = new Window
                {
                    Title = "Ошибка запуска",
                    Width = 760,
                    Height = 460,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = Brushes.White
                };

                TextBox details = new TextBox
                {
                    Text = text + "\r\n\r\n" + ex,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    BorderThickness = new Thickness(0),
                    FontSize = 12,
                    Margin = new Thickness(16),
                    Background = Brushes.White,
                    Foreground = Brushes.Black
                };

                window.Content = details;
                MainWindow = window;
                window.Show();
            }
            catch (Exception inner)
            {
                Log("Не удалось показать окно с ошибкой: " + inner);
                MessageBox.Show(caption + "\r\n\r\n" + Describe(ex),
                    "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Краткое описание ошибки: тип, текст и самое информативное место в стеке
        private static string Describe(Exception ex)
        {
            if (ex == null) return "Неизвестная ошибка.";

            string text = ex.GetType().Name + ": " + ex.Message;

            // У XamlParseException настоящая причина обычно во внутреннем исключении
            Exception root = ex;
            while (root.InnerException != null) root = root.InnerException;
            if (!ReferenceEquals(root, ex))
                text += "\r\nПричина: " + root.GetType().Name + ": " + root.Message;

            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                string[] lines = ex.StackTrace.Split('\n');
                text += "\r\nМесто: " + lines[0].Trim();
            }

            return text;
        }

        internal static void Log(string message)
        {
            try
            {
                string folder = Path.GetDirectoryName(LogFile);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.AppendAllText(LogFile,
                    DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (Exception)
            {
                // Журнал — вспомогательный инструмент: если писать некуда, работаем дальше
            }
        }
    }
}

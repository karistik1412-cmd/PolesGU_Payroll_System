using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using PayrollClient.Network;
using PayrollClient.Services;
using Localization = PayrollClient.Services.Localization; // снимает конфликт с System.Windows.Localization

namespace PayrollClient.Views
{
    public partial class LoginWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Brand { get { return Localization.T("shell.brand"); } }
        public string Subtitle { get { return Localization.T("login.subtitle"); } }
        public string Caption { get { return Localization.T("login.caption"); } }
        public string LoginLabel { get { return Localization.T("login.login"); } }
        public string PasswordLabel { get { return Localization.T("login.password"); } }
        public string SubmitLabel { get { return Localization.T("login.submit"); } }
        public string Footer { get { return Localization.T("login.footer"); } }

        public LoginWindow()
        {
            InitializeComponent();
            DataContext = this;
            Title = Localization.T("app.title") == "app.title"
                ? "ПолесГУ — Система начисления заработной платы"
                : Localization.T("app.title");
            Loaded += (s, e) => txtLogin.Focus();
        }

        private void OnPasswordKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnLoginClick(sender, e);
        }

        private void OnLoginClick(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                ShowError(Localization.T("login.fillFields"));
                return;
            }

            btnLogin.IsEnabled = false;
            try
            {
                NetworkMessage request = new NetworkMessage
                {
                    Command = "LOGIN",
                    Data = string.Format("{0}|{1}", login, password)
                };

                NetworkMessage response = new ServerConnection().SendRequest(request);

                if (response != null && response.IsSuccess)
                {
                    MainWindow main = new MainWindow(response.Data);
                    Application.Current.MainWindow = main;
                    main.Show();
                    Close();
                }
                else
                {
                    ShowError(response == null ? Localization.T("common.error") : response.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                ShowError(Localization.T("login.networkError") + ex.Message);
            }
            finally
            {
                btnLogin.IsEnabled = true;
            }
        }

        private void ShowError(string message)
        {
            lblError.Text = message;
            lblError.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}

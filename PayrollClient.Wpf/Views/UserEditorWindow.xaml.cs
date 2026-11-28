using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PayrollClient.Services;
using Localization = PayrollClient.Services.Localization; // снимает конфликт с System.Windows.Localization

namespace PayrollClient.Views
{
    // Создание учётной записи: логин, пароль (с подтверждением) и роль
    public partial class UserEditorWindow : Window
    {
        public string Login { get; private set; }
        public string Password { get; private set; }
        public int RoleId { get; private set; }

        public UserEditorWindow()
        {
            InitializeComponent();

            Title = Localization.T("users.new");
            lblHeader.Text = Localization.T("users.new");
            lblLogin.Text = Localization.T("users.login");
            lblPassword.Text = Localization.T("users.password");
            lblPassword2.Text = Localization.T("users.passwordRepeat");
            lblRole.Text = Localization.T("users.role");

            btnCancel.Content = Localization.T("common.cancel");
            btnSave.Content = Localization.T("common.save");

            // Роли: значения RoleID совпадают с тем, что ждёт сервер
            cmbRole.Items.Add(new ComboBoxItem { Content = Localization.RoleText("Администратор"), Tag = 1 });
            cmbRole.Items.Add(new ComboBoxItem { Content = Localization.RoleText("Бухгалтер"), Tag = 2 });
            cmbRole.Items.Add(new ComboBoxItem { Content = Localization.RoleText("Экономист"), Tag = 3 });
            cmbRole.Items.Add(new ComboBoxItem { Content = Localization.RoleText("Сотрудник"), Tag = 4 });
            cmbRole.SelectedIndex = 2; // по умолчанию — экономист

            Loaded += (s, e) => txtLogin.Focus();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text.Trim();
            string password = txtPassword.Password;
            string repeat = txtPassword2.Password;

            if (string.IsNullOrEmpty(login))
            {
                Warn(Localization.T("users.fillLogin"));
                return;
            }

            if (password.Length < 5)
            {
                Warn(Localization.T("users.shortPassword"));
                return;
            }

            if (password != repeat)
            {
                Warn(Localization.T("users.passwordMismatch"));
                return;
            }

            ComboBoxItem role = cmbRole.SelectedItem as ComboBoxItem;
            if (role == null)
            {
                Warn(Localization.T("users.selectRole"));
                return;
            }

            Login = login;
            Password = password;
            RoleId = (int)role.Tag;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Warn(string message)
        {
            MessageBox.Show(message, Localization.T("common.attention"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

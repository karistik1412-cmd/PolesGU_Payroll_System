using System;
using System.Drawing;
using System.Windows.Forms;
using PayrollClient.Network;

namespace PayrollClient
{
    public partial class LoginForm : Form
    {
        // Объявляем элементы управления на уровне класса, чтобы они были доступны в методе клика
        private TextBox txtLogin;
        private TextBox txtPassword;
        private Button btnLogin;

        public LoginForm()
        {
            // Настраиваем само окно (убираем стандартные рамки для красоты)
            this.Text = "Авторизация - ПолесГУ";
            this.Size = new Size(400, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 244, 248); // Светло-серый фон
            this.FormBorderStyle = FormBorderStyle.FixedSingle; // Фиксированный размер
            this.MaximizeBox = false;
            this.Font = new Font("Segoe UI", 10f);

            // Создаем контейнер для центрирования элементов
            Panel centerPanel = new Panel();
            centerPanel.Dock = DockStyle.Fill;
            this.Controls.Add(centerPanel);

            // --- ЗАГОЛОВОК ---
            Label lblTitle = new Label();
            lblTitle.Text = "Вход в систему";
            lblTitle.Font = new Font("Segoe UI", 18f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(45, 52, 54);
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            lblTitle.Location = new Point(0, 50);
            lblTitle.Size = new Size(400, 50);
            centerPanel.Controls.Add(lblTitle);

            // --- ПОЛЕ ЛОГИНА ---
            Label lblLogin = new Label();
            lblLogin.Text = "Логин";
            lblLogin.ForeColor = Color.Gray;
            lblLogin.Location = new Point(100, 140);
            centerPanel.Controls.Add(lblLogin);

            txtLogin = new TextBox();
            txtLogin.Location = new Point(100, 165);
            txtLogin.Size = new Size(200, 30);
            txtLogin.Font = new Font("Segoe UI", 12f);
            centerPanel.Controls.Add(txtLogin);

            // --- ПОЛЕ ПАРОЛЯ ---
            Label lblPassword = new Label();
            lblPassword.Text = "Пароль";
            lblPassword.ForeColor = Color.Gray;
            lblPassword.Location = new Point(100, 210);
            centerPanel.Controls.Add(lblPassword);

            txtPassword = new TextBox();
            txtPassword.Location = new Point(100, 235);
            txtPassword.Size = new Size(200, 30);
            txtPassword.Font = new Font("Segoe UI", 12f);
            txtPassword.PasswordChar = '*'; // Скрываем символы
            centerPanel.Controls.Add(txtPassword);

            // --- КНОПКА ВОЙТИ ---
            btnLogin = new Button();
            btnLogin.Text = "Войти";
            btnLogin.Location = new Point(100, 300);
            btnLogin.Size = new Size(200, 45);
            btnLogin.FlatStyle = FlatStyle.Flat;
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.BackColor = Color.FromArgb(46, 139, 87); // Зеленый
            btnLogin.ForeColor = Color.White;
            btnLogin.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            btnLogin.Cursor = Cursors.Hand;

            // Эффект при наведении
            btnLogin.MouseEnter += (s, e) => btnLogin.BackColor = Color.FromArgb(36, 119, 67);
            btnLogin.MouseLeave += (s, e) => btnLogin.BackColor = Color.FromArgb(46, 139, 87);

            // Привязываем наш старый метод клика
            btnLogin.Click += btnLogin_Click;

            centerPanel.Controls.Add(btnLogin);
        }

        // --- ВАШ СТАРЫЙ КОД ОБРАБОТКИ КНОПКИ ОСТАЕТСЯ БЕЗ ИЗМЕНЕНИЙ ---
        private void btnLogin_Click(object sender, EventArgs e)
        {
            string login = txtLogin.Text;
            string password = txtPassword.Text;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Заполните все поля!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var connection = new ServerConnection();
                var request = new NetworkMessage
                {
                    Command = "LOGIN",
                    Data = $"{login}|{password}"
                };

                var response = connection.SendRequest(request);

                if (response.IsSuccess)
                {
                    MainForm mainForm = new MainForm(response.Data);
                    mainForm.Show();
                    this.Hide();
                }
                else
                {
                    MessageBox.Show(response.ErrorMessage, "Ошибка авторизации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось связаться с сервером. Убедитесь, что он запущен.\n" + ex.Message, "Сетевая ошибка");
            }
        }
    }
}
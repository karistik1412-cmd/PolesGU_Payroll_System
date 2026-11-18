using System;
using System.Drawing;
using System.Windows.Forms;
using PayrollClient.Network;
using PayrollClient.Services;

namespace PayrollClient
{
    public partial class LoginForm : Form
    {
        private readonly TextBox txtLogin;
        private readonly TextBox txtPassword;
        private readonly Button btnLogin;

        public LoginForm()
        {
            this.Text = "ПолесГУ — Вход в систему";
            this.ClientSize = new Size(430, 495);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = UiTheme.CardBg;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Font = new Font("Segoe UI", 10f);

            // --- ВЕРХНЯЯ ФИРМЕННАЯ ШАПКА ПОЛЕСГУ ---
            Panel headerBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 135,
                BackColor = UiTheme.SidebarBg
            };
            headerBanner.Paint += (s, e) =>
            {
                using (SolidBrush accent = new SolidBrush(UiTheme.AccentGreen))
                {
                    e.Graphics.FillRectangle(accent, 0, headerBanner.Height - 4, headerBanner.Width, 4);
                }
            };
            this.Controls.Add(headerBanner);

            Label lblBrand = new Label
            {
                Text = "ПОЛЕСГУ",
                Font = new Font("Segoe UI", 22f, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 30),
                Size = new Size(430, 44)
            };
            headerBanner.Controls.Add(lblBrand);

            Label lblSubtitle = new Label
            {
                Text = "Система начисления заработной платы",
                Font = new Font("Segoe UI", 9.75f, FontStyle.Regular),
                ForeColor = UiTheme.SidebarTextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 78),
                Size = new Size(430, 26)
            };
            headerBanner.Controls.Add(lblSubtitle);

            // --- ЗАГОЛОВОК ФОРМЫ ---
            Label lblTitle = new Label
            {
                Text = "Вход в учётную запись",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = UiTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 158),
                Size = new Size(430, 32)
            };
            this.Controls.Add(lblTitle);

            int fieldX = 60;
            int fieldWidth = 310;

            // --- ПОЛЕ ЛОГИНА ---
            Label lblLogin = new Label
            {
                Text = "Логин",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(fieldX, 206),
                AutoSize = true
            };
            this.Controls.Add(lblLogin);

            txtLogin = new TextBox
            {
                Font = new Font("Segoe UI", 11f)
            };
            Panel loginBox = UiTheme.WrapTextBox(txtLogin, fieldX, 229, fieldWidth, 36);
            this.Controls.Add(loginBox);

            // --- ПОЛЕ ПАРОЛЯ ---
            Label lblPassword = new Label
            {
                Text = "Пароль",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                ForeColor = UiTheme.TextSecondary,
                Location = new Point(fieldX, 282),
                AutoSize = true
            };
            this.Controls.Add(lblPassword);

            txtPassword = new TextBox
            {
                Font = new Font("Segoe UI", 11f),
                UseSystemPasswordChar = true
            };
            Panel passBox = UiTheme.WrapTextBox(txtPassword, fieldX, 305, fieldWidth, 36);
            this.Controls.Add(passBox);

            // --- КНОПКА ВОЙТИ ---
            btnLogin = UiTheme.CreatePrimaryButton("Войти в систему", fieldX, 368, fieldWidth, 44);
            btnLogin.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            btnLogin.Click += btnLogin_Click;
            this.Controls.Add(btnLogin);

            // --- НИЖНЯЯ ПОДПИСЬ ---
            Label lblFooter = new Label
            {
                Text = "УО «Полесский государственный университет»",
                Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 452),
                Size = new Size(430, 22)
            };
            this.Controls.Add(lblFooter);

            this.AcceptButton = btnLogin;
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            string login = txtLogin.Text.Trim();
            string password = txtPassword.Text;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Заполните логин и пароль!", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                btnLogin.Enabled = false;
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
                    mainForm.FormClosed += (s, args) => this.Close();
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
                MessageBox.Show("Не удалось связаться с сервером. Убедитесь, что он запущен.\n" + ex.Message,
                    "Сетевая ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnLogin.Enabled = true;
            }
        }
    }
}

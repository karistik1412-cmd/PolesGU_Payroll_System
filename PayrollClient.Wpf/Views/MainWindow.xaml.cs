using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using PayrollClient.Controls;
using PayrollClient.Network;
using PayrollClient.Services;
using Localization = PayrollClient.Services.Localization; // снимает конфликт с System.Windows.Localization

namespace PayrollClient.Views
{
    public partial class MainWindow : Window
    {
        // --- Данные авторизованного пользователя ---
        private readonly string _userRole = "Экономист";
        private readonly int _currentUserId = -1;
        private readonly string _currentUserLogin = string.Empty;

        // --- Выбранный расчётный период ---
        private int _selectedMonth = DateTime.Now.Month;
        private int _selectedYear = DateTime.Now.Year >= 2020 && DateTime.Now.Year <= 2035
            ? DateTime.Now.Year
            : 2026;

        // --- Состояние экрана «Сотрудники» ---
        private DataTable _employees;
        private DataView _employeesView;
        private DataGrid _employeesGrid;
        private string _employeesFooter = string.Empty;

        // --- Состояние экрана «Главная» ---
        private string _homeFooter = string.Empty;

        // --- Состояние экрана «Табель» ---
        private DataTable _timesheet;
        private DataView _timesheetView;
        private DataGrid _timesheetGrid;

        // --- Состояние экрана «Расчёт зарплаты» ---
        private DataTable _payrollResult;
        private DataView _payrollView;
        private DataGrid _payrollGrid;
        private int _payrollHalf = 2;

        // --- Состояние экрана «Пользователи» ---
        private DataTable _users;
        private DataView _usersView;
        private DataGrid _usersGrid;

        // Пока окно загружается, обработчики выбора пунктов меню не должны
        // запускать построение экранов (иначе экран строится дважды).
        private bool _ready;

        public MainWindow(string authData)
        {
            InitializeComponent();

            string[] parts = (authData ?? string.Empty).Split('|');
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) _userRole = parts[0].Trim();
            int parsedId;
            if (parts.Length > 1 && int.TryParse(parts[1], out parsedId)) _currentUserId = parsedId;
            if (parts.Length > 2) _currentUserLogin = parts[2];

            ApplyLocalization();
            BuildPeriodSelectors();
            ShowHomeScreen();

            _ready = true;
            navHome.IsChecked = true;

            // При изменении размера окна графики перерисовываются под новую ширину
            SizeChanged += (s, e) =>
            {
                if (_ready && navHome.IsChecked == true) ShowHomeScreen();
            };
        }

        // =====================================================================
        //  Локализация оболочки
        // =====================================================================
        private void ApplyLocalization()
        {
            Title = string.Format("ПолесГУ — {0} | {1} ({2})",
                Localization.T("shell.brandSub"),
                Localization.T("users.role").ToLowerInvariant(),
                Localization.RoleText(_userRole));

            string caption = !string.IsNullOrEmpty(_currentUserLogin)
                ? string.Format("{0} · {1}", _currentUserLogin, Localization.RoleText(_userRole))
                : string.Format("{0}: {1}", Localization.T("users.role"), Localization.RoleText(_userRole));

            Title = string.Format(Localization.T("app.title"), caption);

            lblBrand.Text = Localization.T("shell.brand");
            lblBrandSub.Text = Localization.T("shell.brandSub");
            lblRoleInfo.Text = caption;
            btnLogout.Content = Localization.T("shell.logout");

            navHome.Content = Localization.T("menu.home");
            navEmployees.Content = Localization.T("menu.employees");
            navTimesheet.Content = Localization.T("menu.timesheet");
            navPayroll.Content = Localization.T("menu.payroll");
            navUsers.Content = Localization.T("menu.users");

            // Разделы, доступные только бухгалтеру и администратору
            bool canPayroll = _userRole == "Бухгалтер" || _userRole == "Администратор";
            navTimesheet.Visibility = canPayroll ? Visibility.Visible : Visibility.Collapsed;
            navPayroll.Visibility = canPayroll ? Visibility.Visible : Visibility.Collapsed;
            navUsers.Visibility = _userRole == "Администратор" ? Visibility.Visible : Visibility.Collapsed;

            lblYearCaption.Text = Localization.T("common.year") + ":";
            lblSearch.Text = Localization.T("common.search");
        }

        private void BuildPeriodSelectors()
        {
            cmbMonth.Items.Clear();
            for (int m = 1; m <= 12; m++)
                cmbMonth.Items.Add(new ComboBoxItem { Content = Localization.T("month." + m), Tag = m });
            cmbMonth.SelectedIndex = _selectedMonth - 1;

            cmbYear.Items.Clear();
            for (int y = 2020; y <= 2035; y++)
                cmbYear.Items.Add(new ComboBoxItem { Content = y.ToString(), Tag = y });
            cmbYear.SelectedItem = cmbYear.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == _selectedYear);
            if (cmbYear.SelectedItem == null) cmbYear.SelectedIndex = 6;
        }

        private void OnPeriodChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem monthItem = cmbMonth.SelectedItem as ComboBoxItem;
            ComboBoxItem yearItem = cmbYear.SelectedItem as ComboBoxItem;
            if (monthItem == null || yearItem == null) return;

            _selectedMonth = (int)monthItem.Tag;
            _selectedYear = (int)yearItem.Tag;

            if (navHome.IsChecked == true) ShowHomeScreen();
            else if (navEmployees.IsChecked == true) LoadEmployees();
            else if (navTimesheet.IsChecked == true) LoadTimesheet();
            else if (navPayroll.IsChecked == true) LoadPayroll();
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (navEmployees.IsChecked == true) ApplyEmployeeFilter();
            else if (navTimesheet.IsChecked == true) ApplyTimesheetFilter();
            else if (navUsers.IsChecked == true) ApplyUsersFilter();
        }

        // =====================================================================
        //  Общие помощники разметки
        // =====================================================================
        private static SolidColorBrush Res(string key)
        {
            return (SolidColorBrush)Application.Current.Resources[key];
        }

        private void SetHeader(string title, string subtitle)
        {
            lblPageTitle.Text = title;
            lblPageSubtitle.Text = subtitle ?? string.Empty;
            lblPageSubtitle.Visibility = string.IsNullOrWhiteSpace(subtitle)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private Button AddAction(string text, string styleKey, RoutedEventHandler click)
        {
            Button button = new Button
            {
                Content = text,
                Style = (Style)Application.Current.Resources[styleKey],
                Margin = new Thickness(0, 0, 10, 0),
                MinWidth = ButtonMetrics.Measure(null) // ширина уточняется ниже по тексту
            };
            if (click != null) button.Click += click;
            button.MinWidth = ButtonMetrics.Measure(button);
            actionBar.Children.Add(button);
            return button;
        }

        private void ResetToolbar(bool showSearch = true, bool showPeriod = true)
        {
            actionBar.Children.Clear();

            // В панели одна колонка под поля: на «Главной» это период,
            // на «Сотрудниках» — поиск. Кнопки всегда стоят под ней.
            periodPanel.Visibility = showPeriod ? Visibility.Visible : Visibility.Collapsed;
            searchPanel.Visibility = showSearch ? Visibility.Visible : Visibility.Collapsed;

            // У поиска собственная подпись («Поиск:»), поэтому общая подпись
            // нужна только для периода — иначе выходило «Поиск: Поиск:».
            lblMonthCaption.Text = showPeriod ? Localization.T("common.month") + ":" : string.Empty;
            lblMonthCaption.Visibility = showPeriod ? Visibility.Visible : Visibility.Collapsed;
        }

        private Border CreateCard(string title, string subtitle, out StackPanel body)
        {
            Border card = new Border { Style = (Style)Application.Current.Resources["Card"], Margin = new Thickness(6) };
            Grid inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock titleBlock = new TextBlock { Text = title, Style = (Style)Application.Current.Resources["CardTitle"] };
            Grid.SetColumn(titleBlock, 0);
            header.Children.Add(titleBlock);

            TextBlock subBlock = new TextBlock
            {
                Text = subtitle ?? string.Empty,
                Style = (Style)Application.Current.Resources["HintText"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(subBlock, 1);
            header.Children.Add(subBlock);

            body = new StackPanel();
            Grid.SetRow(header, 0);
            Grid.SetRow(body, 1);
            inner.Children.Add(header);
            inner.Children.Add(body);
            card.Child = inner;
            return card;
        }

        // =====================================================================
        //  ЭКРАН 1: ГЛАВНАЯ
        // =====================================================================
        private void ShowHomeScreen()
        {
            SetHeader(Localization.T("home.welcome", Localization.RoleText(_userRole)),
                      Localization.T("home.subtitle"));
            ResetToolbar(showSearch: false);
            AddAction(Localization.T("chart.refresh"), "PrimaryButton", (s, e) => ShowHomeScreen());
            AddAction(Localization.T("chart.print"), "OutlineButton", OnPrintCharts);

            pageHost.Children.Clear();
            pageHost.RowDefinitions.Clear();

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            Grid content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // KPI
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // справка
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // язык
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // графики

            // --- 1. Показатели ---
            Grid kpi = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            for (int i = 0; i < 4; i++)
                kpi.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string empCount = "—";
            string userCount = "—";
            string deptCount = "—";
            string fundText = "0,00";
            try
            {
                NetworkMessage response = new ServerConnection().SendRequest(
                    new NetworkMessage { Command = "GET_DASHBOARD" });
                if (response != null && response.IsSuccess && !string.IsNullOrEmpty(response.Data))
                {
                    string[] stats = response.Data.Split('|');
                    if (stats.Length > 0) empCount = stats[0];
                    if (stats.Length > 1) userCount = stats[1];
                    if (stats.Length > 2)
                    {
                        decimal totalFund;
                        if (decimal.TryParse(stats[2].Replace(',', '.'), NumberStyles.Any,
                                CultureInfo.InvariantCulture, out totalFund))
                            fundText = Math.Round(totalFund, 2).ToString("N2");
                    }
                    if (stats.Length > 3) deptCount = stats[3];
                }
            }
            catch (Exception)
            {
                // Сервер недоступен — показываем прочерки
            }

            AddStatCard(kpi, 0, Localization.T("home.kpiEmployees"), empCount, Localization.T("home.kpiEmployeesCap"));
            AddStatCard(kpi, 1, Localization.T("home.kpiDepts"), deptCount, Localization.T("home.kpiDeptsCap"));
            AddStatCard(kpi, 2, Localization.T("home.kpiUsers"), userCount, Localization.T("home.kpiUsersCap"));
            AddStatCard(kpi, 3, Localization.T("home.kpiFund"), fundText + " р.", Localization.T("home.kpiFundCap"));
            Grid.SetRow(kpi, 0);
            content.Children.Add(kpi);

            // --- 2. Справка по периоду ---
            Border info = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Margin = new Thickness(6),
                Padding = new Thickness(20, 14, 20, 14)
            };
            StackPanel infoBody = new StackPanel();
            infoBody.Children.Add(new TextBlock
            {
                Text = Localization.T("home.infoTitle", MonthName(DateTime.Today.Month), DateTime.Today.Year,
                    DateTime.Today.ToString("dd.MM.yyyy")),
                Style = (Style)Application.Current.Resources["CardTitle"],
                Margin = new Thickness(0, 0, 0, 8)
            });
            int workDays = CountWorkingDaysLocal(DateTime.Today.Year, DateTime.Today.Month);
            decimal teacherHours = Math.Round(workDays * 7.2m, 1);
            infoBody.Children.Add(new TextBlock
            {
                Text = Localization.T("home.infoNorm", workDays, teacherHours.ToString("0.#")),
                FontSize = 11.5, Foreground = Res("TextPrimary"), Margin = new Thickness(0, 0, 0, 4)
            });
            infoBody.Children.Add(new TextBlock
            {
                Text = Localization.T("home.infoSchedule", PayoutStageText()),
                FontSize = 11.5, Foreground = Res("TextPrimary"), Margin = new Thickness(0, 0, 0, 4)
            });
            infoBody.Children.Add(new TextBlock
            {
                Text = Localization.T("home.infoRules"),
                FontSize = 11, Foreground = Res("TextSecondary"), TextWrapping = TextWrapping.Wrap
            });
            info.Child = infoBody;
            Grid.SetRow(info, 1);
            content.Children.Add(info);

            // --- 3. Выбор языка ---
            Border langCard = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Margin = new Thickness(6),
                Padding = new Thickness(20, 12, 20, 12)
            };
            Grid langGrid = new Grid();
            langGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            langGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            langGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock langCaption = new TextBlock
            {
                Text = Localization.T("lang.caption"),
                Style = (Style)Application.Current.Resources["FieldLabel"],
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(langCaption, 0);
            langGrid.Children.Add(langCaption);

            ComboBox cmbLanguage = new ComboBox
            {
                Style = (Style)Application.Current.Resources["AppComboBox"],
                Width = 190
            };
            cmbLanguage.Items.Add(new ComboBoxItem { Content = Localization.DisplayName(AppLanguage.Russian), Tag = AppLanguage.Russian });
            cmbLanguage.Items.Add(new ComboBoxItem { Content = Localization.DisplayName(AppLanguage.English), Tag = AppLanguage.English });
            cmbLanguage.Items.Add(new ComboBoxItem { Content = Localization.DisplayName(AppLanguage.Belarusian), Tag = AppLanguage.Belarusian });
            cmbLanguage.SelectedIndex = (int)Localization.Current;
            cmbLanguage.SelectionChanged += OnLanguageChanged;
            Grid.SetColumn(cmbLanguage, 1);
            langGrid.Children.Add(cmbLanguage);

            TextBlock langHint = new TextBlock
            {
                Text = Localization.T("lang.hint"),
                Style = (Style)Application.Current.Resources["HintText"],
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(langHint, 2);
            langGrid.Children.Add(langHint);

            langCard.Child = langGrid;
            Grid.SetRow(langCard, 2);
            content.Children.Add(langCard);

            // --- 4. Графики 2×2 ---
            Grid chartsGrid = new Grid { Margin = new Thickness(0) };
            chartsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chartsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chartsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(330) });
            chartsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(330) });

            DepartmentBarChart deptChart = new DepartmentBarChart();
            DoughnutChart structureChart = new DoughnutChart();
            TrendChart trendChart = new TrendChart();
            HoursChart hoursChart = new HoursChart();

            Border card1 = CreateChartCard(Localization.T("chart.title1"), deptChart);
            Border card2 = CreateChartCard(Localization.T("chart.title2"), structureChart);
            Border card3 = CreateChartCard(Localization.T("chart.title3"), trendChart);
            Border card4 = CreateChartCard(Localization.T("chart.title4"), hoursChart);

            Grid.SetRow(card1, 0); Grid.SetColumn(card1, 0);
            Grid.SetRow(card2, 0); Grid.SetColumn(card2, 1);
            Grid.SetRow(card3, 1); Grid.SetColumn(card3, 0);
            Grid.SetRow(card4, 1); Grid.SetColumn(card4, 1);
            chartsGrid.Children.Add(card1);
            chartsGrid.Children.Add(card2);
            chartsGrid.Children.Add(card3);
            chartsGrid.Children.Add(card4);
            Grid.SetRow(chartsGrid, 3);
            content.Children.Add(chartsGrid);

            scroll.Content = content;
            pageHost.Children.Add(scroll);

            // Итоговая строка одна — закреплённая внизу окна (lblFooter).
            // Второй такой же строки под графиками больше нет.
            LoadCharts(deptChart, structureChart, trendChart, hoursChart, lblFooter);
        }

        // Печать дашборда: страница собирается из тех же графиков.
        // Печатаем текущую страницу «Главной» через системный диалог печати.
        private void OnPrintCharts(object sender, RoutedEventArgs e)
        {
            try
            {
                PrintDialog dialog = new PrintDialog();
                if (dialog.ShowDialog() != true) return;

                // Печатаем содержимое страницы, подобрав масштаб под лист
                FrameworkElement content = pageHost;
                double pageWidth = dialog.PrintableAreaWidth;
                double pageHeight = dialog.PrintableAreaHeight;

                double scale = Math.Min(pageWidth / Math.Max(1, content.ActualWidth),
                                        pageHeight / Math.Max(1, content.ActualHeight));
                if (scale <= 0 || double.IsInfinity(scale)) scale = 1;

                content.Measure(new Size(pageWidth, pageHeight));
                content.Arrange(new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                content.UpdateLayout();

                dialog.PrintVisual(content, string.Format("{0} — {1} {2} г.",
                    Localization.T("home.kpiFund"), MonthName(_selectedMonth), _selectedYear));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.error") + "\n" + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddStatCard(Grid host, int column, string title, string value, string caption)
        {
            Border card = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Margin = new Thickness(6)
            };
            StackPanel body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Res("TextSecondary")
            });
            body.Children.Add(new TextBlock
            {
                Text = value,
                Style = (Style)Application.Current.Resources["KpiValue"],
                Margin = new Thickness(0, 6, 0, 2)
            });
            body.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 10,
                Foreground = Res("TextSecondary"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            card.Child = body;

            Grid.SetColumn(card, column);
            host.Children.Add(card);
        }

        // Карточка графика: подпись сверху, сам график занимает всё оставшееся
        // место. Высоту задаём явно — StackPanel не растягивает дочерние элементы,
        // из-за чего Canvas получал нулевую высоту и ничего не рисовал.
        private Border CreateChartCard(string title, FrameworkElement chart)
        {
            Border card = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Margin = new Thickness(6)
            };

            Grid inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            TextBlock titleBlock = new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["CardTitle"],
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(titleBlock, 0);
            inner.Children.Add(titleBlock);

            chart.Height = double.NaN;      // растягиваем по ячейке
            chart.Margin = new Thickness(0); // без внешних отступов
            Grid.SetRow(chart, 1);
            inner.Children.Add(chart);

            card.Child = inner;
            return card;
        }

        private void LoadCharts(DepartmentBarChart deptChart, DoughnutChart structureChart,
                                TrendChart trendChart, HoursChart hoursChart, TextBlock footerText)
        {
            int month = _selectedMonth;
            int year = _selectedYear;
            string periodLabel = string.Format("{0} {1} г.", MonthName(month), year);

            try
            {
                var request = new NetworkMessage
                {
                    Command = "GET_ANALYTICS_FULL",
                    Data = string.Format("{0}|{1}", month, year)
                };
                NetworkMessage response = new ServerConnection().SendRequest(request);

                if (response == null || !response.IsSuccess)
                {
                    deptChart.ShowEmpty(Localization.T("chart.emptyMonth"));
                    structureChart.ShowEmpty(Localization.T("chart.emptyMonth"));
                    trendChart.ShowEmpty(Localization.T("chart.emptyYear"));
                    hoursChart.ShowEmpty(Localization.T("chart.emptyEmployees"));
                    footerText.Text = Localization.T("common.errorLoading") +
                                      (response == null ? string.Empty : response.ErrorMessage);
                    return;
                }

                AnalyticsDataDto data = JsonConvert.DeserializeObject<AnalyticsDataDto>(response.Data);
                if (data == null)
                {
                    deptChart.ShowEmpty(Localization.T("chart.emptyMonth"));
                    structureChart.ShowEmpty(Localization.T("chart.emptyMonth"));
                    trendChart.ShowEmpty(Localization.T("chart.emptyYear"));
                    hoursChart.ShowEmpty(Localization.T("chart.emptyEmployees"));
                    footerText.Text = Localization.T("chart.noData");
                    return;
                }

                // График 1: фонд по кафедрам
                List<BarItem> departments = new List<BarItem>();
                if (data.DepartmentPayroll != null)
                {
                    foreach (DataRow row in data.DepartmentPayroll.Rows)
                    {
                        string name = row.Table.Columns.Contains("Кафедра") && row["Кафедра"] != DBNull.Value
                            ? UiNames.CleanDepartment(row["Кафедра"].ToString())
                            : string.Empty;
                        double gross = ReadDouble(row, "Начислено");
                        double net = ReadDouble(row, "КВыплате");
                        departments.Add(new BarItem(name, net, gross));
                    }
                }
                deptChart.SetData(departments);
                if (departments.Count == 0) deptChart.ShowEmpty(Localization.T("chart.emptyMonth"));

                // График 2: структура фонда
                decimal grossTotal = data.TotalBaseSalary + data.TotalBonus + data.TotalSickVacationPay;
                List<SliceItem> slices = new List<SliceItem>();
                if (grossTotal > 0)
                {
                    slices.Add(new SliceItem
                    {
                        Name = Localization.T("chart.legendNet"),
                        Amount = (double)data.TotalNetSalary,
                        Fill = Res("ChartPrimary"),
                        Tooltip = string.Format("{0}: {1} р.", Localization.T("chart.legendNet"),
                            data.TotalNetSalary.ToString("N2"))
                    });
                    slices.Add(new SliceItem
                    {
                        Name = "НДФЛ (13%)",
                        Amount = (double)data.TotalTax,
                        Fill = Res("ChartSecondary"),
                        Tooltip = string.Format("НДФЛ (13%): {0} р.", data.TotalTax.ToString("N2"))
                    });
                    slices.Add(new SliceItem
                    {
                        Name = "ФСЗН (1%)",
                        Amount = (double)data.TotalFszen,
                        Fill = Res("ChartTertiary"),
                        Tooltip = string.Format("ФСЗН (1%): {0} р.", data.TotalFszen.ToString("N2"))
                    });
                    slices.Add(new SliceItem
                    {
                        Name = "Профсоюз (1%)",
                        Amount = (double)data.TotalUnion,
                        Fill = Res("ChartQuaternary"),
                        Tooltip = string.Format("Профсоюз (1%): {0} р.", data.TotalUnion.ToString("N2"))
                    });
                }
                structureChart.SetData(slices);
                if (slices.Count == 0) structureChart.ShowEmpty(Localization.T("chart.emptyMonth"));

                // График 3: годовая динамика
                double[] netByMonth = new double[12];
                double[] grossByMonth = new double[12];
                if (data.MonthlyTrend != null)
                {
                    foreach (DataRow row in data.MonthlyTrend.Rows)
                    {
                        int m = Convert.ToInt32(row["Month"]);
                        if (m < 1 || m > 12) continue;
                        grossByMonth[m - 1] = ReadDouble(row, "Начислено");
                        netByMonth[m - 1] = ReadDouble(row, "КВыплате");
                    }
                }
                trendChart.SetData(netByMonth, grossByMonth, month);

                // График 4: отработанные часы
                List<BarItem> hours = new List<BarItem>();
                decimal sumHours = 0m;
                decimal sumNorm = 0m;
                if (data.EmployeeHours != null)
                {
                    foreach (DataRow row in data.EmployeeHours.Rows)
                    {
                        string name = row.Table.Columns.Contains("ФИО") && row["ФИО"] != DBNull.Value
                            ? row["ФИО"].ToString()
                            : string.Empty;
                        double fact = ReadDouble(row, "ЧасыФакт");
                        double norm = ReadDouble(row, "ЧасыНорма");
                        sumHours += (decimal)fact;
                        sumNorm += (decimal)norm;
                        hours.Add(new BarItem(name, fact, norm));
                    }
                }
                hoursChart.SetData(hours);
                if (hours.Count == 0) hoursChart.ShowEmpty(Localization.T("chart.emptyEmployees"));

                // Итоговая строка
                if (data.PaidEmployeesCount > 0)
                {
                    decimal avgNet = Math.Round(data.TotalNetSalary / data.PaidEmployeesCount, 2);
                    int deptCount = data.DepartmentPayroll != null ? data.DepartmentPayroll.Rows.Count : 0;
                    _homeFooter = Localization.T("home.footerCharts",
                        periodLabel,
                        deptCount,
                        grossTotal.ToString("N2"),
                        (data.TotalTax + data.TotalFszen + data.TotalUnion).ToString("N2"),
                        data.TotalNetSalary.ToString("N2"),
                        avgNet.ToString("N2"));
                }
                else
                {
                    int empCount = data.EmployeeHours != null ? data.EmployeeHours.Rows.Count : 0;
                    _homeFooter = Localization.T("home.footerHours", periodLabel, empCount)
                        .Replace("|  Часы", string.Format("|  Часы ({0:0.#}/{1:0.#})", sumHours, sumNorm));
                }
                footerText.Text = _homeFooter;
            }
            catch (Exception ex)
            {
                deptChart.ShowEmpty(Localization.T("chart.emptyMonth"));
                structureChart.ShowEmpty(Localization.T("chart.emptyMonth"));
                trendChart.ShowEmpty(Localization.T("chart.emptyYear"));
                hoursChart.ShowEmpty(Localization.T("chart.emptyEmployees"));
                footerText.Text = Localization.T("common.errorNetwork") + ex.Message;
            }
        }

        private static double ReadDouble(DataRow row, string column)
        {
            if (row == null || !row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return 0;
            return (double)ParseNumber(row[column]);
        }

        // Разбор числа из JSON.
        // DataTable, собранная из JSON, приходит в виде строки, причём разделитель
        // дробной части зависит от культуры сервера: «30980,94» или «30980.94».
        // InvariantCulture читает запятую как разделитель РАЗРЯДОВ, поэтому
        // «30 980,94» превращалось в 3098094 — отсюда огромные подписи на графиках.
        internal static decimal ParseNumber(object raw)
        {
            if (raw == null || raw == DBNull.Value) return 0m;

            decimal result;
            if (raw is decimal) return (decimal)raw;
            if (raw is int) return (int)raw;
            if (raw is long) return (long)raw;
            if (raw is double) return (decimal)(double)raw;
            if (raw is float) return (decimal)(float)raw;

            string text = raw.ToString().Trim();
            if (text.Length == 0) return 0m;

            // Сначала «как есть» в текущей культуре
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out result)) return result;

            // Затем с явно заданным разделителем дробной части
            if (text.Contains(",") && !text.Contains("."))
            {
                if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out result)) return result;
            }
            else if (text.Contains(".") && !text.Contains(","))
            {
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out result)) return result;
            }
            else
            {
                // Есть и точка, и запятая: последняя из них — десятичная
                int lastComma = text.LastIndexOf(',');
                int lastDot = text.LastIndexOf('.');
                string normalized = lastComma > lastDot
                    ? text.Replace(".", string.Empty).Replace(',', '.')
                    : text.Replace(",", string.Empty);
                if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
                    return result;
            }

            return 0m;
        }

        private string PayoutStageText()
        {
            int day = DateTime.Today.Day;
            if (day < 15) return Localization.T("home.stageEarly");
            if (day < 25) return Localization.T("home.stageMiddle");
            return Localization.T("home.stageFinal");
        }

        private string MonthName(int month)
        {
            if (month < 1 || month > 12) month = 1;
            return Localization.T("month." + month);
        }

        private static int CountWorkingDaysLocal(int year, int month)
        {
            int total = DateTime.DaysInMonth(year, month);
            int working = 0;
            for (int day = 1; day <= total; day++)
            {
                DayOfWeek dow = new DateTime(year, month, day).DayOfWeek;
                if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday) working++;
            }
            return working;
        }

        private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null || combo.SelectedItem == null) return;
            ComboBoxItem item = combo.SelectedItem as ComboBoxItem;
            if (item == null) return;

            AppLanguage language = (AppLanguage)item.Tag;
            if (language == Localization.Current) return;

            Localization.Current = language;
            AppSettingsStore.SaveLanguage(Localization.Code);
            RebuildForLanguage();
        }

        // Пересобираем оболочку и открываемый экран уже на новом языке
        // После смены языка перестраиваем текущий экран целиком
        private void RebuildForLanguage()
        {
            ApplyLocalization();
            BuildPeriodSelectors();

            if (navEmployees.IsChecked == true) LoadEmployees();
            else if (navTimesheet.IsChecked == true) LoadTimesheet();
            else if (navPayroll.IsChecked == true) LoadPayroll();
            else if (navUsers.IsChecked == true) LoadUsers();
            else ShowHomeScreen();
        }

        // =====================================================================
        //  ЭКРАН 2: СОТРУДНИКИ
        // =====================================================================
        private void LoadEmployees()
        {
            SetHeader(Localization.T("emp.screen"), Localization.T("emp.subtitle"));
            ResetToolbar(showSearch: true, showPeriod: false);
            actionBar.Children.Clear();

            bool canManage = _userRole == "Администратор" || _userRole == "Бухгалтер";

            if (canManage)
            {
                AddAction(Localization.T("emp.add"), "PrimaryButton", OnAddEmployee);
                AddAction(Localization.T("emp.edit"), "OutlineButton", OnEditEmployee);
                AddAction(Localization.T("emp.fire"), "DangerButton", OnDeleteEmployee);
            }
            AddAction(Localization.T("emp.card"), "OutlineButton", OnShowEmployeeCard);

            Button btnRefresh = new Button
            {
                Content = Localization.T("common.refresh"),
                Style = (Style)Application.Current.Resources["OutlineButton"]
            };
            btnRefresh.MinWidth = ButtonMetrics.Measure(btnRefresh);
            btnRefresh.Click += (s, e) => LoadEmployees();
            actionBar.Children.Add(btnRefresh);

            // --- Таблица ---
            pageHost.Children.Clear();

            Border card = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(4)
            };

            DataGrid grid = new DataGrid
            {
                Style = (Style)Application.Current.Resources["AppDataGrid"]
            };
            _employeesGrid = grid;
            BuildEmployeeColumns(grid);

            grid.SelectionChanged += (s, e) => UpdateEmployeeFooter();
            grid.ContextMenu = BuildEmployeeContextMenu(canManage);

            // ПКМ по строке: сначала выделяем её, иначе действия меню
            // относятся к предыдущей выбранной строке.
            grid.PreviewMouseRightButtonDown += OnGridRightButtonDown;
            card.Child = grid;
            pageHost.Children.Add(card);

            try
            {
                NetworkMessage response = new ServerConnection().SendRequest(
                    new NetworkMessage { Command = "GET_EMPLOYEES" });

                if (response == null || !response.IsSuccess)
                {
                    lblFooter.Text = Localization.T("common.errorLoading") +
                                     (response == null ? string.Empty : response.ErrorMessage);
                    return;
                }

                _employees = JsonConvert.DeserializeObject<DataTable>(response.Data);
                if (_employees != null && _employees.Columns.Contains("Кафедра"))
                {
                    foreach (DataRow row in _employees.Rows)
                        if (row["Кафедра"] != DBNull.Value)
                            row["Кафедра"] = UiNames.CleanDepartment(row["Кафедра"].ToString());
                }

                _employeesView = _employees == null ? null : _employees.DefaultView;
                grid.ItemsSource = _employeesView;
                ApplyEmployeeFilter();
            }
            catch (Exception ex)
            {
                lblFooter.Text = Localization.T("common.errorNetwork") + ex.Message;
            }
        }

        // Контекстное меню таблицы (ПКМ по строке сотрудника).
        // Пункты те же, что и кнопки на панели, поэтому поведение одинаково.
        private ContextMenu BuildEmployeeContextMenu(bool canManage)
        {
            ContextMenu menu = new ContextMenu();

            if (canManage)
            {
                menu.Items.Add(CreateMenuItem(Localization.T("emp.edit"), OnEditEmployee));
                menu.Items.Add(CreateMenuItem(Localization.T("emp.card"), OnShowEmployeeCard));
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem(Localization.T("emp.fire"), OnDeleteEmployee, danger: true));
                menu.Items.Add(new Separator());
            }
            else
            {
                menu.Items.Add(CreateMenuItem(Localization.T("emp.card"), OnShowEmployeeCard));
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(CreateMenuItem(Localization.T("common.refresh"), (s, e) => LoadEmployees()));
            return menu;
        }

        private static MenuItem CreateMenuItem(string header, RoutedEventHandler click, bool danger = false)
        {
            MenuItem item = new MenuItem { Header = header, Padding = new Thickness(10, 4, 16, 4) };
            if (click != null) item.Click += click;
            if (danger) item.Foreground = Res("DangerText");
            return item;
        }

        // ПКМ по строке делает её выделенной — иначе меню действует на чужую запись
        private void OnGridRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DataGrid grid = sender as DataGrid;
            if (grid == null || grid.Items.Count == 0) return;

            System.Windows.Controls.DataGridRow row = FindParentRow(e.OriginalSource as DependencyObject);
            if (row == null) return; // клик по заголовку или пустому месту

            row.IsSelected = true;
            grid.CurrentItem = row.Item;
            row.Focus();
        }

        // Поиск строки таблицы выше по дереву элементов
        private static System.Windows.Controls.DataGridRow FindParentRow(DependencyObject source)
        {
            while (source != null)
            {
                System.Windows.Controls.DataGridRow row = source as System.Windows.Controls.DataGridRow;
                if (row != null) return row;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        private void BuildEmployeeColumns(DataGrid grid)
        {
            AddColumn(grid, "ФИО", 1.8);
            AddColumn(grid, "Кафедра", 1.5);
            AddColumn(grid, "Должность", 1.4, useTerm: true);
            AddColumn(grid, "Ставка", 0.6, converter: "Rate");
            AddColumn(grid, "Тип занятости", 1.3, useTerm: true);
            AddColumn(grid, "Стаж, лет", 0.6);
            AddColumn(grid, "Дата приёма", 0.9, dateFormat: "dd.MM.yyyy");
        }

        private static void AddColumn(DataGrid grid, string field, double width,
                                      bool useTerm = false, string converter = null, string dateFormat = null,
                                      bool numeric = false)
        {
            DataGridTextColumn column = new DataGridTextColumn
            {
                Header = Localization.T("col." + field),
                Binding = new System.Windows.Data.Binding("[" + field + "]"),
                Width = new DataGridLength(width, DataGridLengthUnitType.Star)
            };

            Style cellStyle = new Style(typeof(TextBlock));
            cellStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            cellStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            cellStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0)));

            if (useTerm)
            {
                System.Windows.Data.Binding binding = new System.Windows.Data.Binding("[" + field + "]");
                binding.Converter = (System.Windows.Data.IValueConverter)Application.Current.Resources["Term"];
                column.Binding = binding;
            }
            else if (converter != null)
            {
                System.Windows.Data.Binding binding = new System.Windows.Data.Binding("[" + field + "]");
                binding.Converter = (System.Windows.Data.IValueConverter)Application.Current.Resources["Rate"];
                column.Binding = binding;
            }
            else if (dateFormat != null)
            {
                System.Windows.Data.Binding binding = new System.Windows.Data.Binding("[" + field + "]") { StringFormat = dateFormat };
                column.Binding = binding;
            }
            else if (numeric)
            {
                // Числа из JSON приходят строками — приводим к виду «12,5»
                System.Windows.Data.Binding binding = new System.Windows.Data.Binding("[" + field + "]");
                binding.Converter = (System.Windows.Data.IValueConverter)Application.Current.Resources["Money"];
                column.Binding = binding;
                column.ElementStyle = NumericStyle();
            }

            column.ElementStyle = cellStyle;
            grid.Columns.Add(column);
        }

        // Числовые ячейки — по правому краю, как принято в таблицах
        private static Style NumericStyle()
        {
            Style style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0)));
            return style;
        }

        private void ApplyEmployeeFilter()
        {
            if (_employeesView == null) return;

            string query = txtSearch == null ? string.Empty : txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                _employeesView.RowFilter = string.Empty;
            }
            else
            {
                string safe = query.Replace("'", "''");
                _employeesView.RowFilter = string.Format("[ФИО] LIKE '%{0}%'", safe);
            }
            UpdateEmployeeFooter();
        }

        private void UpdateEmployeeFooter()
        {
            if (_employeesView == null)
            {
                lblFooter.Text = string.Empty;
                return;
            }

            DataTable table = _employees;
            int rows = _employeesView.Count;
            HashSet<string> people = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> departments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            decimal rates = 0m;
            int withBenefits = 0;

            foreach (DataRowView view in _employeesView)
            {
                DataRow row = view.Row;
                string name = row["ФИО"] == DBNull.Value ? string.Empty : row["ФИО"].ToString().Trim();
                people.Add(name);

                if (table.Columns.Contains("Кафедра") && row["Кафедра"] != DBNull.Value)
                    departments.Add(row["Кафедра"].ToString());

                if (table.Columns.Contains("Ставка") && row["Ставка"] != DBNull.Value)
                {
                    decimal rate;
                    if (decimal.TryParse(row["Ставка"].ToString(), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out rate))
                        rates += rate;
                }

                if (table.Columns.Contains("Льготы") && row["Льготы"] != DBNull.Value &&
                    row["Льготы"].ToString() != "Нет")
                    withBenefits++;
            }

            lblFooter.Text = string.Format(
                "{0}: {1}  |  {2}: {3}  |  {4}: {5:0.##}  |  {6}: {7}",
                Localization.T("common.employees"), rows,
                Localization.T("home.kpiDepts"), departments.Count,
                Localization.T("col.Ставка"), rates,
                Localization.T("col.Льготы"), withBenefits);
        }

        // --- Действия над сотрудниками ---
        private void OnAddEmployee(object sender, RoutedEventArgs e)
        {
            EmployeeEditorWindow editor = new EmployeeEditorWindow();
            if (editor.ShowDialog() == true) LoadEmployees();
        }

        private void OnEditEmployee(object sender, RoutedEventArgs e)
        {
            DataRow row = GetSelectedEmployee();
            if (row == null) return;

            EmployeeEditorWindow editor = new EmployeeEditorWindow(row);
            if (editor.ShowDialog() == true) LoadEmployees();
        }

        private void OnDeleteEmployee(object sender, RoutedEventArgs e)
        {
            DataRow row = GetSelectedEmployee();
            if (row == null) return;

            int id = Convert.ToInt32(row["EmployeeID"]);
            string name = row["ФИО"].ToString();
            string empType = Localization.Term(row["Тип занятости"] == DBNull.Value
                ? string.Empty
                : row["Тип занятости"].ToString());

            MessageBoxResult answer = MessageBox.Show(
                Localization.T("emp.confirmDelete", name, empType),
                Localization.T("emp.deleteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;

            NetworkMessage response = new ServerConnection().SendRequest(
                new NetworkMessage { Command = "DELETE_EMPLOYEE", Data = id.ToString(CultureInfo.InvariantCulture) });

            if (response != null && response.IsSuccess)
            {
                LoadEmployees();
                MessageBox.Show(Localization.T("emp.deleted"), Localization.T("emp.done"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(response == null ? Localization.T("common.error") : response.ErrorMessage,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnShowEmployeeCard(object sender, RoutedEventArgs e)
        {
            DataRow row = GetSelectedEmployee();
            if (row == null) return;

            string name = row["ФИО"].ToString();
            decimal totalRate = 0m;
            if (_employees != null && _employees.Columns.Contains("Ставка"))
            {
                foreach (DataRow r in _employees.Rows)
                {
                    if (string.Equals(r["ФИО"].ToString().Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)
                        && r["Ставка"] != DBNull.Value)
                    {
                        decimal rate;
                        if (decimal.TryParse(r["Ставка"].ToString(), NumberStyles.Any,
                                CultureInfo.InvariantCulture, out rate))
                            totalRate += rate;
                    }
                }
            }

            DateTime hireDate = DateTime.Today;
            if (_employees.Columns.Contains("Дата приёма") && row["Дата приёма"] != DBNull.Value)
                DateTime.TryParse(row["Дата приёма"].ToString(), out hireDate);

            new EmployeeCardWindow(row, totalRate, hireDate).ShowDialog();
        }

        private DataRow GetSelectedEmployee()
        {
            DataGrid grid = _employeesGrid;
            if (grid == null) return null;

            DataRowView view = grid.SelectedItem as DataRowView;
            if (view == null)
            {
                MessageBox.Show(Localization.T("emp.selectRecord"), Localization.T("common.attention"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            return view.Row;
        }

        // =====================================================================
        //  Навигация
        // =====================================================================
        private void OnNavHome(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            ShowHomeScreen();
        }

        private void OnNavEmployees(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            LoadEmployees();
        }

        private void OnNavTimesheet(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            LoadTimesheet();
        }

        private void OnNavPayroll(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            LoadPayroll();
        }

        private void OnNavUsers(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            LoadUsers();
        }

        // =====================================================================
        //  ЭКРАН 3: ТАБЕЛЬ
        // =====================================================================
        private void LoadTimesheet()
        {
            SetHeader(Localization.T("ts.screen"), Localization.T("ts.subtitle"));
            ResetToolbar(showSearch: true, showPeriod: true);
            actionBar.Children.Clear();

            Button btnImport = AddAction(Localization.T("ts.import"), "PrimaryButton", OnImportTimesheet);
            Button btnSave = AddAction(Localization.T("ts.saveManual"), "OutlineButton", OnSaveTimesheet);
            AddAction(Localization.T("ts.reset"), "DangerButton", OnClearTimesheet);
            AddAction(Localization.T("common.refresh"), "OutlineButton", (s, e) => LoadTimesheet());
            ButtonMetrics.AutoSize(new[] { btnImport, btnSave });

            pageHost.Children.Clear();

            Border card = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(4)
            };

            DataGrid grid = new DataGrid
            {
                Style = (Style)Application.Current.Resources["AppDataGrid"],
                IsReadOnly = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false
            };
            _timesheetGrid = grid;

            BuildTimesheetColumns(grid);
            grid.ContextMenu = BuildTimesheetContextMenu();
            grid.CellEditEnding += (s, e) => ScheduleTimesheetRecalc();
            card.Child = grid;
            pageHost.Children.Add(card);

            try
            {
                NetworkMessage response = new ServerConnection().SendRequest(new NetworkMessage
                {
                    Command = "GET_TIMESHEETS",
                    Data = string.Format("{0}|{1}", _selectedMonth, _selectedYear)
                });

                if (response == null || !response.IsSuccess)
                {
                    lblFooter.Text = Localization.T("common.errorLoading") +
                                     (response == null ? string.Empty : response.ErrorMessage);
                    return;
                }

                _timesheet = JsonConvert.DeserializeObject<DataTable>(response.Data);
                _timesheetView = _timesheet == null ? null : _timesheet.DefaultView;
                grid.ItemsSource = _timesheetView;
                ApplyTimesheetFilter();
            }
            catch (Exception ex)
            {
                lblFooter.Text = Localization.T("common.errorNetwork") + ex.Message;
            }
        }

        private void BuildTimesheetColumns(DataGrid grid)
        {
            grid.Columns.Clear();
            AddColumn(grid, "ФИО", 2.0);
            AddColumn(grid, "Отработано, дн.", 0.9, numeric: true);
            AddColumn(grid, "Норма, дн.", 0.9, numeric: true);
            AddColumn(grid, "Отработано, ч.", 0.9, numeric: true);
            AddColumn(grid, "Норма, ч.", 0.8, numeric: true);
            AddColumn(grid, "Больничный (Б), дн.", 0.9, numeric: true);
            AddColumn(grid, "Отпуск (О), дн.", 0.9, numeric: true);
            AddColumn(grid, "Статус", 1.1, useTerm: true);
        }

        private ContextMenu BuildTimesheetContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem(Localization.T("ts.import"), OnImportTimesheet));
            menu.Items.Add(CreateMenuItem(Localization.T("ts.saveManual"), OnSaveTimesheet));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(Localization.T("common.refresh"), (s, e) => LoadTimesheet()));
            return menu;
        }

        // Правки в табеле пересчитывают норму и итог в нижней строке
        private void ScheduleTimesheetRecalc()
        {
            Dispatcher.BeginInvoke(new Action(UpdateTimesheetFooter));
        }

        private void ApplyTimesheetFilter()
        {
            if (_timesheetView == null) return;

            string query = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query)) _timesheetView.RowFilter = string.Empty;
            else _timesheetView.RowFilter = string.Format("[ФИО] LIKE '%{0}%'", query.Replace("'", "''"));

            UpdateTimesheetFooter();
        }

        private void UpdateTimesheetFooter()
        {
            if (_timesheet == null || _timesheetView == null)
            {
                lblFooter.Text = string.Empty;
                return;
            }

            int rows = _timesheetView.Count;
            decimal days = 0m;
            decimal normDays = 0m;
            decimal hours = 0m;
            int sick = 0;
            int vacation = 0;

            foreach (DataRowView view in _timesheetView)
            {
                DataRow row = view.Row;
                days += ParseNumber(GetCell(row, "Отработано, дн."));
                normDays += ParseNumber(GetCell(row, "Норма, дн."));
                hours += ParseNumber(GetCell(row, "Отработано, ч."));
                sick += (int)ParseNumber(GetCell(row, "Больничный (Б), дн."));
                vacation += (int)ParseNumber(GetCell(row, "Отпуск (О), дн."));
            }

            lblFooter.Text = string.Format(
                "{0}: {1}  |  {2}: {3:0.##} / {4:0.##}  |  {5}: {6:0.#}  |  {7}: {8}  |  {9}: {10}",
                Localization.T("common.employees"), rows,
                Localization.T("ts.days"), days, normDays,
                Localization.T("ts.hours"), hours,
                Localization.T("ts.sick"), sick,
                Localization.T("ts.vacation"), vacation);
        }

        private static object GetCell(DataRow row, string column)
        {
            if (row == null || !row.Table.Columns.Contains(column)) return null;
            object value = row[column];
            return value == DBNull.Value ? null : value;
        }

        private void OnImportTimesheet(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Localization.T("ts.import"),
                Filter = "Excel (*.xlsx;*.xls)|*.xlsx;*.xls|Все файлы (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ExcelParseResult parsed = ExcelTimesheetService.ParseTimesheetFile(
                    dialog.FileName, _selectedMonth, _selectedYear);

                if (parsed == null || parsed.Entries == null || parsed.Entries.Count == 0)
                {
                    MessageBox.Show(parsed == null ? Localization.T("common.noData") : parsed.InfoMessage,
                        Localization.T("common.attention"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Файл может относиться к другому месяцу — предупредим
                if (parsed.DetectedMonth.HasValue && parsed.DetectedMonth.Value != _selectedMonth)
                {
                    string period = string.Format("{0} {1}", MonthName(parsed.DetectedMonth.Value),
                        parsed.DetectedYear ?? _selectedYear);
                    MessageBox.Show(Localization.T("ts.periodMismatch", period),
                        Localization.T("common.attention"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                TimesheetImportRequestDto request = new TimesheetImportRequestDto
                {
                    Month = _selectedMonth,
                    Year = _selectedYear,
                    Entries = parsed.Entries
                };

                NetworkMessage response = new ServerConnection().SendRequest(new NetworkMessage
                {
                    Command = "IMPORT_TIMESHETS",
                    Data = JsonConvert.SerializeObject(request)
                });

                if (response == null || !response.IsSuccess)
                {
                    MessageBox.Show(response == null ? Localization.T("common.errorNetwork") : response.ErrorMessage,
                        Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                TimesheetImportResultDto result =
                    JsonConvert.DeserializeObject<TimesheetImportResultDto>(response.Data);

                string text = result == null
                    ? Localization.T("ts.importOk")
                    : Localization.T("ts.importResult", result.SavedCount, result.TotalCount);

                if (result != null && result.UnmatchedNames != null && result.UnmatchedNames.Count > 0)
                    text += "\n\n" + Localization.T("ts.unmatched") + "\n• " +
                            string.Join("\n• ", result.UnmatchedNames.Take(10));

                MessageBox.Show(text, Localization.T("common.done"), MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadTimesheet();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.error") + "\n" + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSaveTimesheet(object sender, RoutedEventArgs e)
        {
            if (_timesheet == null || _timesheet.Rows.Count == 0)
            {
                MessageBox.Show(Localization.T("common.noData"), Localization.T("common.attention"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                List<TimesheetEntryDto> entries = new List<TimesheetEntryDto>();
                foreach (DataRow row in _timesheet.Rows)
                {
                    object idRaw = GetCell(row, "EmployeeID");
                    if (idRaw == null) continue;

                    entries.Add(new TimesheetEntryDto
                    {
                        EmployeeId = (int)ParseNumber(idRaw),
                        FullName = Convert.ToString(GetCell(row, "ФИО")),
                        Rate = ParseNumber(GetCell(row, "Rate")),
                        DaysWorked = (int)ParseNumber(GetCell(row, "Отработано, дн.")),
                        NormDays = (int)ParseNumber(GetCell(row, "Норма, дн.")),
                        HoursWorked = ParseNumber(GetCell(row, "Отработано, ч.")),
                        NormHours = ParseNumber(GetCell(row, "Норма, ч.")),
                        HasHours = true,
                        SickDays = (int)ParseNumber(GetCell(row, "Больничный (Б), дн.")),
                        VacationDays = (int)ParseNumber(GetCell(row, "Отпуск (О), дн."))
                    });
                }

                TimesheetImportRequestDto request = new TimesheetImportRequestDto
                {
                    Month = _selectedMonth,
                    Year = _selectedYear,
                    Entries = entries
                };

                NetworkMessage response = new ServerConnection().SendRequest(new NetworkMessage
                {
                    Command = "IMPORT_TIMESHETS",
                    Data = JsonConvert.SerializeObject(request)
                });

                if (response != null && response.IsSuccess)
                {
                    MessageBox.Show(Localization.T("ts.saved"), Localization.T("common.done"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadTimesheet();
                }
                else
                {
                    MessageBox.Show(response == null ? Localization.T("common.errorNetwork") : response.ErrorMessage,
                        Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.error") + "\n" + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnClearTimesheet(object sender, RoutedEventArgs e)
        {
            string period = string.Format("{0} {1} г.", MonthName(_selectedMonth), _selectedYear);
            if (MessageBox.Show(Localization.T("ts.confirmClear", period),
                    Localization.T("common.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                NetworkMessage response = new ServerConnection().SendRequest(new NetworkMessage
                {
                    Command = "CLEAR_TIMESHETS",
                    Data = string.Format("{0}|{1}", _selectedMonth, _selectedYear)
                });

                if (response != null && response.IsSuccess)
                {
                    MessageBox.Show(response.Data, Localization.T("common.done"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadTimesheet();
                }
                else
                {
                    MessageBox.Show(response == null ? Localization.T("common.errorNetwork") : response.ErrorMessage,
                        Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                lblFooter.Text = Localization.T("common.errorNetwork") + ex.Message;
            }
        }

        // =====================================================================
        //  ЭКРАН 4: РАСЧЁТ ЗАРАБОТНОЙ ПЛАТЫ
        // =====================================================================
        private void LoadPayroll()
        {
            SetHeader(Localization.T("payroll.screen"), Localization.T("payroll.subtitle"));
            ResetToolbar(showSearch: false, showPeriod: true);
            actionBar.Children.Clear();

            AddAction(Localization.T("payroll.calc"), "PrimaryButton", OnCalculatePayroll);
            AddAction(Localization.T("payroll.save"), "AccentButton", OnSavePayroll);
            AddAction(Localization.T("payroll.export"), "OutlineButton", OnExportPayroll);
            AddAction(Localization.T("payroll.payslip"), "OutlineButton", OnPayslipPayroll);
            AddAction(Localization.T("common.refresh"), "OutlineButton", (s, e) => LoadPayroll());

            pageHost.Children.Clear();
            pageHost.RowDefinitions.Clear();
            pageHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pageHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // --- Выбор этапа выплаты ---
            Border stageCard = new Border
            {
                Style = (Style)Application.Current.Resources["CardTight"],
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StackPanel stageRow = new StackPanel { Orientation = Orientation.Horizontal };
            stageRow.Children.Add(new TextBlock
            {
                Text = Localization.T("payroll.stage") + ":",
                Style = (Style)Application.Current.Resources["FieldLabel"],
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            RadioButton firstHalf = StageRadio(Localization.T("payroll.half1"), 1);
            RadioButton wholeMonth = StageRadio(Localization.T("payroll.halfAll"), 2);
            if (_payrollHalf == 1) firstHalf.IsChecked = true; else wholeMonth.IsChecked = true;
            stageRow.Children.Add(firstHalf);
            stageRow.Children.Add(wholeMonth);

            stageRow.Children.Add(new TextBlock
            {
                Text = string.Format(Localization.T("payroll.periodHint2"),
                    MonthName(_selectedMonth), _selectedYear,
                    _payrollHalf == 1 ? Localization.T("payroll.mode1") : Localization.T("payroll.modeAll")),
                Style = (Style)Application.Current.Resources["HintText"],
                Margin = new Thickness(20, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            stageCard.Child = stageRow;
            Grid.SetRow(stageCard, 0);
            pageHost.Children.Add(stageCard);

            // --- Таблица расчёта ---
            Border card = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(4)
            };

            DataGrid grid = new DataGrid
            {
                Style = (Style)Application.Current.Resources["AppDataGrid"]
            };
            _payrollGrid = grid;
            BuildPayrollColumns(grid);
            grid.ContextMenu = BuildPayrollContextMenu();
            card.Child = grid;
            Grid.SetRow(card, 1);
            pageHost.Children.Add(card);

            if (_payrollResult != null && _payrollView != null)
            {
                grid.ItemsSource = _payrollView;
                UpdatePayrollFooter();
            }
            else
            {
                lblFooter.Text = string.Format(Localization.T("payroll.periodHint"),
                    MonthName(_selectedMonth), _selectedYear);
            }
        }

        private RadioButton StageRadio(string text, int mode)
        {
            RadioButton radio = new RadioButton
            {
                Content = text,
                GroupName = "PayrollStage",
                FontSize = 12,
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = _payrollHalf == mode
            };
            radio.Checked += (s, e) =>
            {
                _payrollHalf = mode;
                OnCalculatePayroll(s, e);
            };
            return radio;
        }

        private void BuildPayrollColumns(DataGrid grid)
        {
            grid.Columns.Clear();
            AddColumn(grid, "ФИО", 1.7);

            if (_payrollHalf == 1)
            {
                // Аванс: только первая половина месяца
                AddColumn(grid, "Ставка", 0.55, converter: "Rate");
                AddColumn(grid, "Дни (1-я пол.), дн.", 0.8, numeric: true);
                AddColumn(grid, "Часы (1-я пол.), ч.", 0.8, numeric: true);
                AddColumn(grid, "Оклад (50%), руб.", 0.95, numeric: true);
                AddColumn(grid, "Надбавка (50%), руб.", 0.95, numeric: true);
                AddColumn(grid, "Аванс (1-я пол.), руб.", 1.0, numeric: true);
                AddColumn(grid, "Этап выплаты", 1.1, useTerm: true);
            }
            else
            {
                AddColumn(grid, "Ставка", 0.45, converter: "Rate");
                AddColumn(grid, "Дни, дн.", 0.65, numeric: true);
                AddColumn(grid, "Часы, ч.", 0.65, numeric: true);
                AddColumn(grid, "Б / О, дн.", 0.6, numeric: true);
                AddColumn(grid, "Оклад, руб.", 0.85, numeric: true);
                AddColumn(grid, "Надбавка, руб.", 0.8, numeric: true);
                AddColumn(grid, "Б/О, руб.", 0.7, numeric: true);
                AddColumn(grid, "Вычет, руб.", 0.7, numeric: true);
                AddColumn(grid, "НДФЛ, руб.", 0.7, numeric: true);
                AddColumn(grid, "ФСЗН, руб.", 0.7, numeric: true);
                AddColumn(grid, "Профс., руб.", 0.65, numeric: true);
                AddColumn(grid, "1-я пол., руб.", 0.8, numeric: true);
                AddColumn(grid, "2-я пол., руб.", 0.8, numeric: true);
                AddColumn(grid, "К выплате, руб.", 0.9, numeric: true);
            }
        }

        private ContextMenu BuildPayrollContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem(Localization.T("payroll.calc"), OnCalculatePayroll));
            menu.Items.Add(CreateMenuItem(Localization.T("payroll.save"), OnSavePayroll));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(Localization.T("payroll.export"), OnExportPayroll));
            menu.Items.Add(CreateMenuItem(Localization.T("payroll.payslip"), OnPayslipPayroll));
            return menu;
        }

        private void OnCalculatePayroll(object sender, RoutedEventArgs e)
        {
            try
            {
                NetworkMessage response = new ServerConnection().SendRequest(new NetworkMessage
                {
                    Command = "CALCULATE_PAYROLL",
                    Data = string.Format("{0}|{1}|{2}", _selectedMonth, _selectedYear, _payrollHalf)
                });

                if (response == null || !response.IsSuccess)
                {
                    lblFooter.Text = Localization.T("common.errorLoading") +
                                     (response == null ? string.Empty : response.ErrorMessage);
                    return;
                }

                _payrollResult = JsonConvert.DeserializeObject<DataTable>(response.Data);
                _payrollView = _payrollResult == null ? null : _payrollResult.DefaultView;

                BuildPayrollColumns(_payrollGrid);
                _payrollGrid.ItemsSource = _payrollView;
                UpdatePayrollFooter();
            }
            catch (Exception ex)
            {
                lblFooter.Text = Localization.T("common.errorNetwork") + ex.Message;
            }
        }

        private void UpdatePayrollFooter()
        {
            if (_payrollResult == null || _payrollView == null || _payrollView.Count == 0)
            {
                lblFooter.Text = string.Format(Localization.T("payroll.periodHint"),
                    MonthName(_selectedMonth), _selectedYear);
                return;
            }

            int count = _payrollView.Count;

            if (_payrollHalf == 1)
            {
                decimal advance = 0m;
                foreach (DataRowView view in _payrollView)
                    advance += ParseNumber(GetCell(view.Row, "Аванс (1-я пол.), руб."));

                lblFooter.Text = string.Format(Localization.T("payroll.totalsAdvance"),
                    count, advance.ToString("N2"));
            }
            else
            {
                decimal gross = 0m, tax = 0m, first = 0m, second = 0m, net = 0m;
                foreach (DataRowView view in _payrollView)
                {
                    DataRow row = view.Row;
                    decimal basePay = ParseNumber(GetCell(row, "Оклад, руб."));
                    decimal bonus = ParseNumber(GetCell(row, "Надбавка, руб."));
                    decimal sick = ParseNumber(GetCell(row, "Б/О, руб."));
                    gross += basePay + bonus + sick;
                    tax += ParseNumber(GetCell(row, "НДФЛ, руб.")) +
                           ParseNumber(GetCell(row, "ФСЗН, руб.")) +
                           ParseNumber(GetCell(row, "Профс., руб."));
                    first += ParseNumber(GetCell(row, "1-я пол., руб."));
                    second += ParseNumber(GetCell(row, "2-я пол., руб."));
                    net += ParseNumber(GetCell(row, "К выплате, руб."));
                }

                lblFooter.Text = string.Format(Localization.T("payroll.totalsFull"),
                    count, gross.ToString("N2"), tax.ToString("N2"),
                    first.ToString("N2"), second.ToString("N2"), net.ToString("N2"));
            }
        }

        private void OnSavePayroll(object sender, RoutedEventArgs e)
        {
            if (_payrollResult == null || _payrollResult.Rows.Count == 0)
            {
                MessageBox.Show(Localization.T("payroll.nothingToSave"), Localization.T("common.attention"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Формат сервера: месяц|год|json
                string payload = string.Format("{0}|{1}|{2}", _selectedMonth, _selectedYear,
                    JsonConvert.SerializeObject(_payrollResult));

                NetworkMessage response = new ServerConnection().SendRequest(new NetworkMessage
                {
                    Command = "SAVE_PAYROLL",
                    Data = payload
                });

                if (response != null && response.IsSuccess)
                {
                    MessageBox.Show(Localization.T("payroll.saved"), Localization.T("common.done"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(response == null ? Localization.T("common.errorNetwork") : response.ErrorMessage,
                        Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.error") + "\n" + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnExportPayroll(object sender, RoutedEventArgs e)
        {
            if (_payrollView == null || _payrollView.Count == 0)
            {
                MessageBox.Show(Localization.T("payroll.nothingToSave"), Localization.T("common.attention"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Localization.T("payroll.export"),
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = string.Format("Расчёт_{0}_{1}.xlsx", MonthName(_selectedMonth), _selectedYear)
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ExcelTimesheetService.ExportPayrollTable(_payrollResult, dialog.FileName);
                MessageBox.Show(Localization.T("payroll.exported"), Localization.T("common.done"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.error") + "\n" + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnPayslipPayroll(object sender, RoutedEventArgs e)
        {
            if (_payrollView == null || _payrollView.Count == 0)
            {
                MessageBox.Show(Localization.T("payroll.nothingToSave"), Localization.T("common.attention"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Нужен выбранный сотрудник — берём строку таблицы
            DataRowView selected = _payrollGrid.SelectedItem as DataRowView;
            if (selected == null)
            {
                MessageBox.Show(Localization.T("payroll.selectEmployee"), Localization.T("common.attention"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Localization.T("payroll.payslip"),
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = string.Format("Листок_{0}.xlsx",
                    Convert.ToString(GetCell(selected.Row, "ФИО")))
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ExcelTimesheetService.ExportPayslip(selected.Row, _selectedMonth, _selectedYear,
                    _payrollHalf, dialog.FileName);
                MessageBox.Show(Localization.T("payroll.exported"), Localization.T("common.done"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.error") + "\n" + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =====================================================================
        //  ЭКРАН 5: ПОЛЬЗОВАТЕЛИ
        // =====================================================================
        private void LoadUsers()
        {
            SetHeader(Localization.T("users.screen"), Localization.T("users.subtitle"));
            ResetToolbar(showSearch: true, showPeriod: false);
            actionBar.Children.Clear();

            AddAction(Localization.T("users.add"), "PrimaryButton", OnAddUser);
            AddAction(Localization.T("users.delete"), "DangerButton", OnDeleteUser);
            AddAction(Localization.T("common.refresh"), "OutlineButton", (s, e) => LoadUsers());

            pageHost.Children.Clear();

            Border card = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Padding = new Thickness(4)
            };

            DataGrid grid = new DataGrid
            {
                Style = (Style)Application.Current.Resources["AppDataGrid"]
            };
            _usersGrid = grid;
            grid.Columns.Clear();
            AddColumn(grid, "Логин", 1.4);
            AddColumn(grid, "Роль", 1.4, useTerm: true);
            grid.ContextMenu = BuildUsersContextMenu();
            grid.SelectionChanged += (s, e) => UpdateUsersFooter();
            card.Child = grid;
            pageHost.Children.Add(card);

            try
            {
                NetworkMessage response = new ServerConnection().SendRequest(
                    new NetworkMessage { Command = "GET_USERS" });

                if (response == null || !response.IsSuccess)
                {
                    lblFooter.Text = Localization.T("common.errorLoading") +
                                     (response == null ? string.Empty : response.ErrorMessage);
                    return;
                }

                _users = JsonConvert.DeserializeObject<DataTable>(response.Data);
                _usersView = _users == null ? null : _users.DefaultView;
                grid.ItemsSource = _usersView;
                ApplyUsersFilter();
            }
            catch (Exception ex)
            {
                lblFooter.Text = Localization.T("common.errorNetwork") + ex.Message;
            }
        }

        private ContextMenu BuildUsersContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem(Localization.T("users.add"), OnAddUser));
            menu.Items.Add(CreateMenuItem(Localization.T("users.delete"), OnDeleteUser, danger: true));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(Localization.T("common.refresh"), (s, e) => LoadUsers()));
            return menu;
        }

        private void ApplyUsersFilter()
        {
            if (_usersView == null) return;

            string query = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query)) _usersView.RowFilter = string.Empty;
            else _usersView.RowFilter = string.Format("[Логин] LIKE '%{0}%'", query.Replace("'", "''"));

            UpdateUsersFooter();
        }

        private void UpdateUsersFooter()
        {
            if (_usersView == null)
            {
                lblFooter.Text = string.Empty;
                return;
            }

            int total = _usersView.Count;
            int admins = 0, accountants = 0, economists = 0;

            foreach (DataRowView view in _usersView)
            {
                object roleRaw = GetCell(view.Row, "Роль");
                string role = roleRaw == null ? string.Empty : roleRaw.ToString().Trim();
                if (role == "Администратор") admins++;
                else if (role == "Бухгалтер") accountants++;
                else if (role == "Экономист") economists++;
            }

            lblFooter.Text = string.Format(
                "{0}: {1}  |  {2}: {3}  |  {4}: {5}  |  {6}: {7}",
                Localization.T("users.total"), total,
                Localization.T("users.admins"), admins,
                Localization.T("users.accountants"), accountants,
                Localization.T("users.economists"), economists);
        }

        private void OnAddUser(object sender, RoutedEventArgs e)
        {
            UserEditorWindow editor = new UserEditorWindow();
            if (editor.ShowDialog() != true) return;

            try
            {
                NetworkMessage response = new ServerConnection().SendRequest(new NetworkMessage
                {
                    Command = "ADD_USER",
                    Data = string.Format("{0}|{1}|{2}", editor.Login, editor.Password, editor.RoleId)
                });

                if (response != null && response.IsSuccess)
                {
                    LoadUsers();
                }
                else
                {
                    MessageBox.Show(response == null ? Localization.T("common.errorNetwork") : response.ErrorMessage,
                        Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.errorNetwork") + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeleteUser(object sender, RoutedEventArgs e)
        {
            DataRowView selected = _usersGrid == null ? null : _usersGrid.SelectedItem as DataRowView;
            if (selected == null)
            {
                MessageBox.Show(Localization.T("users.selectUser"), Localization.T("common.attention"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            object idRaw = GetCell(selected.Row, "UserID");
            if (idRaw == null) return;
            int userId = (int)ParseNumber(idRaw);

            string login = Convert.ToString(GetCell(selected.Row, "Логин"));
            if (MessageBox.Show(Localization.T("users.confirmDelete", login),
                    Localization.T("common.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                NetworkMessage response = new ServerConnection().SendRequest(new NetworkMessage
                {
                    Command = "DELETE_USER",
                    Data = string.Format("{0}|{1}", userId, _currentUserId)
                });

                if (response != null && response.IsSuccess)
                {
                    LoadUsers();
                }
                else
                {
                    MessageBox.Show(response == null ? Localization.T("common.errorNetwork") : response.ErrorMessage,
                        Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T("common.errorNetwork") + ex.Message,
                    Localization.T("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Заглушка для экранов, которые переедут в WPF на следующем шаге
        private void ShowPlaceholder(string title, string subtitle)
        {
            SetHeader(title, subtitle);
            ResetToolbar(showSearch: false, showPeriod: false);
            pageHost.Children.Clear();

            Border card = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                VerticalAlignment = VerticalAlignment.Top
            };
            StackPanel body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = Localization.T("common.noData"),
                Style = (Style)Application.Current.Resources["CardTitle"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 6)
            });
            body.Children.Add(new TextBlock
            {
                Text = "Этот раздел переносится в WPF на следующем шаге.",
                Style = (Style)Application.Current.Resources["HintText"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 30)
            });
            card.Child = body;
            pageHost.Children.Add(card);

            lblFooter.Text = string.Empty;
        }

        private void OnLogoutClick(object sender, RoutedEventArgs e)
        {
            LoginWindow login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }
    }

    // Небольшой помощник для названий кафедр (перенесён из старой дизайн-системы)
    public static class UiNames
    {
        public static string CleanDepartment(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;

            string trimmed = rawName.Trim();
            const string prefix = "кафедра ";
            if (trimmed.Length > prefix.Length &&
                trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string rest = trimmed.Substring(prefix.Length).TrimStart();
                if (rest.Length == 0) return trimmed;
                if (rest.Length == 1) return rest.ToUpperInvariant();
                return char.ToUpper(rest[0]) + rest.Substring(1);
            }
            return trimmed;
        }
    }
}

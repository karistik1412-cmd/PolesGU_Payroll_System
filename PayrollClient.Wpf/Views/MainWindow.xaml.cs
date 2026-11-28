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

            lblMonthCaption.Text = Localization.T("common.month") + ":";
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
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (navEmployees.IsChecked == true) ApplyEmployeeFilter();
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
            periodPanel.Visibility = showPeriod ? Visibility.Visible : Visibility.Collapsed;
            txtSearch.Visibility = showSearch ? Visibility.Visible : Visibility.Collapsed;
            lblSearch.Visibility = showSearch ? Visibility.Visible : Visibility.Collapsed;
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
            AddAction(Localization.T("chart.print"), "OutlineButton", null);

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
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // итог

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

            DepartmentBarChart deptChart;
            DoughnutChart structureChart;
            TrendChart trendChart;
            HoursChart hoursChart;

            Border card1 = CreateChartCard(Localization.T("chart.title1"), out deptChart);
            Border card2 = CreateChartCard(Localization.T("chart.title2"), out structureChart);
            Border card3 = CreateChartCard(Localization.T("chart.title3"), out trendChart);
            Border card4 = CreateChartCard(Localization.T("chart.title4"), out hoursChart);

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

            // --- 5. Итоговая строка ПОД графиками ---
            Border footerCard = new Border
            {
                Style = (Style)Application.Current.Resources["Card"],
                Margin = new Thickness(6, 6, 6, 6),
                Padding = new Thickness(18, 12, 18, 12)
            };
            TextBlock footerText = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("TextPrimary"),
                TextWrapping = TextWrapping.Wrap
            };
            footerCard.Child = footerText;
            Grid.SetRow(footerCard, 4);
            content.Children.Add(footerCard);

            scroll.Content = content;
            pageHost.Children.Add(scroll);

            LoadCharts(deptChart, structureChart, trendChart, hoursChart, footerText);
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

        private Border CreateChartCard(string title, out DepartmentBarChart chart)
        {
            StackPanel body;
            Border card = CreateCard(title, null, out body);
            DepartmentBarChart typed = new DepartmentBarChart();
            body.Children.Add(typed);
            chart = typed;
            card.Margin = new Thickness(6);
            return card;
        }

        private Border CreateChartCard(string title, out DoughnutChart chart)
        {
            StackPanel body;
            Border card = CreateCard(title, null, out body);
            DoughnutChart typed = new DoughnutChart();
            body.Children.Add(typed);
            chart = typed;
            return card;
        }

        private Border CreateChartCard(string title, out TrendChart chart)
        {
            StackPanel body;
            Border card = CreateCard(title, null, out body);
            TrendChart typed = new TrendChart();
            body.Children.Add(typed);
            chart = typed;
            return card;
        }

        private Border CreateChartCard(string title, out HoursChart chart)
        {
            StackPanel body;
            Border card = CreateCard(title, null, out body);
            HoursChart typed = new HoursChart();
            body.Children.Add(typed);
            chart = typed;
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
                lblFooter.Text = _homeFooter;
            }
            catch (Exception ex)
            {
                deptChart.ShowEmpty(Localization.T("chart.emptyMonth"));
                structureChart.ShowEmpty(Localization.T("chart.emptyMonth"));
                trendChart.ShowEmpty(Localization.T("chart.emptyYear"));
                hoursChart.ShowEmpty(Localization.T("chart.emptyEmployees"));
                footerText.Text = Localization.T("common.errorNetwork") + ex.Message;
                lblFooter.Text = footerText.Text;
            }
        }

        private static double ReadDouble(DataRow row, string column)
        {
            if (row == null || !row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return 0;
            decimal value;
            if (decimal.TryParse(row[column].ToString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out value))
                return (double)value;
            return 0;
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
        private void RebuildForLanguage()
        {
            ApplyLocalization();
            BuildPeriodSelectors();

            if (navEmployees.IsChecked == true) LoadEmployees();
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
                                      bool useTerm = false, string converter = null, string dateFormat = null)
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

            column.ElementStyle = cellStyle;
            grid.Columns.Add(column);
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
            ShowPlaceholder(Localization.T("ts.screen"), Localization.T("ts.subtitle"));
        }

        private void OnNavPayroll(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            ShowPlaceholder(Localization.T("payroll.screen"), Localization.T("payroll.subtitle"));
        }

        private void OnNavUsers(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            ShowPlaceholder(Localization.T("users.screen"), Localization.T("users.subtitle"));
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

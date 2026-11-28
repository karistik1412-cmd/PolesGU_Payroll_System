using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PayrollClient.Services;

namespace PayrollClient.Controls
{
    // Точка столбчатого графика: подпись + два значения (факт и сравнение)
    public class BarItem
    {
        public string Label { get; set; }
        public double Primary { get; set; }
        public double Secondary { get; set; }

        public BarItem() { }

        public BarItem(string label, double primary, double secondary = 0)
        {
            Label = label;
            Primary = primary;
            Secondary = secondary;
        }
    }

    // Доля в кольцевой диаграмме
    public class SliceItem
    {
        public string Name { get; set; }
        public double Amount { get; set; }
        public Brush Fill { get; set; }
        public string Tooltip { get; set; }
    }

    // =====================================================================
    // График 1: фонд оплаты труда по кафедрам — горизонтальные столбцы
    // (верхний — «Начислено», нижний — «К выплате», сумма подписана справа).
    // =====================================================================
    public class DepartmentBarChart : Grid
    {
        private readonly Canvas _canvas = new Canvas();
        private readonly TextBlock _empty;
        private List<BarItem> _items = new List<BarItem>();

        public DepartmentBarChart()
        {
            Children.Add(_canvas);
            _empty = CreateEmptyLabel();
            Children.Add(_empty);
            SizeChanged += (s, e) => Redraw();
        }

        public static TextBlock CreateEmptyLabel()
        {
            return new TextBlock
            {
                Text = Localization.T("chart.noData"),
                Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24, 0, 24, 0),
                Visibility = Visibility.Collapsed
            };
        }

        public void SetData(IEnumerable<BarItem> items)
        {
            _items = items == null ? new List<BarItem>() : items.ToList();
            Redraw();
        }

        // Заглушка с пояснением, почему график пустой
        public void ShowEmpty(string message)
        {
            _items = new List<BarItem>();
            if (!string.IsNullOrWhiteSpace(message)) _empty.Text = message;
            _empty.Visibility = Visibility.Visible;
            _canvas.Children.Clear();
            _canvas.Visibility = Visibility.Collapsed;
        }

        private void Redraw()
        {
            _canvas.Children.Clear();
            bool hasData = _items.Count > 0;
            _empty.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            _canvas.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            if (!hasData) return;

            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 40 || height < 40) return;

            Brush muted = (Brush)Application.Current.Resources["ChartMuted"];
            Brush mutedBorder = (Brush)Application.Current.Resources["Border"];
            Brush primary = (Brush)Application.Current.Resources["ChartPrimary"];
            Brush textBrush = (Brush)Application.Current.Resources["TextPrimary"];
            Brush dimBrush = (Brush)Application.Current.Resources["TextSecondary"];

            double leftLabels = Math.Min(width * 0.36, 190);
            double rightLabels = 64;
            double plotWidth = Math.Max(20, width - leftLabels - rightLabels - 6);
            double maxValue = _items.Max(i => Math.Max(i.Primary, i.Secondary));
            if (maxValue <= 0) maxValue = 1;

            int rows = _items.Count;
            double rowHeight = height / Math.Max(1, rows);

            for (int r = 0; r < rows; r++)
            {
                BarItem item = _items[r];
                double rowTop = r * rowHeight;

                // Подпись кафедры слева (обрезаем длинные названия многоточием)
                TextBlock label = new TextBlock
                {
                    Text = Shorten(item.Label, 24),
                    FontSize = 11,
                    Foreground = dimBrush,
                    TextAlignment = TextAlignment.Right,
                    Width = leftLabels - 10,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, rowTop + rowHeight / 2 - 9);
                _canvas.Children.Add(label);

                double barHeight = Math.Min(13, Math.Max(7, rowHeight / 3.2));
                double grossTop = rowTop + rowHeight / 2 - barHeight - 1;
                double netTop = rowTop + rowHeight / 2 + 1;

                AddBar(leftLabels, grossTop, plotWidth * (item.Secondary / maxValue), barHeight, muted, mutedBorder);
                AddBar(leftLabels, netTop, plotWidth * (item.Primary / maxValue), barHeight, primary, primary);

                // Подпись суммы у полосы «К выплате»
                TextBlock netValue = new TextBlock
                {
                    Text = item.Primary.ToString("N0"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = textBrush
                };
                Canvas.SetLeft(netValue, leftLabels + plotWidth * (item.Primary / maxValue) + 6);
                Canvas.SetTop(netValue, netTop - 2);
                _canvas.Children.Add(netValue);

                TextBlock grossValue = new TextBlock
                {
                    Text = item.Secondary.ToString("N0"),
                    FontSize = 10,
                    Foreground = dimBrush
                };
                Canvas.SetLeft(grossValue, leftLabels + plotWidth * (item.Secondary / maxValue) + 6);
                Canvas.SetTop(grossValue, grossTop - 2);
                _canvas.Children.Add(grossValue);
            }
        }

        private void AddBar(double x, double y, double w, double h, Brush fill, Brush stroke)
        {
            if (w <= 0 || h <= 0) return;
            Rectangle rect = new Rectangle
            {
                Width = Math.Max(1, w),
                Height = h,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 0.6,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            _canvas.Children.Add(rect);
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }
    }

    // =====================================================================
    // График 2: структура фонда — кольцевая диаграмма с легендой справа
    // =====================================================================
    public class DoughnutChart : Grid
    {
        private readonly Canvas _canvas = new Canvas();
        private readonly TextBlock _empty;
        private List<SliceItem> _slices = new List<SliceItem>();
        private double _total;

        public DoughnutChart()
        {
            Children.Add(_canvas);
            _empty = DepartmentBarChart.CreateEmptyLabel();
            Children.Add(_empty);
            SizeChanged += (s, e) => Redraw();
        }

        public void SetData(IEnumerable<SliceItem> slices)
        {
            _slices = slices == null ? new List<SliceItem>() : slices.Where(s => s.Amount > 0).ToList();
            _total = _slices.Sum(s => s.Amount);
            Redraw();
        }

        public void ShowEmpty(string message)
        {
            _slices = new List<SliceItem>();
            _total = 0;
            if (!string.IsNullOrWhiteSpace(message)) _empty.Text = message;
            _empty.Visibility = Visibility.Visible;
            _canvas.Children.Clear();
            _canvas.Visibility = Visibility.Collapsed;
        }

        private void Redraw()
        {
            _canvas.Children.Clear();
            bool hasData = _slices.Count > 0 && _total > 0;
            _empty.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            _canvas.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            if (!hasData) return;

            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 60 || height < 60) return;

            double legendWidth = Math.Min(width * 0.46, 230);
            double size = Math.Min(height, width - legendWidth) - 8;
            if (size < 40) size = Math.Min(height, width) - 8;
            if (size < 20) return;

            double cx = size / 2 + 4;
            double cy = height / 2;
            double thickness = Math.Max(12, size * 0.17);
            double outer = size / 2;
            double inner = Math.Max(2, outer - thickness);

            double angle = -90;
            foreach (SliceItem slice in _slices)
            {
                double sweep = slice.Amount / _total * 360.0;
                AddArc(cx, cy, outer, inner, angle, sweep, slice.Fill, slice.Tooltip);
                angle += sweep;
            }

            // Подпись в центре кольца: общая сумма
            TextBlock center = new TextBlock
            {
                Text = FormatShort(_total),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextPrimary"],
                TextAlignment = TextAlignment.Center
            };
            center.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(center, cx - center.DesiredSize.Width / 2);
            Canvas.SetTop(center, cy - center.DesiredSize.Height / 2);
            _canvas.Children.Add(center);

            // Легенда
            double legendX = size + 22;
            double legendY = Math.Max(6, cy - (_slices.Count * 26) / 2);
            foreach (SliceItem slice in _slices)
            {
                Rectangle swatch = new Rectangle
                {
                    Width = 10,
                    Height = 10,
                    Fill = slice.Fill,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(swatch, legendX);
                Canvas.SetTop(swatch, legendY + 3);
                _canvas.Children.Add(swatch);

                double pct = slice.Amount / _total * 100.0;
                TextBlock line = new TextBlock
                {
                    Text = string.Format("{0}: {1} р. ({2:0.#}%)", slice.Name, slice.Amount.ToString("N2"), pct),
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextPrimary"],
                    Width = Math.Max(60, legendWidth - 16),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(line, legendX + 16);
                Canvas.SetTop(line, legendY);
                _canvas.Children.Add(line);

                if (!string.IsNullOrEmpty(slice.Tooltip)) line.ToolTip = slice.Tooltip;
                legendY += 26;
            }
        }

        private void AddArc(double cx, double cy, double outer, double inner,
                            double startAngle, double sweep, Brush fill, string tooltip)
        {
            if (sweep <= 0.01) return;
            if (sweep >= 359.99)
            {
                Ellipse full = new Ellipse { Width = outer * 2, Height = outer * 2, Fill = fill };
                Canvas.SetLeft(full, cx - outer);
                Canvas.SetTop(full, cy - outer);
                if (!string.IsNullOrEmpty(tooltip)) full.ToolTip = tooltip;
                _canvas.Children.Add(full);

                Ellipse hole = new Ellipse
                {
                    Width = inner * 2,
                    Height = inner * 2,
                    Fill = (Brush)Application.Current.Resources["CardBg"]
                };
                Canvas.SetLeft(hole, cx - inner);
                Canvas.SetTop(hole, cy - inner);
                _canvas.Children.Add(hole);
                return;
            }

            Point p1 = PointOnCircle(cx, cy, outer, startAngle);
            Point p2 = PointOnCircle(cx, cy, outer, startAngle + sweep);
            Point p3 = PointOnCircle(cx, cy, inner, startAngle + sweep);
            Point p4 = PointOnCircle(cx, cy, inner, startAngle);

            bool largeArc = sweep > 180;
            PathFigure figure = new PathFigure { StartPoint = p1, IsClosed = true };
            figure.Segments.Add(new ArcSegment(p2, new Size(outer, outer), 0, largeArc, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(p3, true));
            figure.Segments.Add(new ArcSegment(p4, new Size(inner, inner), 0, largeArc, SweepDirection.Counterclockwise, true));

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            Path path = new Path
            {
                Data = geometry,
                Fill = fill,
                Stroke = (Brush)Application.Current.Resources["CardBg"],
                StrokeThickness = 1.5
            };
            if (!string.IsNullOrEmpty(tooltip)) path.ToolTip = tooltip;
            _canvas.Children.Add(path);
        }

        private static Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            return new Point(cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad));
        }

        private static string FormatShort(double value)
        {
            if (value >= 1000000) return (value / 1000000.0).ToString("0.#") + " млн";
            if (value >= 1000) return (value / 1000.0).ToString("0.#") + " тыс.";
            return value.ToString("N0");
        }
    }

    // =====================================================================
    // График 3: динамика за год — плавная линия «К выплате» + линия «Начислено»
    // =====================================================================
    public class TrendChart : Grid
    {
        private readonly Canvas _canvas = new Canvas();
        private readonly TextBlock _empty;
        private double[] _net = new double[12];
        private double[] _gross = new double[12];
        private int _currentMonth;

        public TrendChart()
        {
            Children.Add(_canvas);
            _empty = DepartmentBarChart.CreateEmptyLabel();
            Children.Add(_empty);
            SizeChanged += (s, e) => Redraw();
        }

        public void SetData(double[] net, double[] gross, int currentMonth)
        {
            _net = net ?? new double[12];
            _gross = gross ?? new double[12];
            _currentMonth = currentMonth;
            Redraw();
        }

        public void ShowEmpty(string message)
        {
            _net = new double[12];
            _gross = new double[12];
            if (!string.IsNullOrWhiteSpace(message)) _empty.Text = message;
            _empty.Visibility = Visibility.Visible;
            _canvas.Children.Clear();
            _canvas.Visibility = Visibility.Collapsed;
        }

        private void Redraw()
        {
            _canvas.Children.Clear();
            bool hasData = _net.Any(v => v > 0) || _gross.Any(v => v > 0);
            _empty.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            _canvas.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            if (!hasData) return;

            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 80 || height < 60) return;

            Brush grid = (Brush)Application.Current.Resources["GridLine"];
            Brush primary = (Brush)Application.Current.Resources["ChartPrimary"];
            Brush secondary = (Brush)Application.Current.Resources["ChartSecondary"];
            Brush dim = (Brush)Application.Current.Resources["TextSecondary"];

            double left = 52;
            double right = 12;
            double top = 12;
            double bottom = 26;
            double plotW = Math.Max(20, width - left - right);
            double plotH = Math.Max(20, height - top - bottom);

            double max = Math.Max(_gross.Max(), _net.Max());
            if (max <= 0) max = 1;
            // Округляем верхнюю границу до «красивого» значения
            double step = NiceStep(max / 4.0);
            double maxAxis = Math.Ceiling(max / step) * step;

            // Горизонтальная сетка + подписи оси Y
            for (double v = 0; v <= maxAxis + 0.001; v += step)
            {
                double y = top + plotH - (v / maxAxis) * plotH;
                Line line = new Line
                {
                    X1 = left, X2 = left + plotW, Y1 = y, Y2 = y,
                    Stroke = grid, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }
                };
                _canvas.Children.Add(line);

                TextBlock valueLabel = new TextBlock
                {
                    Text = v.ToString("N0"),
                    FontSize = 9.5,
                    Foreground = dim,
                    Width = 46,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(valueLabel, 0);
                Canvas.SetTop(valueLabel, y - 8);
                _canvas.Children.Add(valueLabel);
            }

            // Подписи месяцев
            string[] months =
            {
                "Янв", "Фев", "Мар", "Апр", "Май", "Июн",
                "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"
            };
            double slot = plotW / 12.0;
            for (int i = 0; i < 12; i++)
            {
                TextBlock monthLabel = new TextBlock
                {
                    Text = months[i],
                    FontSize = 9.5,
                    Foreground = dim,
                    Width = slot,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(monthLabel, left + slot * i);
                Canvas.SetTop(monthLabel, top + plotH + 4);
                _canvas.Children.Add(monthLabel);
            }

            // Заливка под линией «К выплате»
            PointCollection netPoints = new PointCollection();
            PointCollection grossPoints = new PointCollection();
            for (int i = 0; i < 12; i++)
            {
                double x = left + slot * i + slot / 2;
                netPoints.Add(new Point(x, top + plotH - (_net[i] / maxAxis) * plotH));
                grossPoints.Add(new Point(x, top + plotH - (_gross[i] / maxAxis) * plotH));
            }

            PathGeometry areaGeometry = new PathGeometry();
            PathFigure areaFigure = new PathFigure
            {
                StartPoint = new Point(netPoints[0].X, top + plotH),
                IsClosed = true
            };
            foreach (Point p in netPoints) areaFigure.Segments.Add(new LineSegment(p, true));
            areaFigure.Segments.Add(new LineSegment(new Point(netPoints[11].X, top + plotH), true));
            areaGeometry.Figures.Add(areaFigure);

            Path area = new Path
            {
                Data = areaGeometry,
                Fill = new SolidColorBrush(Color.FromArgb(40, 62, 114, 87)),
                Stroke = null
            };
            _canvas.Children.Add(area);

            _canvas.Children.Add(CreateLine(netPoints, primary, 2.4));
            _canvas.Children.Add(CreateLine(grossPoints, secondary, 1.6));

            // Точка текущего месяца
            if (_currentMonth >= 1 && _currentMonth <= 12)
            {
                Point current = netPoints[_currentMonth - 1];
                Ellipse marker = new Ellipse
                {
                    Width = 9,
                    Height = 9,
                    Fill = primary,
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(marker, current.X - 4.5);
                Canvas.SetTop(marker, current.Y - 4.5);
                marker.ToolTip = string.Format("{0}: {1} р.", months[_currentMonth - 1], _net[_currentMonth - 1].ToString("N2"));
                _canvas.Children.Add(marker);

                TextBlock value = new TextBlock
                {
                    Text = _net[_currentMonth - 1].ToString("N0"),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = primary
                };
                value.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(value, Math.Min(current.X - value.DesiredSize.Width / 2, left + plotW - value.DesiredSize.Width));
                Canvas.SetTop(value, current.Y - 20);
                _canvas.Children.Add(value);
            }
        }

        private static Path CreateLine(PointCollection points, Brush brush, double thickness)
        {
            PathFigure figure = new PathFigure { StartPoint = points[0], IsClosed = false };
            for (int i = 1; i < points.Count; i++)
                figure.Segments.Add(new LineSegment(points[i], true));

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return new Path
            {
                Data = geometry,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round
            };
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double normalized = raw / magnitude;
            double nice;
            if (normalized <= 1) nice = 1;
            else if (normalized <= 2) nice = 2;
            else if (normalized <= 5) nice = 5;
            else nice = 10;
            return nice * magnitude;
        }
    }

    // =====================================================================
    // График 4: отработанные часы — сгруппированные столбцы (норма / факт)
    // =====================================================================
    public class HoursChart : Grid
    {
        private readonly Canvas _canvas = new Canvas();
        private readonly TextBlock _empty;
        private List<BarItem> _items = new List<BarItem>();

        public HoursChart()
        {
            Children.Add(_canvas);
            _empty = DepartmentBarChart.CreateEmptyLabel();
            Children.Add(_empty);
            SizeChanged += (s, e) => Redraw();
        }

        public void SetData(IEnumerable<BarItem> items)
        {
            _items = items == null ? new List<BarItem>() : items.ToList();
            Redraw();
        }

        // Заглушка с пояснением, почему график пустой
        public void ShowEmpty(string message)
        {
            _items = new List<BarItem>();
            if (!string.IsNullOrWhiteSpace(message)) _empty.Text = message;
            _empty.Visibility = Visibility.Visible;
            _canvas.Children.Clear();
            _canvas.Visibility = Visibility.Collapsed;
        }

        private void Redraw()
        {
            _canvas.Children.Clear();
            bool hasData = _items.Count > 0;
            _empty.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            _canvas.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            if (!hasData) return;

            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 80 || height < 60) return;

            Brush grid = (Brush)Application.Current.Resources["GridLine"];
            Brush muted = (Brush)Application.Current.Resources["ChartMuted"];
            Brush mutedBorder = (Brush)Application.Current.Resources["Border"];
            Brush primary = (Brush)Application.Current.Resources["ChartPrimary"];
            Brush dim = (Brush)Application.Current.Resources["TextSecondary"];

            double left = 40;
            double right = 8;
            double top = 10;
            double bottom = 42;
            double plotW = Math.Max(20, width - left - right);
            double plotH = Math.Max(20, height - top - bottom);

            double max = _items.Max(i => Math.Max(i.Primary, i.Secondary));
            if (max <= 0) max = 1;
            max = Math.Ceiling(max / 40.0) * 40.0;

            for (int i = 0; i <= 4; i++)
            {
                double v = max / 4.0 * i;
                double y = top + plotH - (v / max) * plotH;
                Line line = new Line
                {
                    X1 = left, X2 = left + plotW, Y1 = y, Y2 = y,
                    Stroke = grid, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }
                };
                _canvas.Children.Add(line);

                TextBlock valueLabel = new TextBlock
                {
                    Text = v.ToString("0"),
                    FontSize = 9.5,
                    Foreground = dim,
                    Width = 34,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(valueLabel, 0);
                Canvas.SetTop(valueLabel, y - 8);
                _canvas.Children.Add(valueLabel);
            }

            double slot = plotW / _items.Count;
            double barWidth = Math.Max(4, Math.Min(14, slot / 3.4));

            for (int i = 0; i < _items.Count; i++)
            {
                BarItem item = _items[i];
                double centerX = left + slot * i + slot / 2;

                double normH = (item.Secondary / max) * plotH;
                Rectangle normBar = new Rectangle
                {
                    Width = barWidth,
                    Height = Math.Max(0.5, normH),
                    Fill = muted,
                    Stroke = mutedBorder,
                    StrokeThickness = 0.6,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(normBar, centerX - barWidth - 1);
                Canvas.SetTop(normBar, top + plotH - normH);
                normBar.ToolTip = string.Format("{0}\nНорма: {1:0.#} ч.", item.Label, item.Secondary);
                _canvas.Children.Add(normBar);

                double factH = (item.Primary / max) * plotH;
                Rectangle factBar = new Rectangle
                {
                    Width = barWidth,
                    Height = Math.Max(0.5, factH),
                    Fill = primary,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(factBar, centerX + 1);
                Canvas.SetTop(factBar, top + plotH - factH);
                factBar.ToolTip = string.Format("{0}\nФакт: {1:0.#} ч.\nНорма: {2:0.#} ч.", item.Label, item.Primary, item.Secondary);
                _canvas.Children.Add(factBar);

                // Фамилия под столбцом — вертикально, если места мало
                TextBlock label = new TextBlock
                {
                    Text = ShortName(item.Label),
                    FontSize = 8.5,
                    Foreground = dim,
                    Width = Math.Max(28, slot),
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(label, left + slot * i);
                Canvas.SetTop(label, top + plotH + 6);
                label.LayoutTransform = new RotateTransform(-35);
                _canvas.Children.Add(label);
            }
        }

        private static string ShortName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
            string[] parts = fullName.Trim().Split(' ');
            if (parts.Length >= 2)
            {
                string initials = string.Empty;
                for (int i = 1; i < parts.Length && initials.Length < 4; i++)
                    if (parts[i].Length > 0) initials += parts[i][0] + ".";
                return parts[0] + " " + initials;
            }
            return fullName;
        }
    }

    // =====================================================================
    // Легенда под графиком (цветной маркер + подпись)
    // =====================================================================
    public class ChartLegend : StackPanel
    {
        public ChartLegend()
        {
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Center;
            Margin = new Thickness(0, 4, 0, 0);
        }

        public void SetItems(params string[] labels)
        {
            Children.Clear();
            Brush[] colors =
            {
                (Brush)Application.Current.Resources["ChartPrimary"],
                (Brush)Application.Current.Resources["ChartMuted"]
            };
            for (int i = 0; i < labels.Length; i++)
            {
                StackPanel item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
                item.Children.Add(new Rectangle
                {
                    Width = 10,
                    Height = 10,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = colors[Math.Min(i, colors.Length - 1)],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                item.Children.Add(new TextBlock
                {
                    Text = labels[i],
                    FontSize = 10.5,
                    Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                    VerticalAlignment = VerticalAlignment.Center
                });
                Children.Add(item);
            }
        }
    }

    // Общие форматтеры для подписей
    public static class ChartFormat
    {
        public static string Money(decimal value)
        {
            return value.ToString("N2", CultureInfo.CurrentCulture);
        }
    }
}

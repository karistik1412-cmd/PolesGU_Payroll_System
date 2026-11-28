using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PayrollClient.Services;
using Localization = PayrollClient.Services.Localization; // снимает конфликт с System.Windows.Localization

namespace PayrollClient.Converters
{
    // Разбор чисел, приходящих со стороны сервера.
    // DataTable из JSON отдаёт значения строками, причём разделитель дробной части
    // зависит от культуры сервера: «0,75» или «0.75». InvariantCulture читает запятую
    // как разделитель РАЗРЯДОВ, из-за чего 0,75 превращалось в 75, а 30 980,94 —
    // в 3 098 094. Поэтому разбираем аккуратно, с учётом обоих вариантов.
    internal static class NumberText
    {
        public static bool TryParse(object value, out decimal result)
        {
            result = 0m;
            if (value == null) return false;

            if (value is decimal) { result = (decimal)value; return true; }
            if (value is int) { result = (int)value; return true; }
            if (value is long) { result = (long)value; return true; }
            if (value is double) { result = (decimal)(double)value; return true; }
            if (value is float) { result = (decimal)(float)value; return true; }

            string text = value.ToString().Trim();
            if (text.Length == 0) return false;

            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out result)) return true;

            bool hasComma = text.Contains(",");
            bool hasDot = text.Contains(".");

            if (hasComma && !hasDot)
                return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out result);

            if (hasDot && !hasComma)
                return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out result);

            if (hasComma && hasDot)
            {
                // Последний из разделителей — десятичный
                int lastComma = text.LastIndexOf(',');
                int lastDot = text.LastIndexOf('.');
                string normalized = lastComma > lastDot
                    ? text.Replace(".", string.Empty).Replace(',', '.')
                    : text.Replace(",", string.Empty);
                return decimal.TryParse(normalized, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out result);
            }

            return false;
        }
    }
    // Перевод служебного значения с сервера на язык интерфейса
    public class TermConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? string.Empty : Localization.Term(value.ToString());
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    // Базовое имя столбца -> переведённый заголовок (ключ col.*)
    public class ColumnHeaderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string name = value == null ? string.Empty : value.ToString();
            return Localization.T("col." + name);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    // true -> Visible, false -> Collapsed
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            if (parameter != null && parameter.ToString() == "invert") flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Visible;
        }
    }

    // Число с разделителями разрядов (денежный вид)
    public class MoneyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "—";
            decimal d;
            return NumberText.TryParse(value, out d) ? d.ToString("N2") : value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    // Количество (дни, часы): без лишних нулей — «21», «151,2»
    public class NumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            decimal d;
            return NumberText.TryParse(value, out d) ? d.ToString("0.##") : value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    // Ставка: 1 -> «1», 0,5 -> «0,5»
    public class RateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "—";
            decimal d;
            return NumberText.TryParse(value, out d) ? d.ToString("0.##") : value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    // «Пусто или null» -> видимость (для заглушек «нет данных»)
    public class EmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEmpty;
            if (value == null) isEmpty = true;
            else if (value is bool) isEmpty = !(bool)value;
            else if (value is int) isEmpty = (int)value <= 0;
            else isEmpty = string.IsNullOrWhiteSpace(value.ToString());

            if (parameter != null && parameter.ToString() == "invert") isEmpty = !isEmpty;
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}

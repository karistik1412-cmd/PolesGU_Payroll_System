using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PayrollClient.Services;
using Localization = PayrollClient.Services.Localization; // снимает конфликт с System.Windows.Localization

namespace PayrollClient.Converters
{
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
            if (!decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                if (!decimal.TryParse(value.ToString(), out d)) return value.ToString();
            }
            return d.ToString("N2");
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
            if (!decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                if (!decimal.TryParse(value.ToString(), out d)) return value.ToString();
            }
            return d.ToString("0.##");
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

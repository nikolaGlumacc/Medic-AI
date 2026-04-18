using System;
using System.Globalization;
using System.Windows.Data;

namespace MedicAIGUI.Converters
{
    public class LogColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string line) return "Default";

            string upper = line.ToUpper();
            if (upper.Contains("[ERROR]") || upper.Contains("[FAIL]") || upper.Contains("FATAL") || upper.Contains("EXCEPTION"))
                return "Error";
            if (upper.Contains("[WARN]") || upper.Contains("WARNING") || upper.Contains("TIMEOUT"))
                return "Warning";
            if (upper.Contains("[OK]") || upper.Contains("SUCCESS") || upper.Contains("VERIFIED") || upper.Contains("DONE"))
                return "Success";

            return "Default";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

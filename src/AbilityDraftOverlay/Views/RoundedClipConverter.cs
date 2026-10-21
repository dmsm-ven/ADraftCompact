using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ADraftCompact.Views;

/// <summary>
/// Clips an element to a rounded rectangle of its own size. A Border with a CornerRadius does not round the
/// bitmap inside it, so without this the icon would keep square corners (and with a zero-width border the
/// "round borders" option would change nothing visible).
/// Inputs: ActualWidth, ActualHeight, CornerRadius. No clip is applied for a zero radius or an unmeasured element.
/// </summary>
public sealed class RoundedClipConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not double width || values[1] is not double height || values[2] is not CornerRadius corners)
            return null;

        var radius = corners.TopLeft;
        if (width <= 0 || height <= 0 || radius <= 0) return null;

        var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        clip.Freeze();
        return clip;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

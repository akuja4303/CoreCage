using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CoreCage.App;

/// <summary>
/// Maps a VM's <c>LastOk</c> bool to the Good/Bad brush from App.xaml's palette, so a status message's
/// success/failure is visually distinct (FOLD-1) rather than shown in one neutral color regardless of
/// outcome. Looks the brushes up on Application.Current so it always reflects the live palette instead
/// of duplicating the two hex colors here.
/// </summary>
public sealed class LastOkToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool ok = value is bool b && b;
        string key = ok ? "Good" : "Bad";
        return Application.Current?.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a VM's <c>LastOk</c> bool to a leading status glyph ("done"/"failed" shape, not just a color) so
/// colorblind users get a non-color cue too (FOLD-1: "not by color alone"). Kept alongside the existing
/// StatusMessage text prefix, never replacing it.
/// </summary>
public sealed class LastOkToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && b ? "✓" : "✗"; // check / cross

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

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

/// <summary>
/// Maps a VM's three-state <see cref="StatusKind"/> to the Good/Bad/neutral brush from App.xaml's
/// palette (review IMPORTANT-1 — a bool LastOk had no way to express "idle, nothing happened yet" so
/// idle/informational messages rendered as a false Good-green success). Neutral resolves to TextLo, the
/// app's existing muted-text brush — no new palette entry needed. Looks brushes up on
/// Application.Current so it always reflects the live palette instead of duplicating hex colors here.
/// </summary>
public sealed class StatusKindToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is StatusKind k
            ? k switch
            {
                StatusKind.Success => "Good",
                StatusKind.Error => "Bad",
                _ => "TextLo",
            }
            : "TextLo";
        return Application.Current?.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a VM's three-state <see cref="StatusKind"/> to a leading status glyph: a neutral state gets a
/// plain bullet (no ✓ and no ✗ — there is nothing to claim success or failure about), a completed
/// success gets ✓, a completed failure gets ✗. Kept alongside the existing StatusMessage text, never
/// replacing it, so failure still reads by glyph+text, not color alone (review IMPORTANT-1).
/// </summary>
public sealed class StatusKindToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StatusKind k
            ? k switch
            {
                StatusKind.Success => "✓",
                StatusKind.Error => "✗",
                _ => "•",
            }
            : "•";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

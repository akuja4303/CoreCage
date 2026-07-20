using Microsoft.VisualStudio.TestTools.UnitTesting;
using CoreCage.App;

namespace CoreCage.Tests;

/// <summary>
/// FOLD-1 (Task 8 UX pass): StatusMessage used to render in one neutral color regardless of LastOk.
/// These two converters wire it to the Good/Bad brushes plus a non-color glyph cue (colorblind/AA),
/// without any change to what a StatusMessage or LastOk actually mean.
/// </summary>
[TestClass]
public sealed class StatusConvertersTests
{
    [TestMethod]
    public void Glyph_converter_shows_check_when_ok()
    {
        var converter = new LastOkToGlyphConverter();
        Assert.AreEqual("✓", converter.Convert(true, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Glyph_converter_shows_cross_when_not_ok()
    {
        var converter = new LastOkToGlyphConverter();
        Assert.AreEqual("✗", converter.Convert(false, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Glyph_converter_treats_non_bool_input_as_not_ok()
    {
        var converter = new LastOkToGlyphConverter();
        Assert.AreEqual("✗", converter.Convert(null, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Brush_converter_never_throws_without_a_live_Application()
    {
        // The unit test host has no System.Windows.Application instance, so Application.Current is
        // null -- the converter must degrade gracefully (return null), never throw onto a binding.
        var converter = new LastOkToBrushConverter();
        var okResult = converter.Convert(true, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture);
        var failResult = converter.Convert(false, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsNull(okResult);
        Assert.IsNull(failResult);
    }

    [TestMethod]
    public void Brush_converter_ConvertBack_is_not_supported()
    {
        var converter = new LastOkToBrushConverter();
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack(null!, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}

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

    // ------------------------------------------------------------------
    // Three-state StatusKind converters (review IMPORTANT-1): Neutral must render as neither the
    // check nor the cross glyph, and as a muted (not Good/Bad) brush, so an idle/informational status
    // is never mistaken for a completed success or failure.
    // ------------------------------------------------------------------

    [TestMethod]
    public void StatusKind_glyph_is_check_for_Success()
    {
        var converter = new StatusKindToGlyphConverter();
        Assert.AreEqual("✓", converter.Convert(StatusKind.Success, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void StatusKind_glyph_is_cross_for_Error()
    {
        var converter = new StatusKindToGlyphConverter();
        Assert.AreEqual("✗", converter.Convert(StatusKind.Error, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void StatusKind_glyph_is_neither_check_nor_cross_for_Neutral()
    {
        var converter = new StatusKindToGlyphConverter();
        var glyph = converter.Convert(StatusKind.Neutral, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreNotEqual("✓", glyph, "an idle/informational status must never show a false success checkmark");
        Assert.AreNotEqual("✗", glyph, "an idle/informational status must never show a false failure cross");
    }

    [TestMethod]
    public void StatusKind_glyph_treats_non_enum_input_as_Neutral()
    {
        var converter = new StatusKindToGlyphConverter();
        var glyph = converter.Convert(null, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreNotEqual("✓", glyph);
        Assert.AreNotEqual("✗", glyph);
    }

    [TestMethod]
    public void StatusKind_brush_converter_never_throws_without_a_live_Application()
    {
        var converter = new StatusKindToBrushConverter();
        var success = converter.Convert(StatusKind.Success, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture);
        var error = converter.Convert(StatusKind.Error, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture);
        var neutral = converter.Convert(StatusKind.Neutral, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsNull(success);
        Assert.IsNull(error);
        Assert.IsNull(neutral);
    }

    [TestMethod]
    public void StatusKind_brush_converter_ConvertBack_is_not_supported()
    {
        var converter = new StatusKindToBrushConverter();
        Assert.ThrowsException<NotSupportedException>(() =>
            converter.ConvertBack(null!, typeof(StatusKind), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}

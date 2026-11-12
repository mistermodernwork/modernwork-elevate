namespace ModernWorkElevate.AppAvalonia.Services;

using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

/// <summary>
/// Derives the app's accent tokens from the user's Windows accent colour, the way Windows 11
/// does for its own controls: a darker shade as the fill on light surfaces, a lighter shade on
/// dark ones, and a foreground that keeps text on the fill readable. Re-applied when the accent
/// or the theme changes, so the panel follows Settings → Personalisation → Colours live.
/// </summary>
/// <remarks>
/// The values are written to <see cref="Application.Resources"/> directly, which wins over the
/// fallbacks in <c>Styles/Theme.axaml</c>; every view reads them as <c>DynamicResource</c>.
/// The Fluent <c>SystemAccentColor*</c> keys are set too, so toggles, focus rings and
/// selection highlights in the stock controls carry the same colour.
/// </remarks>
internal static class AccentPalette
{
    // Windows 11 default accent, used when the platform reports none.
    private static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary>Applies the palette now and keeps it in sync with accent and theme changes.</summary>
    public static void Attach(Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        Apply(app);

        app.ActualThemeVariantChanged += (_, _) => Apply(app);
        if (app.PlatformSettings is { } settings)
        {
            settings.ColorValuesChanged += (_, _) => Apply(app);
        }
    }

    private static void Apply(Application app)
    {
        var accent = app.PlatformSettings?.GetColorValues().AccentColor1 ?? FallbackAccent;
        if (accent.A == 0)
        {
            accent = FallbackAccent;
        }

        var dark = app.ActualThemeVariant == ThemeVariant.Dark;

        var dark1 = Mix(accent, Colors.Black, 0.18);
        var dark2 = Mix(accent, Colors.Black, 0.34);
        var dark3 = Mix(accent, Colors.Black, 0.50);
        var light1 = Mix(accent, Colors.White, 0.22);
        var light2 = Mix(accent, Colors.White, 0.42);
        var light3 = Mix(accent, Colors.White, 0.62);

        // Windows 11: AccentFillColorDefault is Dark1 on light surfaces and Light2 on dark ones.
        var fill = dark ? light2 : dark1;
        var hover = dark ? light1 : dark2;
        var text = dark ? light3 : dark2;

        var resources = app.Resources;
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorDark1"] = dark1;
        resources["SystemAccentColorDark2"] = dark2;
        resources["SystemAccentColorDark3"] = dark3;
        resources["SystemAccentColorLight1"] = light1;
        resources["SystemAccentColorLight2"] = light2;
        resources["SystemAccentColorLight3"] = light3;

        resources["AccentBrush"] = new SolidColorBrush(fill);
        resources["AccentHoverBrush"] = new SolidColorBrush(hover);
        resources["AccentForegroundBrush"] = new SolidColorBrush(ReadableOn(fill));
        resources["AccentTextBrush"] = new SolidColorBrush(text);
        resources["AccentSoftBrush"] = new SolidColorBrush(fill, dark ? 0.16 : 0.10);
        resources["SurfaceAccentBrush"] = new SolidColorBrush(fill, dark ? 0.12 : 0.06);
        resources["AccentStrokeBrush"] = new SolidColorBrush(fill, dark ? 0.45 : 0.35);
    }

    /// <summary>Linear mix of <paramref name="color"/> towards <paramref name="target"/>.</summary>
    private static Color Mix(Color color, Color target, double amount)
    {
        static byte Channel(byte from, byte to, double t) => (byte)Math.Round(from + ((to - from) * t));
        return Color.FromRgb(Channel(color.R, target.R, amount), Channel(color.G, target.G, amount), Channel(color.B, target.B, amount));
    }

    /// <summary>Black or white, whichever reads better on <paramref name="background"/> (WCAG luminance).</summary>
    private static Color ReadableOn(Color background)
    {
        static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Linear(background.R)) + (0.7152 * Linear(background.G)) + (0.0722 * Linear(background.B));

        // Contrast with white (1.05 / (L + 0.05)) beats contrast with black ((L + 0.05) / 0.05)
        // below L ≈ 0.179.
        return luminance > 0.179 ? Colors.Black : Colors.White;
    }
}

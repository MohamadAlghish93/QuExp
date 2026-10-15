using PdfSharpCore.Fonts;

namespace QueryExplorer.Infrastructure.Services;

/// <summary>
/// PdfSharpCore needs an actual TrueType font file to draw text - unlike on Windows,
/// Linux has no GDI font store it can fall back to automatically. This resolver looks
/// for a handful of commonly-preinstalled fonts and fails with an actionable message
/// (rather than PdfSharpCore's own cryptic internal error) if none are found.
/// Register once at startup: GlobalFontSettings.FontResolver = new SystemFontResolver();
/// </summary>
public class SystemFontResolver : IFontResolver
{
    // Ordered by preference. Arabic-capable fonts are listed first since a font with no
    // Arabic glyphs at all (DejaVu Sans has essentially none) will render Arabic text as
    // empty boxes regardless of how correctly ArabicTextShaper reorders/reshapes it - the
    // shaping only helps once a font that actually has the glyphs is being used.
    private static readonly string[] CandidatePaths =
    {
        "/usr/share/fonts/truetype/noto/NotoNaskhArabic-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoSansArabic-Regular.ttf",
        "/usr/share/fonts/opentype/noto/NotoNaskhArabic-Regular.ttf",
        "/usr/share/fonts/opentype/noto/NotoSansArabic-Regular.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        "/Library/Fonts/Arial.ttf", // macOS dev machines
        "C:\\Windows\\Fonts\\arial.ttf" // Windows dev machines
    };

    private static string? _regularPath;
    private static string? _boldPath;

    private static string RegularPath => _regularPath ??= CandidatePaths
        .Where(p => !p.Contains("Bold", StringComparison.OrdinalIgnoreCase))
        .FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException(
            "No TrueType font found for PDF generation. On Debian/Ubuntu, install one with: " +
            "apt-get install -y fonts-dejavu-core - then restart the API. " +
            "If you need Arabic text to render correctly, also install: apt-get install -y fonts-noto-core " +
            "(DejaVu has little to no Arabic glyph coverage). " +
            "(PDF generation needs an actual font file on Linux; there's no OS font fallback like on Windows.)");

    private static string BoldPath => _boldPath ??=
        CandidatePaths.Where(p => p.Contains("Bold", StringComparison.OrdinalIgnoreCase)).FirstOrDefault(File.Exists)
        ?? RegularPath; // fall back to regular weight if no bold variant is installed

    public string DefaultFontName => throw new NotImplementedException();

    public byte[] GetFont(string faceName) =>
        File.ReadAllBytes(faceName == "DefaultFontBold" ? BoldPath : RegularPath);

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? "DefaultFontBold" : "DefaultFont");
}

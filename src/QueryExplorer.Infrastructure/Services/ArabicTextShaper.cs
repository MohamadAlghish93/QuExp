using System.Text;

namespace QueryExplorer.Infrastructure.Services;

/// <summary>
/// Best-effort Arabic text shaping for PdfSharpCore, which - unlike a real text engine
/// (e.g. HarfBuzz) - draws raw Unicode codepoints left-to-right with no contextual letter
/// joining. Left as-is, Arabic text renders as disconnected, isolated-form letters in the
/// wrong (LTR) order, which is the "value appears wrong" symptom.
///
/// This does two things:
/// 1. Reshapes each Arabic letter into its correct contextual glyph (isolated / initial /
///    medial / final) from the Unicode Arabic Presentation Forms-B block, based on its
///    joining neighbors, so words render as connected script instead of separate letters.
/// 2. Reorders runs right-to-left, while keeping embedded Latin/digit runs (names, numbers)
///    in their own left-to-right order within the line - a simplified stand-in for the full
///    Unicode Bidirectional Algorithm, sufficient for typical single-line field values.
///
/// Known limitations: ligatures (e.g. lam-alef), tashkeel/diacritics, and multi-paragraph
/// bidi are not specially handled. A character with no mapped presentation form is left
/// unshaped rather than dropped, so unsupported input degrades gracefully instead of
/// producing wrong output.
/// </summary>
public static class ArabicTextShaper
{
    public static bool ContainsArabic(string text) => text.Any(IsArabicChar);

    private static bool IsArabicChar(char c) =>
        (c >= 0x0600 && c <= 0x06FF) || (c >= 0x0750 && c <= 0x077F) ||
        (c >= 0xFB50 && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF);

    // Letters that only join to a PRECEDING letter, never to the following one
    // (no initial/medial forms exist for these).
    private static readonly HashSet<char> NonJoiningLetters = new("اأإآدذرزوىةؤ");

    // base letter -> (isolated, final, initial, medial). 0 means "form doesn't exist".
    private static readonly Dictionary<char, (char Iso, char Fin, char Init, char Med)> Forms = new()
    {
        ['ء'] = ('\uFE80', '\0', '\0', '\0'),
        ['آ'] = ('\uFE81', '\uFE82', '\0', '\0'),
        ['أ'] = ('\uFE83', '\uFE84', '\0', '\0'),
        ['ؤ'] = ('\uFE85', '\uFE86', '\0', '\0'),
        ['إ'] = ('\uFE87', '\uFE88', '\0', '\0'),
        ['ئ'] = ('\uFE89', '\uFE8A', '\uFE8B', '\uFE8C'),
        ['ا'] = ('\uFE8D', '\uFE8E', '\0', '\0'),
        ['ب'] = ('\uFE8F', '\uFE90', '\uFE91', '\uFE92'),
        ['ة'] = ('\uFE93', '\uFE94', '\0', '\0'),
        ['ت'] = ('\uFE95', '\uFE96', '\uFE97', '\uFE98'),
        ['ث'] = ('\uFE99', '\uFE9A', '\uFE9B', '\uFE9C'),
        ['ج'] = ('\uFE9D', '\uFE9E', '\uFE9F', '\uFEA0'),
        ['ح'] = ('\uFEA1', '\uFEA2', '\uFEA3', '\uFEA4'),
        ['خ'] = ('\uFEA5', '\uFEA6', '\uFEA7', '\uFEA8'),
        ['د'] = ('\uFEA9', '\uFEAA', '\0', '\0'),
        ['ذ'] = ('\uFEAB', '\uFEAC', '\0', '\0'),
        ['ر'] = ('\uFEAD', '\uFEAE', '\0', '\0'),
        ['ز'] = ('\uFEAF', '\uFEB0', '\0', '\0'),
        ['س'] = ('\uFEB1', '\uFEB2', '\uFEB3', '\uFEB4'),
        ['ش'] = ('\uFEB5', '\uFEB6', '\uFEB7', '\uFEB8'),
        ['ص'] = ('\uFEB9', '\uFEBA', '\uFEBB', '\uFEBC'),
        ['ض'] = ('\uFEBD', '\uFEBE', '\uFEBF', '\uFEC0'),
        ['ط'] = ('\uFEC1', '\uFEC2', '\uFEC3', '\uFEC4'),
        ['ظ'] = ('\uFEC5', '\uFEC6', '\uFEC7', '\uFEC8'),
        ['ع'] = ('\uFEC9', '\uFECA', '\uFECB', '\uFECC'),
        ['غ'] = ('\uFECD', '\uFECE', '\uFECF', '\uFED0'),
        ['ف'] = ('\uFED1', '\uFED2', '\uFED3', '\uFED4'),
        ['ق'] = ('\uFED5', '\uFED6', '\uFED7', '\uFED8'),
        ['ك'] = ('\uFED9', '\uFEDA', '\uFEDB', '\uFEDC'),
        ['ل'] = ('\uFEDD', '\uFEDE', '\uFEDF', '\uFEE0'),
        ['م'] = ('\uFEE1', '\uFEE2', '\uFEE3', '\uFEE4'),
        ['ن'] = ('\uFEE5', '\uFEE6', '\uFEE7', '\uFEE8'),
        ['ه'] = ('\uFEE9', '\uFEEA', '\uFEEB', '\uFEEC'),
        ['و'] = ('\uFEED', '\uFEEE', '\0', '\0'),
        ['ى'] = ('\uFEEF', '\uFEF0', '\0', '\0'),
        ['ي'] = ('\uFEF1', '\uFEF2', '\uFEF3', '\uFEF4'),
    };

    /// <summary>Shapes and reorders a line of text for correct visual PDF rendering.
    /// Safe to call on any text - non-Arabic input is returned unchanged.</summary>
    public static string PrepareForDisplay(string? text)
    {
        if (string.IsNullOrEmpty(text) || !ContainsArabic(text)) return text ?? "";

        // Split into runs of "Arabic" vs "everything else" so embedded numbers/Latin
        // words keep their own natural left-to-right order.
        var runs = new List<(bool IsArabic, string Text)>();
        var current = new StringBuilder();
        bool? currentIsArabic = null;

        foreach (var c in text)
        {
            var isArabic = IsArabicChar(c);
            if (currentIsArabic is null) currentIsArabic = isArabic;

            if (isArabic != currentIsArabic)
            {
                runs.Add((currentIsArabic.Value, current.ToString()));
                current.Clear();
                currentIsArabic = isArabic;
            }
            current.Append(c);
        }
        if (current.Length > 0) runs.Add((currentIsArabic!.Value, current.ToString()));

        // Shape each Arabic run's letters, then reverse its character order (RTL).
        // Non-Arabic runs are kept as-is. Finally, reverse the ORDER of runs so the line
        // reads right-to-left overall, with each run's own internal order already correct.
        var processed = runs.Select(r => r.IsArabic ? Reverse(ShapeRun(r.Text)) : r.Text);
        return string.Concat(processed.Reverse());
    }

    private static string ShapeRun(string run)
    {
        var result = new StringBuilder(run.Length);
        for (int i = 0; i < run.Length; i++)
        {
            var c = run[i];
            if (!Forms.TryGetValue(c, out var forms))
            {
                result.Append(c); // not a letter we have a mapping for (e.g. punctuation, digit) - passthrough
                continue;
            }

            var prevJoins = i > 0 && Forms.ContainsKey(run[i - 1]) && !NonJoiningLetters.Contains(run[i - 1]);
            var nextExists = i < run.Length - 1 && Forms.ContainsKey(run[i + 1]);
            var thisIsNonJoining = NonJoiningLetters.Contains(c);

            char shaped;
            if (prevJoins && nextExists && !thisIsNonJoining && forms.Med != '\0')
                shaped = forms.Med;
            else if (prevJoins && forms.Fin != '\0')
                shaped = forms.Fin;
            else if (nextExists && !thisIsNonJoining && forms.Init != '\0')
                shaped = forms.Init;
            else
                shaped = forms.Iso;

            result.Append(shaped);
        }
        return result.ToString();
    }

    private static string Reverse(string s)
    {
        var chars = s.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
}

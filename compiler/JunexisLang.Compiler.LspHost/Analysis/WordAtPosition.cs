namespace JunexisLang.Compiler.LspHost.Analysis;

/// <summary>
/// Wyciąga identyfikator (słowo) stojący pod kursorem w dokumencie tekstowym.
/// </summary>
internal static class WordAtPosition
{
    /// <summary>Słowo pod kursorem wraz z jego zakresem znakowym w wierszu.</summary>
    /// <param name="Word">Tekst identyfikatora.</param>
    /// <param name="StartChar">Indeks pierwszego znaku (0-based, inclusive).</param>
    /// <param name="EndChar">Indeks za ostatnim znakiem (0-based, exclusive).</param>
    internal readonly record struct WordInfo(string Word, int StartChar, int EndChar);

    /// <param name="source">Pełny tekst dokumentu.</param>
    /// <param name="line">Numer wiersza wg LSP (0-based).</param>
    /// <param name="character">Numer kolumny wg LSP (0-based).</param>
    /// <returns>Identyfikator z zakresem lub null gdy kursor nie stoi na identyfikatorze.</returns>
    public static WordInfo? Get(string source, int line, int character)
    {
        // normalizujemy znaki nowej linii → LF
        var lines = source.ReplaceLineEndings("\n").Split('\n');

        if (line >= lines.Length) return null;

        var ln = lines[line];
        if (character > ln.Length) character = ln.Length;

        var start = character;
        while (start > 0 && IsWord(ln[start - 1])) start--;

        var end = character;
        while (end < ln.Length && IsWord(ln[end])) end++;

        return start < end ? new WordInfo(ln[start..end], start, end) : null;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}

namespace _10xnotes.Text;

/// <summary>
/// Length arithmetic shared by every rule set that clamps user text to a column width.
/// </summary>
/// <remarks>
/// Extracted because <see cref="Truncate"/> existed twice — once in
/// <see cref="MarkdownImportValidator"/> and once in <c>NoteValidator</c> — with the same body and
/// nearly the same comment. A third copy was the point at which the duplication stopped being
/// harmless: the correction below is subtle enough that one copy drifting from the others would
/// not be noticed until a title with an emoji reached Postgres.
/// </remarks>
internal static class TextLimits
{
    /// <summary>
    /// Cuts to at most <paramref name="maxLength"/> UTF-16 units without splitting a character.
    /// </summary>
    /// <remarks>
    /// A plain <c>value[..maxLength]</c> slices by code unit, so a cut landing inside a surrogate
    /// pair — any emoji, which file names and pasted first lines carry routinely — leaves a lone
    /// surrogate behind. That is not a valid string: it either encodes to U+FFFD or throws on the
    /// way to Postgres. Polish diacritics are all BMP, so the naive version looks correct in every
    /// realistic test and fails on the first emoji.
    /// </remarks>
    internal static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = maxLength;

        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return value[..cut];
    }
}

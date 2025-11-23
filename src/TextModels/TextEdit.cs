namespace FluidX.TextModels;

public class TextEdit
{
    public TextReplacement[] Replacements { get; }

    public TextEdit(ReadOnlySpan<TextReplacement> replacements)
    {
        for (int i = 0; i < replacements.Length - 2; i++)
        {
            var item1 = replacements[i];
            var item2 = replacements[i + 1];
            bool isValid = item1.Range.GetEndPosition().IsBeforeOrEqual(item2.Range.GetStartPosition());
            if (!isValid)
                throw new ArgumentException("Replacements must not overlap and must be in order", nameof(replacements));
        }
        Replacements = replacements.ToArray();
    }
}

public record class TextReplacement(TextRange Range, string Text)
{
    public bool IsEmpty => Range.IsEmpty && Text.Length == 0;
}

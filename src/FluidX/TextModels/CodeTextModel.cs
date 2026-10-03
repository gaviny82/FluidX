using FluidX.Tokenization;

namespace FluidX.TextModels;

/// <summary>
/// A code-oriented text model that adds language tokenization to <see cref="TextModel"/>.
/// </summary>
public class CodeTextModel : TextModel
{
    private const int LargeFileSizeThreshold = 20 * 1024 * 1024;
    private const int LargeFileLineCountThreshold = 300 * 1000;

    public GlobalLanguageId LanguageId => Tokenization.LanguageId;
    public TokenizationTextModelPart Tokenization { get; }
    public bool IsTooLargeForTokenization { get; }

    public CodeTextModel(string source, GlobalLanguageId languageId)
        : base(source)
    {
        IsTooLargeForTokenization = TextBuffer.Length > LargeFileSizeThreshold
            || TextBuffer.LineCount > LargeFileLineCountThreshold;
        Tokenization = new TokenizationTextModelPart(this, languageId);
    }

    protected override void OnContentChanged(TextModelContentChangedEventArgs change)
        => Tokenization.HandleDidChangeContent(change);

}

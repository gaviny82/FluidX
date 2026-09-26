using FluidX.TextBuffers;
using FluidX.Tokenization;

namespace FluidX.TextModels;

/// <summary>
/// A code-oriented text model that adds language tokenization to <see cref="PlainTextModel"/>.
/// </summary>
public class TextModel : PlainTextModel, IDecorationTreesHost
{
    private const int LargeFileSizeThreshold = 20 * 1024 * 1024;
    private const int LargeFileLineCountThreshold = 300 * 1000;

    public GlobalLanguageId LanguageId => Tokenization.LanguageId;
    public TokenizationTextModelPart Tokenization { get; }
    public bool IsTooLargeForTokenization { get; }

    private readonly TextModelDecorationTrees _decorationTrees = new();

    public TextModel(string source, EndOfLine eol, GlobalLanguageId languageId)
        : base(source, eol)
    {
        IsTooLargeForTokenization = TextBuffer.Length > LargeFileSizeThreshold
            || TextBuffer.LineCount > LargeFileLineCountThreshold;
        Tokenization = new TokenizationTextModelPart(this, languageId);
    }

    TextRange IDecorationTreesHost.GetRangeAt(int start, int end)
        => TextBuffer.GetRangeAt(start, end - start);

    protected override void AcceptDecorationReplace(
        int offset,
        int length,
        int textLength,
        bool forceMoveMarkers)
        => _decorationTrees.AcceptReplace(offset, length, textLength, forceMoveMarkers);
}

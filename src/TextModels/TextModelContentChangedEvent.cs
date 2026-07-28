using FluidX.TextBuffers;

namespace FluidX.TextModels;

public enum RawContentChangedType
{
    Flush = 1,
    LineChanged = 2,
    LinesDeleted = 3,
    LinesInserted = 4,
    EOLChanged = 5
}

public abstract class ModelRawChange
{
    public abstract RawContentChangedType Type { get; }
}

/// <summary>
/// An event describing that a model has been reset to a new value.
/// </summary>
public class ModelRawFlush : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.Flush;
}

/// <summary>
/// An event describing that a line has changed in a model.
/// </summary>
public class ModelRawLineChanged : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.LineChanged;

    /// <summary>
    /// The line that has changed (1-based)
    /// </summary>
    public int LineNumber { get; }
    /// <summary>
    /// The new value of the line.
    /// </summary>
    public string Detail { get; }

    // TODO: InjectedText

    public ModelRawLineChanged(int lineNumber, string detail)
    {
        LineNumber = lineNumber;
        Detail = detail;
    }
}

public class ModelRawLinesDeleted : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.LinesDeleted;
    public int FromLineNumber { get; }
    public int ToLineNumber { get; }
    public ModelRawLinesDeleted(int fromLineNumber, int toLineNumber)
    {
        FromLineNumber = fromLineNumber;
        ToLineNumber = toLineNumber;
    }
}

public class ModelRawLinesInserted : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.LinesInserted;
    public int FromLineNumber { get; }
    public int ToLineNumber { get; }
    public string[] Details { get; }

    // TODO: InjectedText

    public ModelRawLinesInserted(int fromLineNumber, int toLineNumber, string[] details)
    {
        FromLineNumber = fromLineNumber;
        ToLineNumber = toLineNumber;
        Details = details;
    }
}

public class ModelRawEOLChanged : ModelRawChange
{
    public override RawContentChangedType Type => RawContentChangedType.EOLChanged;
}

public record class ModelRawContentChangedEventArgs(
    IReadOnlyList<ModelRawChange> Changes,
    long VersionId,
    bool IsUndoing,
    bool IsRedoing)
{
    public bool ContainsEventOfType(RawContentChangedType type)
    {
        foreach (var change in Changes)
        {
            if (change.Type == type)
                return true;
        }
        return false;
    }

    public static ModelRawContentChangedEventArgs Merge(
        ModelRawContentChangedEventArgs a,
        ModelRawContentChangedEventArgs b) => new ModelRawContentChangedEventArgs(
            Changes: a.Changes.Concat(b.Changes).ToArray(),
            VersionId: b.VersionId,
            IsUndoing: a.IsUndoing || b.IsUndoing,
            IsRedoing: a.IsRedoing || b.IsRedoing
        );
}

public delegate void TextModelModelContentChangedEventHandler(TextModelContentChangedEventArgs e);

public record class TextModelContentChangedEventArgs(
    ModelRawContentChangedEventArgs RawContentChangedEventArgs,
    ModelContentChangedEventArgs ModelContentChangedEventArgs
);

/// <summary>
/// Args of an event describing a change in the text of a model.
/// </summary>
/// <param name="Changes">The changes are ordered from the end of the document to the beginning, so they should be safe to apply in sequence.</param>
/// <param name="EOL">The (new) end-of-line character.</param>
/// <param name="VersionId">The new version id the model has transitioned to.</param>
/// <param name="IsUndoing">Flag that indicates that this event was generated while undoing.</param>
/// <param name="IsRedoing">Flag that indicates that this event was generated while redoing.</param>
/// <param name="IsFlush">Flag that indicates that all decorations were lost with this edit. The model has been reset to a new value.</param>
/// <param name="IsEolChange">Flag that indicates that this event describes an eol change.</param>
/// <param name="DetailedReasons">Detailed reason information for the change.</param>
/// <param name="DetailedReasonsChangeLengths">The sum of these lengths equals changes.length. The length of this array must equal the length of detailedReasons.</param>
public record class ModelContentChangedEventArgs(
    IReadOnlyList<ModelContentChange> Changes,
    string EOL,
    long VersionId,
    bool IsUndoing,
    bool IsRedoing,
    bool IsFlush,
    bool IsEolChange,
    TextModelEditSource[] DetailedReasons,
    int[] DetailedReasonsChangeLengths
);

public class ModelContentChange
{
    /// <summary>
    /// The old range that got replaced.
    /// </summary>
    public required TextRange Range { get; init; }
    /// <summary>
    /// The offset of the range that got replaced.
    /// </summary>
    public required int RangeOffset { get; init; }
    /// <summary>
    /// The length of the range that got replaced.
    /// </summary>
    public required int RangeLength { get; init; }
    /// <summary>
    /// The new text for the range.
    /// </summary>
    public required string Text { get; init; }
}
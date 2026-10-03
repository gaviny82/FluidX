using System.Collections.Immutable;

namespace FluidX.TextModels;

/// <summary>A committed content operation on the text model.</summary>
/// <remarks>
/// A committed operation does not guarantee different character content.
/// A non-empty edit batch could result in unchanged content before and after applying the edits.
/// </remarks>
public sealed class TextModelContentChangedEventArgs : EventArgs
{
    internal TextModelContentChangedEventArgs(TextVersion version,
        ImmutableArray<TextRecordTransition> transitions)
    {
        Version = version;
        Transitions = transitions;
    }

    /// <summary>
    /// The version committed by the action that changes the text document.
    /// </summary>
    public TextVersion Version { get; }

    /// <summary>
    /// A sequence of transitions from the source <see cref="TextRecord"/> before the action.
    /// </summary>
    /// <remarks>Each <see cref="TextRecordTransition"/> provides the changes from a previous
    /// <see cref="TextRecord"/> to the next intermediate <see cref="TextRecord"/>. Coordinates
    /// are in its own source/target UTF-16 offsets.
    /// </remarks>
    public ImmutableArray<TextRecordTransition> Transitions { get; }

    /// <summary>
    /// The document state before the action.
    /// </summary>
    public TextRecord Before => Transitions[0].Source;

    /// <summary>
    /// The document state after the action.
    /// </summary>
    public TextRecord After => Transitions[^1].Target;
}

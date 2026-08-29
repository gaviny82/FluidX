using System;
using System.Collections.Generic;
using System.Text;
using FluidX.TextBuffers;

namespace FluidX.TextModels;

/// <summary>
/// A text replacement together with model-level behavior used while applying the edit.
/// </summary>
public sealed record class ModelEditOperation
{
    /// <summary>
    /// The text replacement to apply to the underlying text buffer.
    /// </summary>
    public TextReplacement Replacement { get; init; }

    /// <summary>
    /// Forces decorations and tracked ranges at an edit boundary to move after the inserted test.
    /// When <see langword="false"/>, each decoration's configured stickness determines whether it
    /// stays before the inserted text or grows to include it. This is most visible for an insertion,
    /// where <see cref="Range"/> is empty.
    /// </summary>
    public bool ForceMoveMarkers { get; init; }

    /// <summary>
    /// Marks an empty-range insertion as whitespace generated automatically by an editor command,
    /// such as indentation inserted after a newline. When automatic whitespace triming is enabled,
    /// whitespace-only lines produced by this operation become candidates for removal by the next
    /// model edit. This flag has no effect on non-insertion operations.
    /// </summary>
    public bool IsAutowhitespaceEdit { get; init; }

    /// <summary>
    /// Preserves this batch's individual operation boundaries and inverse edits. Set this when a
    /// caller uses the corresponding inverse operation to compute cursor, selection, snippet, or
    /// other tracked state. If any operation in a large batch is tracked, the model will not combine
    /// that batch into a single replacement.
    /// </summary>
    public bool IsTracked { get; init; }

    /// <summary>
    /// Gets the range replaced by this operation.
    /// </summary>
    public TextRange Range => Replacement.Range;

    /// <summary>
    /// Gets the replacement text. Empty text represents a deletion.
    /// </summary>
    public string Text => Replacement.Text;

    public ModelEditOperation(TextReplacement replacement)
    {
        Replacement = replacement;
    }

    public ModelEditOperation(TextRange range, string text)
        : this(new TextReplacement(range, text))
    {
    }
}

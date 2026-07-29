using FluidX.TextBuffers;

namespace FluidX.Decorations;

/// <summary>
/// A decoration in the model.
/// </summary>
public interface IModelDecoration
{
    /// <summary>
    /// Identifier for a decoration.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Identifier for a decoration's owner.
    /// </summary>
    int OwnerId { get; }

    /// <summary>
    /// TextRange that this decoration covers.
    /// </summary>
    TextRange Range { get; }

    /// <summary>
    /// Options associated with this decoration.
    /// </summary>
    ModelDecorationOptions Options { get; }
}

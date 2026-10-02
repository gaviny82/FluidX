namespace FluidX.TextModels;

/// <summary>
/// Opaque, caller-defined metadata associated with a committed text model operation.
/// The text model stores this value with the retrained version history without
/// interpreting its contents.
/// </summary>
/// <remarks>
/// Implementers must keep instances and any referenced data immutable once supplied to
/// a model operation. Metadata is retained by reference, not copied, and may outlive
/// the model's retained history when a consumer holds a version or event.
/// </remarks>
public abstract class TextModelOperationMetadata { }

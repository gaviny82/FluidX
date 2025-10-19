namespace FluidX.TextModels;

/// <summary>
/// A single reversible operation applied to a known document model.
/// </summary>
public interface IUndoRedoElement
{
    void Undo();
    void Redo();
}

/// <summary>
/// The undo and redo history of reversible operations for a single document model.
/// </summary>
/// <remarks>This class is not thread-safe; synchronize access if used from multiple threads.</remarks>
public class UndoRedoStack
{
    public Stack<IUndoRedoElement> Past = [];
    public Stack<IUndoRedoElement> Future = [];

    public bool CanUndo => Past.Count > 0;
    public bool CanRedo => Future.Count > 0;
    public bool HasElements => Past.Count > 0 || Future.Count > 0;

    /// <summary>
    /// Returns the most recent <see cref="IUndoRedoElement"/>, if available.
    /// </summary>
    /// <returns>The last element in the undo stack, or <see langword="null"/> if there are no elements to undo or if there are
    /// pending redo operations.</returns>
    public IUndoRedoElement? GetLastElement()
    {
        if (Future.Count != 0) return null;
        if (Past.Count == 0) return null;
        return Past.Peek();
    }

    public void PushElement(IUndoRedoElement element)
    {
        Future.Clear();
        Past.Push(element);
    }

    public void Undo()
    {
        if (Past.Count == 0) return;
        var element = Past.Pop();
        element.Undo();
        Future.Push(element);
    }

    public void Redo()
    {
        if (Future.Count == 0) return;
        var element = Future.Pop();
        element.Redo();
        Past.Push(element);
    }
}

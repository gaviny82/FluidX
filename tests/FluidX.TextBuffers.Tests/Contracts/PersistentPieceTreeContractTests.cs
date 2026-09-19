using FluidX.TextBuffers.Tests.Infrastructure;

namespace FluidX.TextBuffers.Tests.Contracts;

[TestClass]
public sealed class PersistentPieceTreeReadOnlyContractTests : ReadOnlyTextBufferContractTests
{
    protected override IReadOnlyTextBuffer Create(string text) => TextBufferFactory.PersistentPieceTree(text);
}

[TestClass]
public sealed class PersistentPieceTreeMutationContractTests : TextBufferMutationContractTests
{
    protected override ITextBuffer Create(string text) => TextBufferFactory.PersistentPieceTree(text);
}

[TestClass]
public sealed class PersistentPieceTreeSnapshotContractTests : TextSnapshotContractTests
{
    protected override ITextBuffer CreateBuffer(string text) => TextBufferFactory.PersistentPieceTree(text);
}

using FluidX.TextBuffers.Tests.Infrastructure;

namespace FluidX.TextBuffers.Tests.Contracts;

[TestClass]
public sealed class TextBufferEqualityContractTests
{
    [TestMethod]
    public void Equality_IsReflexiveSymmetricAndTransitive()
    {
        foreach (Func<string, ITextBuffer> create in Factories())
        {
            IReadOnlyTextBuffer a = create("a\r\n😀");
            IReadOnlyTextBuffer b = create("a\r\n😀");
            IReadOnlyTextBuffer c = create("a\r\n😀");
            Assert.IsTrue(a.Equals(a));
            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(b.Equals(a));
            Assert.IsTrue(b.Equals(c));
            Assert.IsTrue(a.Equals(c));
            Assert.IsFalse(a.Equals(null));
        }
    }

    [TestMethod]
    public void Equality_UsesExactRawContent()
    {
        foreach (Func<string, ITextBuffer> create in Factories())
        {
            IReadOnlyTextBuffer value = create("abc\n");
            foreach (string different in new[] { "abc", "abc\r\n", "xbc\n", "axc\n", "abx\n" })
                Assert.IsFalse(value.Equals(create(different)));
        }
    }

    [TestMethod]
    public void Equality_IsIndependentOfConstructionHistory()
    {
        foreach (Func<string, ITextBuffer> create in Factories())
        {
            ITextBuffer edited = create("ac");
            edited.ApplyEdits([new(edited.GetRangeAt(1, 0), "b")]);
            IReadOnlyTextBuffer direct = create("abc");
            Assert.IsTrue(edited.Equals(direct));
            Assert.IsTrue(direct.Equals(edited));
        }
    }

    private static Func<string, ITextBuffer>[] Factories() =>
        [TextBufferFactory.LineArray, TextBufferFactory.PieceTree];
}

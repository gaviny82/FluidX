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
        [TextBufferFactory.LineArray, TextBufferFactory.PieceTree, TextBufferFactory.PersistentPieceTree];

    [TestMethod]
    public void Equality_CrossesImplementationsAndSnapshots()
    {
        foreach (string text in new[] { "", "abc\r\n😀\r" })
        {
            ITextBuffer[] sources = Factories().Select(create => create(text)).ToArray();
            IReadOnlyTextBuffer[] values = [.. sources, .. sources.Select(source => source.CreateSnapshot())];
            foreach (var left in values)
            foreach (var right in values)
                Assert.IsTrue(left.Equals(right));

            foreach (var source in sources)
                source.ApplyEdits([new(source.GetRangeAt(0, 0), "changed")]);
            foreach (var source in sources)
            foreach (var snapshot in values.OfType<ITextSnapshot>())
            {
                Assert.IsFalse(source.Equals(snapshot));
                Assert.IsFalse(snapshot.Equals(source));
                Assert.IsFalse(snapshot.Equals(null));
            }
        }
    }
}

using FluidX.TextBeffers.Benchmarks.Utils;

namespace FluidX.TextBuffers.Tests.BenchmarkUtils;

[TestClass]
public sealed class BenchmarkEditGeneratorTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("\r\n\r\n\r\n")]
    [DataRow("abc\r\ndef\nxyz\rmore\r\n")]
    public void GeneratedEdits_RespectCrlfEndpointsAndPreserveExpectedLength(string original)
    {
        for (int length = 1; length <= 10; length++)
        {
            string text = original;
            foreach (var edit in EditHelper.PreGenerateRandomEdits(original, new Random(42), 100, length))
            {
                AssertValid(text, edit.InsertOffset);
                AssertValid(text, edit.InsertOffset + edit.DeleteLength);
                text = text.Remove(edit.InsertOffset, edit.DeleteLength).Insert(edit.InsertOffset, edit.Text);
                Assert.AreEqual(original.Length, text.Length);
            }
        }
        string sequential = original;
        foreach (var edit in EditHelper.PreGenerateSequentialEdits(original, new Random(42), 100))
        {
            AssertValid(sequential, edit.InsertOffset);
            sequential = sequential.Insert(edit.InsertOffset, edit.Text);
        }
    }

    private static void AssertValid(string text, int offset)
    {
        Assert.IsTrue(offset >= 0 && offset <= text.Length);
        Assert.IsFalse(offset > 0 && offset < text.Length && text[offset - 1] == '\r' && text[offset] == '\n');
    }
}

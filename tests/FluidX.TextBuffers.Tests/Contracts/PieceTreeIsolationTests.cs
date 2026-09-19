using FluidX.TextBuffers.Tests.Infrastructure;

namespace FluidX.TextBuffers.Tests.Contracts;

[TestClass]
public sealed class PieceTreeIsolationTests
{
    [TestMethod]
    public async Task ConcurrentIndependentBuffers_MatchStringReferences()
    {
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            string expected = "one\ntwo\nthree";
            var buffer = TextBufferFactory.PieceTree(expected);
            var random = new Random(7319 + worker);
            string[] insertions = ["", "x", "\n", "a\nb", "😀"];
            for (int step = 0; step < 200; step++)
            {
                int offset = random.Next(expected.Length + 1);
                int length = random.Next(Math.Min(8, expected.Length - offset) + 1);
                string inserted = insertions[random.Next(insertions.Length)];
                buffer.ApplyEdits([new(buffer.GetRangeAt(offset, length), inserted)]);
                expected = expected.Remove(offset, length).Insert(offset, inserted);
                BufferAssertions.Matches(expected, buffer);
            }
            buffer.ApplyEdits([new(buffer.GetRangeAt(0, buffer.Length), "")]);
            BufferAssertions.Matches("", buffer);
            buffer.ApplyEdits([new(buffer.GetRangeAt(0, 0), "reused\nbuffer")]);
            BufferAssertions.Matches("reused\nbuffer", buffer);
        })));
    }
}

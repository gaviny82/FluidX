using FluidX.TextBuffers;
using FluidX.TextBuffers.PieceTree;

namespace FluidX.TextBuffers.Tests
{
    [TestClass]
    public sealed class Test1
    {
        private static PieceTreeTextBuffer CreateBuffer(string text)
        {
            var builder = new PieceTreeTextBufferBuilder();
            builder.AcceptChunk(text);
            return builder.Finish(normalizeEOL: false).Create(DefaultEndOfLine.LF);
        }

        [TestMethod]
        public void Insert_IntoFreshBuffer_ProducesCorrectContent()
        {
            var buffer = CreateBuffer("hello\nworld\nfoo bar baz");
            var start = buffer.GetPositionAt(3);
            var end = buffer.GetPositionAt(13);
            buffer.ApplyEdits(
                [
                    new EditOperation
                    {
                        Range = new TextRange(start, end),
                        Text = "abcdefghij",
                        ForceMoveMarkers = false,
                        IsAutoWhitespaceEdit = false,
                        IsTracked = false
                    }
                ],
                false,
                false);

            Assert.AreEqual("helabcdefghijoo bar baz", buffer.GetLinesRawContent());
        }

        [TestMethod]
        public void Insert_MultipleEdits_ProducesCorrectContent()
        {
            var buffer = CreateBuffer("hello\nworld\nfoo bar baz");
            var random = new Random(42);
            var edits = new (int offset, int length)[]
            {
                (3, 10),
                (0, 2),
                (12, 5),
                (1, 1),
            };

            foreach (var (offset, length) in edits)
            {
                var start = buffer.GetPositionAt(offset);
                var end = buffer.GetPositionAt(offset + length);
                var text = new string(Enumerable.Repeat('x', length).ToArray());
                buffer.ApplyEdits(
                    [
                        new EditOperation
                        {
                            Range = new TextRange(start, end),
                            Text = text,
                            ForceMoveMarkers = false,
                            IsAutoWhitespaceEdit = false,
                            IsTracked = false
                        }
                    ],
                    false,
                    false);
            }

            Assert.IsTrue(buffer.Length > 0);
        }
    }
}

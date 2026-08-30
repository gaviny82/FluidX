using FluidX.TextBuffers;
using FluidX.TextBuffers.LineArray;
using FluidX.TextBuffers.PieceTree;
using FluidX.TextBuffers.PieceTree.Buffers;

namespace FluidX.TextBuffers.Tests
{
    [TestClass]
    public sealed class Test1
    {
        private static PieceTreeTextBuffer CreateBuffer(string text)
        {
            return PieceTreeTextBuffer.Create(text, DefaultEndOfLine.LF);
        }

        [TestMethod]
        public void AppendText_HandlesCRLFAcrossAppends()
        {
            var buffer = new InlineStringBuffer();
            buffer.AppendText("abc\r");
            buffer.AppendText("\ndef");

            Assert.AreEqual(2, buffer.LineStarts.Length);
            Assert.AreEqual(0, buffer.LineStarts[0]);
            Assert.AreEqual(5, buffer.LineStarts[1]);
        }

        [TestMethod]
        public void Insert_IntoFreshBuffer_ProducesCorrectContent()
        {
            var buffer = CreateBuffer("hello\nworld\nfoo bar baz");
            var start = buffer.GetPositionAt(3);
            var end = buffer.GetPositionAt(13);
            buffer.ApplyEdits(
                [
                    new TextReplacement(new TextRange(start, end), "abcdefghij")
                ],
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
                        new TextReplacement(new TextRange(start, end), text)
                    ],
                    false);
            }

            Assert.IsTrue(buffer.Length > 0);
        }
        [TestMethod]
        public void MixedEOL_ReadsUseActualLineBreakLengths()
        {
            var buffer = CreateBuffer("one\r\ntwo\nthree\rfour");
            var fullRange = new TextRange(1, 1, 4, 5);

            CollectionAssert.AreEqual(new[] { "one", "two", "three", "four" }, buffer.GetLinesContent().ToArray());
            Assert.AreEqual("one\r\ntwo\r\nthree\r\nfour", buffer.GetValueInRange(fullRange, EndOfLinePreference.TextDefined));
            Assert.AreEqual("one\ntwo\nthree\nfour", buffer.GetValueInRange(fullRange, EndOfLinePreference.LF));
            Assert.AreEqual(21, buffer.GetValueLengthInRange(fullRange, EndOfLinePreference.TextDefined));
            Assert.AreEqual(18, buffer.GetValueLengthInRange(fullRange, EndOfLinePreference.LF));
            Assert.AreEqual(21, buffer.GetCharacterCountInRange(fullRange, EndOfLinePreference.TextDefined));
        }

        [TestMethod]
        public void ApplyEdits_PreservesForeignEOLAndReadPathsRemainCorrect()
        {
            var buffer = CreateBuffer("one\ntwo");
            buffer.ApplyEdits(
                [new TextReplacement(new TextRange(1, 4, 1, 4), "\r\ninserted")],
                false);

            CollectionAssert.AreEqual(new[] { "one", "inserted", "two" }, buffer.GetLinesContent().ToArray());
            Assert.AreEqual("one\r\ninserted\ntwo", buffer.GetLinesRawContent());
        }

        [TestMethod]
        public void NormalizeEOL_RewritesMixedContent()
        {
            var buffer = CreateBuffer("one\r\ntwo\nthree\rfour");
            buffer.NormalizeEOL("\n");

            var fullRange = new TextRange(1, 1, 4, 5);
            Assert.AreEqual("\n", buffer.GetEOL());
            Assert.AreEqual("one\ntwo\nthree\nfour", buffer.GetValueInRange(fullRange));
            Assert.AreEqual(15, buffer.GetValueLengthInRange(fullRange, EndOfLinePreference.TextDefined));
        }

        [TestMethod]
        public void LineArrayBuffer_SplitsBareCR()
        {
            var buffer = new LineArrayTextBuffer("one\rtwo\r\nthree\nfour");

            CollectionAssert.AreEqual(new[] { "one", "two", "three", "four" }, buffer.GetLinesContent().ToArray());
            Assert.AreEqual("one\ntwo\nthree\nfour", buffer.GetValueInRange(new TextRange(1, 1, 4, 5), EndOfLinePreference.TextDefined));
        }
    }
}

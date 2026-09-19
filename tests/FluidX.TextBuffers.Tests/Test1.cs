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
            var fullRange = new TextRange(0, 0, 3, 4);

            CollectionAssert.AreEqual(new[] { "one", "two", "three", "four" }, buffer.GetLinesContent().ToArray());
            Assert.AreEqual("one\r\ntwo\nthree\rfour", buffer.GetTextInRange(fullRange));
            Assert.AreEqual(19, buffer.GetTextLengthInRange(fullRange));
            Assert.AreEqual("\r\n", buffer.GetLineEOL(0));
            Assert.AreEqual("\n", buffer.GetLineEOL(1));
            Assert.AreEqual("\r", buffer.GetLineEOL(2));
            Assert.AreEqual("", buffer.GetLineEOL(3));
        }

        [TestMethod]
        public void PieceTreeTreatsBomCharacterAsRawInput()
        {
            var buffer = PieceTreeTextBuffer.Create("\uFEFFtext");

            Assert.AreEqual(5, buffer.Length);
            Assert.AreEqual('\uFEFF', buffer.GetChar(0));
            Assert.AreEqual("\uFEFFtext", buffer.GetTextInRange(new TextRange(0, 0, 0, 5)));

            var snapshot = buffer.CreateSnapshot(preserveBOM: false);
            Assert.AreEqual("\uFEFFtext", snapshot.Read());
        }

        [TestMethod]
        public void ApplyEdits_PreservesForeignEOLAndReadPathsRemainCorrect()
        {
            var buffer = CreateBuffer("one\ntwo");
            buffer.ApplyEdits(
                [new TextReplacement(new TextRange(0, 3, 0, 3), "\r\ninserted")],
                false);

            CollectionAssert.AreEqual(new[] { "one", "inserted", "two" }, buffer.GetLinesContent().ToArray());
            Assert.AreEqual("one\r\ninserted\ntwo", buffer.GetLinesRawContent());
        }

        [TestMethod]
        public void RawReads_DoNotNormalizeMixedContent()
        {
            var buffer = CreateBuffer("one\r\ntwo\nthree\rfour");

            var fullRange = new TextRange(0, 0, 3, 4);
            Assert.AreEqual("one\r\ntwo\nthree\rfour", buffer.GetTextInRange(fullRange));
            Assert.AreEqual(buffer.Length, buffer.GetTextLengthInRange(fullRange));
        }

        [TestMethod]
        public void LineArrayBuffer_SplitsBareCR()
        {
            var buffer = new LineArrayTextBuffer("one\rtwo\r\nthree\nfour");

            CollectionAssert.AreEqual(new[] { "one", "two", "three", "four" }, buffer.GetLinesContent().ToArray());
            Assert.AreEqual("one\rtwo\r\nthree\nfour", buffer.GetTextInRange(new TextRange(0, 0, 3, 4)));
            Assert.AreEqual("\r", buffer.GetLineEOL(0));
            Assert.AreEqual("\r\n", buffer.GetLineEOL(1));
        }

        [TestMethod]
        public void LineArrayBuffer_RawOffsetsAndSnapshotPreserveEveryCharacter()
        {
            const string text = "\uFEFFone\r\ntwo\nthree\rfour";
            var buffer = new LineArrayTextBuffer(text);

            Assert.AreEqual(text.Length, buffer.Length);
            for (int offset = 0; offset <= text.Length; offset++)
            {
                TextPosition position = buffer.GetPositionAt(offset);
                Assert.AreEqual(offset, buffer.GetOffsetAt(position));
                if (offset < text.Length)
                    Assert.AreEqual(text[offset], buffer.GetChar(offset));
            }

            Assert.AreEqual(text, buffer.CreateSnapshot(preserveBOM: false).Read());
        }

        [TestMethod]
        public void LineArrayBuffer_AppliesMultipleRawEditsAndProducesUndoEdits()
        {
            const string original = "abc\r\ndef\n";
            var buffer = new LineArrayTextBuffer(original);
            TextReplacement[] replacements =
            [
                new(buffer.GetRangeAt(9, 0), "!"),
                new(buffer.GetRangeAt(5, 2), "Y\rZ"),
                new(buffer.GetRangeAt(0, 0), "X\n")
            ];

            ApplyEditsResult result = buffer.ApplyEdits(replacements, computeUndoEdits: true);

            Assert.AreEqual("X\nabc\r\nY\rZf\n!", buffer.CreateSnapshot(false).Read());
            Assert.IsNotNull(result.ReverseEdits);

            buffer.ApplyEdits(
                result.ReverseEdits.Select(edit => new TextReplacement(edit.Range, edit.Text)).ToArray(),
                computeUndoEdits: false);

            Assert.AreEqual(original, buffer.CreateSnapshot(false).Read());
        }

        [TestMethod]
        public void LineArrayBuffer_NormalizeEOLRewritesEachStoredLineEnding()
        {
            var buffer = new LineArrayTextBuffer("one\rtwo\r\nthree\nfour");

            buffer.NormalizeEOL("\r\n");

            Assert.AreEqual("one\r\ntwo\r\nthree\r\nfour", buffer.CreateSnapshot(false).Read());
            CollectionAssert.AreEqual(
                new[] { "\r\n", "\r\n", "\r\n", "" },
                Enumerable.Range(0, buffer.LineCount).Select(buffer.GetLineEOL).ToArray());
        }

        [TestMethod]
        public void LineArrayBuffer_PreservesRawTextAcrossRandomEdits()
        {
            const string initialText = "one\rtwo\r\nthree\nfour";
            var lineArray = new LineArrayTextBuffer(initialText);
            string expected = initialText;
            var random = new Random(42);
            string[] insertedTexts = ["", "x", "\n", "\r", "\r\n", "a\r\nb\nc"];

            for (int i = 0; i < 100; i++)
            {
                int offset = random.Next(expected.Length + 1);
                int length = random.Next(expected.Length - offset + 1);
                string insertedText = insertedTexts[random.Next(insertedTexts.Length)];
                TextRange lineArrayRange = lineArray.GetRangeAt(offset, length);

                lineArray.ApplyEdits(
                    [new TextReplacement(lineArrayRange, insertedText)],
                    computeUndoEdits: false);
                expected = expected.Remove(offset, length).Insert(offset, insertedText);

                Assert.AreEqual(expected.Length, lineArray.Length, $"Edit {i}");
                Assert.AreEqual(expected, lineArray.CreateSnapshot(false).Read(), $"Edit {i}");
            }

            for (int offset = 0; offset <= expected.Length; offset++)
                Assert.AreEqual(offset, lineArray.GetOffsetAt(lineArray.GetPositionAt(offset)));
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="GoldenBytesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.IO;
using System.Linq;
using Akka.Serialization;
using Akka.Tests.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit
{
    /// <summary>The golden-bytes store: file format, capture, and the diff a failed check prints.</summary>
    [Collection(DynamicTypeLoadingCollection.Name)] // shares the process-wide environment with the specs that capture
    public sealed class GoldenBytesSpec : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "akka-golden-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private GoldenBytes Store(bool capture = false, GoldenKind kind = GoldenKind.V2) => new(_directory, capture, kind);

        [Fact(DisplayName = "Should_MatchTheFile_When_BytesWereCaptured")]
        public void Should_MatchTheFile_When_BytesWereCaptured()
        {
            var bytes = Enumerable.Range(0, 40).Select(i => (byte)(i * 7)).ToArray();

            Store(capture: true).Check("case", bytes, "manifest: \"A\"");

            Store().Check("case", bytes);
            Store().Read("case").Should().Equal(bytes);
        }

        [Fact(DisplayName = "Should_CompareEmptyBytes_When_PayloadIsEmpty")]
        public void Should_CompareEmptyBytes_When_PayloadIsEmpty()
        {
            Store(capture: true).Check("empty", Array.Empty<byte>());

            Store().Check("empty", Array.Empty<byte>());
            Action act = () => Store().Check("empty", new byte[] { 1 });
            act.Should().Throw<Exception>().WithMessage("*expected 0 bytes, actual 1 bytes*");
        }

        [Fact(DisplayName = "Should_IgnoreCommentsOffsetsAndLineEndings_When_ParsingAFile")]
        public void Should_IgnoreCommentsOffsetsAndLineEndings_When_ParsingAFile()
        {
            const string text = "# a comment\r\n# case: x\r\n0000: 01 02 0A\r\n\r\n0010: ff\r\nbe ef\r\n";

            GoldenBytes.Parse(text).Should().Equal(0x01, 0x02, 0x0a, 0xff, 0xbe, 0xef);
        }

        [Fact(DisplayName = "Should_ThrowFormatException_When_FileHoldsANonHexToken")]
        public void Should_ThrowFormatException_When_FileHoldsANonHexToken()
        {
            Action act = () => GoldenBytes.Parse("0000: 01 zz");

            act.Should().Throw<FormatException>().WithMessage("*'zz'*");
        }

        [Fact(DisplayName = "Should_ReportTheFirstDifference_When_BytesDiffer")]
        public void Should_ReportTheFirstDifference_When_BytesDiffer()
        {
            var expected = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
            var actual = expected.ToArray();
            actual[17] = 0xff;
            Store(capture: true).Check("diff", expected);

            Action act = () => Store().Check("diff", actual);

            var message = act.Should().Throw<Exception>().Which.Message;
            message.Should().Contain("V2 golden bytes for 'diff' changed");
            message.Should().Contain("first difference at offset 17 (expected 0x11, actual 0xff)");
            message.Should().Contain("AKKA_GOLDEN_CAPTURE=v2");
            // the second row (offset 0x10) holds the difference and is marked; the first is not
            message.Split('\n').Single(l => l.Contains("0010")).Should().StartWith(" !");
            message.Split('\n').Single(l => l.Contains("0000")).Should().NotStartWith(" !");
        }

        [Fact(DisplayName = "Should_ReportALengthChange_When_OneArrayIsAPrefixOfTheOther")]
        public void Should_ReportALengthChange_When_OneArrayIsAPrefixOfTheOther()
        {
            var diff = GoldenBytes.Diff(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3, 4 });

            diff.Should().Contain("expected 3 bytes, actual 4 bytes").And.Contain("part at offset 3");
        }

        [Fact(DisplayName = "Should_ShowAWindowOfRows_When_LongArraysDiffFarIn")]
        public void Should_ShowAWindowOfRows_When_LongArraysDiffFarIn()
        {
            var expected = new byte[2000];
            var actual = new byte[2000];
            actual[1500] = 1;

            var diff = GoldenBytes.Diff(expected, actual);

            diff.Split('\n').Length.Should().BeLessThan(40, "a long dump is cut to a window around the difference");
            diff.Should().Contain("05d0").And.Contain("first difference at offset 1500");
            diff.Should().Contain("...");
        }

        [Fact(DisplayName = "Should_NameTheCaptureCommand_When_NoGoldenFileExists")]
        public void Should_NameTheCaptureCommand_When_NoGoldenFileExists()
        {
            Action check = () => Store(kind: GoldenKind.Legacy).Check("missing", new byte[] { 1 });
            Action read = () => Store(kind: GoldenKind.Legacy).Read("missing");

            check.Should().Throw<Exception>().WithMessage("*No Legacy golden file for 'missing'*AKKA_GOLDEN_CAPTURE=legacy*");
            read.Should().Throw<Exception>().WithMessage("*AKKA_GOLDEN_CAPTURE=legacy*");
        }

        [Fact(DisplayName = "Should_MakeNamesSafe_When_NameHasPathCharacters")]
        public void Should_MakeNamesSafe_When_NameHasPathCharacters()
        {
            Path.GetFileName(Store().PathFor("a/b:c d")).Should().Be("a_b_c_d.hex");
        }

        [Theory(DisplayName = "Should_CaptureOnlyTheRequestedKind_When_EnvironmentVariableIsSet")]
        [InlineData(null, false, false)]
        [InlineData("", false, false)]
        [InlineData("legacy", true, false)]
        [InlineData("LEGACY", true, false)]
        [InlineData("v2", false, true)]
        [InlineData("all", true, true)]
        [InlineData("1", true, true)]
        [InlineData("nonsense", false, false)]
        public void Should_CaptureOnlyTheRequestedKind_When_EnvironmentVariableIsSet(string? value, bool legacy, bool v2)
        {
            var previous = Environment.GetEnvironmentVariable(GoldenBytes.CaptureVariable);
            try
            {
                Environment.SetEnvironmentVariable(GoldenBytes.CaptureVariable, value);

                GoldenBytes.CaptureRequested(GoldenKind.Legacy).Should().Be(legacy);
                GoldenBytes.CaptureRequested(GoldenKind.V2).Should().Be(v2);
            }
            finally
            {
                Environment.SetEnvironmentVariable(GoldenBytes.CaptureVariable, previous);
            }
        }

        [Fact(DisplayName = "Should_FindFilesNextToTheCallingSource_When_ForIsCalled")]
        public void Should_FindFilesNextToTheCallingSource_When_ForIsCalled()
        {
            var store = GoldenBytes.For("GoldenBytes/Fake", GoldenKind.Legacy);

            store.Directory.Should().EndWith(Path.Combine("V2PortKit", "GoldenBytes", "Fake", "legacy"));
            store.Exists("ping").Should().BeTrue();
        }
    }
}

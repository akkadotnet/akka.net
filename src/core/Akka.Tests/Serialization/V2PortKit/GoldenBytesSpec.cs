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
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit
{
    public sealed class GoldenBytesSpec : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "akka-golden-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        [Fact(DisplayName = "Should_MatchTheFile_When_BytesWereCaptured")]
        public void Should_MatchTheFile_When_BytesWereCaptured()
        {
            var bytes = Enumerable.Range(0, 40).Select(i => (byte)(i * 7)).ToArray();

            new GoldenBytes(_directory, capture: true).Check("v2/case", bytes);

            new GoldenBytes(_directory, capture: false).Check("v2/case", bytes);
        }

        [Fact(DisplayName = "Should_ShowBothRows_When_BytesDiffer")]
        public void Should_ShowBothRows_When_BytesDiffer()
        {
            var expected = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
            var actual = expected.ToArray();
            actual[17] = 0xff;
            new GoldenBytes(_directory, capture: true).Check("case", expected);

            Action act = () => new GoldenBytes(_directory, capture: false).Check("case", actual);

            act.Should().Throw<Exception>().WithMessage("*expected 20 bytes, actual 20 bytes*! 10 11 12 13 *| 10 ff 12 13*");
        }

        [Fact(DisplayName = "Should_Fail_When_GoldenFileIsMissing")]
        public void Should_Fail_When_GoldenFileIsMissing()
        {
            Action act = () => new GoldenBytes(_directory, capture: false).Check("nope", new byte[] { 1 });

            act.Should().Throw<Exception>().WithMessage("*No golden file*AKKA_GOLDEN_CAPTURE=1*");
        }
    }
}

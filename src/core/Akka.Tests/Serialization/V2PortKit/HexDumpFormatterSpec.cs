// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Serialization.V2PortKit;

public sealed class HexDumpFormatterSpec
{
    [Theory(DisplayName = "Should_RenderTheManifestWithoutTrailingWhitespace_When_AWireDumpIsFormatted")]
    [InlineData("", "manifest:")]
    [InlineData("a", "manifest: a")]
    public void Should_RenderTheManifestWithoutTrailingWhitespace_When_AWireDumpIsFormatted(string manifest, string expectedHeader)
    {
        var dump = HexDumpFormatter.Format("case", "Message", manifest, 6, [0, 255]);
        Assert.Contains("\n" + expectedHeader + "\n", dump);
        Assert.Contains("0000  00 ff", dump);
        foreach (var line in dump.Split('\n'))
            Assert.Equal(line.TrimEnd(), line);
    }
}

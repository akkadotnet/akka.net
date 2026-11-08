// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
namespace Akka.AOT.SerializationV2;

internal static class StreamProtocolFrozenFixtures
{
    internal sealed record Fixture(string Name, string Manifest, string LegacyHex, string V2Hex);
    internal static readonly Fixture[] Cases =
    [
        new("completed-max", "D", "08ffffffffffffffff7f", "8101cf7fffffffffffffff"),
        new("completed", "D", "", "810100"),
        new("demand-max", "B", "08ffffffffffffffff7f", "8101cf7fffffffffffffff"),
        new("demand", "B", "0801", "810101"),
        new("failure-empty", "C", "", "8101a0"),
        new("failure", "C", "0a0a6661696c656420e29883", "8101aa6661696c656420e29883"),
        new("handshake-empty", "G", "0a00", "8101a0"),
        new("handshake", "G", "0a150a13616b6b613a2f2f616c6c2d73797374656d732f", "8101b3616b6b613a2f2f616c6c2d73797374656d732f"),
        new("next-bytes", "A", "080312080a0400017fff1004", "8201030283010402a003c40400017fff"),
        new("next-empty-string", "A", "08ffffffffffffffff7f122910111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962", "8201cf7fffffffffffffff0283011102d92553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696203c400"),
        new("next-int", "A", "0804122e0a041100000010111a2453797374656d2e496e7433322c2053797374656d2e507269766174652e436f72654c6962", "8201040283011102d92453797374656d2e496e7433322c2053797374656d2e507269766174652e436f72654c696203c40411000000"),
        new("next-string", "A", "12320a077061796c6f616410111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962", "8201000283011102d92553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c696203c4077061796c6f6164"),
    ];
}

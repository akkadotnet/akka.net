using System.Globalization;
using System.Text;
using System.Text.Json;
using Akka.Actor;
using Akka.Serialization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Akka.Reminders.Tests.Golden;

// This file is compiled twice: by the test project and by the 1.5.73 generator in
// Fixtures/golden-1.5.73/generator. Keep it free of test-framework and 1.6-only APIs.
// Stored manifests embed the assembly name, so the generator is built as "Akka.Reminders.Tests".

/// <summary>Plain POCO. Goes to the default JSON serializer.</summary>
public sealed record OrderDue(string OrderId, int Attempt, DateTimeOffset At, string[] Tags, OrderLine Line);

public sealed record OrderLine(string Sku, int Quantity, long PriceCents);

/// <summary>Custom payload stored under manifest <c>rp-v1</c>, like a consumer with its own serializer.</summary>
public sealed record NotePayload(string Text, string Channel, int Priority);

/// <summary>Custom payload stored under manifest <c>rp-v2</c>.</summary>
public sealed record FollowUpPayload(string Text, string Channel, int Priority, string[] Tags, DateTimeOffset? NotBefore);

/// <summary>Payload for the type-manifest <see cref="Serializer"/> (IncludeManifest = true).</summary>
public sealed record TypedPayload(string Value, int Number);

/// <summary>Payload for the no-manifest <see cref="Serializer"/> (IncludeManifest = false).</summary>
public sealed record PlainPayload(string Value);

/// <summary>Consumer-style serializer: custom id, short string manifests.</summary>
public sealed class ReminderPayloadSerializer : SerializerWithStringManifest
{
    public const string NoteManifest = "rp-v1";
    public const string FollowUpManifest = "rp-v2";

    public ReminderPayloadSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override int Identifier => 7001;

    public override string Manifest(object o) => o switch
    {
        NotePayload => NoteManifest,
        FollowUpPayload => FollowUpManifest,
        _ => throw new ArgumentException($"Unsupported type [{o.GetType()}]")
    };

    public override byte[] ToBinary(object obj) => obj switch
    {
        NotePayload n => JsonSerializer.SerializeToUtf8Bytes(n),
        FollowUpPayload f => JsonSerializer.SerializeToUtf8Bytes(f),
        _ => throw new ArgumentException($"Unsupported type [{obj.GetType()}]")
    };

    public override object FromBinary(byte[] bytes, string manifest) => manifest switch
    {
        NoteManifest => JsonSerializer.Deserialize<NotePayload>(bytes)!,
        FollowUpManifest => JsonSerializer.Deserialize<FollowUpPayload>(bytes)!,
        _ => throw new ArgumentException($"Unknown manifest [{manifest}]")
    };
}

/// <summary>Plain <see cref="Serializer"/> that asks Akka to store the type name as the manifest.</summary>
public sealed class TypeManifestSerializer : Serializer
{
    public TypeManifestSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override int Identifier => 7002;

    public override bool IncludeManifest => true;

    public override byte[] ToBinary(object obj) => JsonSerializer.SerializeToUtf8Bytes((TypedPayload)obj);

    public override object FromBinary(byte[] bytes, System.Type type)
        => type == typeof(TypedPayload)
            ? JsonSerializer.Deserialize<TypedPayload>(bytes)!
            : throw new ArgumentException($"Unexpected type [{type}]");
}

/// <summary>Plain <see cref="Serializer"/> that stores no manifest.</summary>
public sealed class NoManifestSerializer : Serializer
{
    public NoManifestSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override int Identifier => 7003;

    public override bool IncludeManifest => false;

    public override byte[] ToBinary(object obj) => Encoding.UTF8.GetBytes(((PlainPayload)obj).Value);

    public override object FromBinary(byte[] bytes, System.Type? type)
        => new PlainPayload(Encoding.UTF8.GetString(bytes));
}

public enum GoldenKind
{
    Poco,
    String,
    Int,
    Long,
    Bytes,
    Bool,
    Double,
    CustomManifestV1,
    CustomManifestV2,
    TypeManifest,
    NoManifest,
    ProtoTimestamp,
    ProtoInt64
}

public enum GoldenProvider
{
    Local,
    Cluster
}

public static class GoldenConfig
{
    /// <summary>The custom serializers and their bindings. Needed by anything that reads or writes the fixture.</summary>
    public const string SerializerHocon = """
        akka.actor.serializers {
          golden-rp = "Akka.Reminders.Tests.Golden.ReminderPayloadSerializer, Akka.Reminders.Tests"
          golden-typed = "Akka.Reminders.Tests.Golden.TypeManifestSerializer, Akka.Reminders.Tests"
          golden-plain = "Akka.Reminders.Tests.Golden.NoManifestSerializer, Akka.Reminders.Tests"
        }
        akka.actor.serialization-bindings {
          "Akka.Reminders.Tests.Golden.NotePayload, Akka.Reminders.Tests" = golden-rp
          "Akka.Reminders.Tests.Golden.FollowUpPayload, Akka.Reminders.Tests" = golden-rp
          "Akka.Reminders.Tests.Golden.TypedPayload, Akka.Reminders.Tests" = golden-typed
          "Akka.Reminders.Tests.Golden.PlainPayload, Akka.Reminders.Tests" = golden-plain
        }
        """;

    /// <summary>Full HOCON the generator uses for a system on the given provider. Cluster systems never join anything.</summary>
    public static string For(GoldenProvider provider)
        => "akka.loglevel = WARNING\n" + (provider == GoldenProvider.Cluster
            ? SerializerHocon + """

              akka.actor.provider = cluster
              akka.remote.dot-netty.tcp.port = 0
              akka.remote.dot-netty.tcp.hostname = localhost
              """
            : SerializerHocon);

    public static string FileName(GoldenProvider provider)
        => provider == GoldenProvider.Cluster ? "golden-cluster.db" : "golden-local.db";
}

public static class GoldenPayloads
{
    private static readonly string[] Strings =
    [
        "hello",
        "",
        "Remind me to review the quarterly summary",
        "unicode: café ✓ 日本語",
        "{\"looks\":\"like json\"}",
        "line one\nline two\ttabbed \"quoted\""
    ];

    private static readonly int[] Ints = [7, 0, -1, 42, int.MaxValue, int.MinValue, 100_000];

    private static readonly long[] Longs = [9_000_000_000L, 0L, -9_000_000_000L, long.MaxValue, long.MinValue, 1L];

    private static readonly double[] Doubles = [1.5d, 0d, -2.25d, 1e300d, 0.1d, 3.141592653589793d];

    private static readonly DateTimeOffset Anchor = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The deterministic payload for a kind and sample index.</summary>
    public static object Sample(GoldenKind kind, int i) => kind switch
    {
        GoldenKind.Poco => new OrderDue(
            $"order-{i:D4}", i % 4, Anchor.AddHours(i), ["alpha", $"tag-{i % 5}"],
            new OrderLine($"sku-{i % 7:D3}", 1 + (i % 3), 1999L + i)),
        GoldenKind.String => Strings[i % Strings.Length],
        GoldenKind.Int => Ints[i % Ints.Length],
        GoldenKind.Long => Longs[i % Longs.Length],
        GoldenKind.Bytes => BytesSample(i),
        GoldenKind.Bool => i % 2 == 0,
        GoldenKind.Double => Doubles[i % Doubles.Length],
        GoldenKind.CustomManifestV1 => Note(i),
        GoldenKind.CustomManifestV2 => FollowUp(i),
        GoldenKind.TypeManifest => new TypedPayload($"typed-{i:D3}", i * 3),
        GoldenKind.NoManifest => new PlainPayload($"plain-{i:D3}"),
        GoldenKind.ProtoTimestamp => Timestamp.FromDateTimeOffset(Anchor.AddMinutes(i * 17)),
        GoldenKind.ProtoInt64 => new Int64Value { Value = 1_000_000_007L * (i + 1) },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static NotePayload Note(int i)
        => new($"Follow up on item {i:D3}", i % 2 == 0 ? "chat" : "email", i % 5);

    public static FollowUpPayload FollowUp(int i)
        => new($"Check status of task {i:D3}", i % 2 == 0 ? "chat" : "sms", i % 4,
            ["ops", $"batch-{i % 6}"], i % 3 == 0 ? Anchor.AddDays(i) : null);

    private static byte[] BytesSample(int i) => (i % 4) switch
    {
        0 => [1, 2, 3, 4],
        1 => [],
        2 => Enumerable.Range(0, 256).Select(b => (byte)b).ToArray(),
        _ => [0]
    };

    /// <summary>
    /// A canonical, comparable description of a payload: runtime type plus value.
    /// </summary>
    public static string Describe(object? o) => o switch
    {
        null => "<null>",
        byte[] b => "System.Byte[]:" + Convert.ToHexString(b),
        OrderDue d => $"{typeof(OrderDue).FullName}({d.OrderId},{d.Attempt},{d.At:O},[{string.Join("|", d.Tags)}],{d.Line})",
        FollowUpPayload f => $"{typeof(FollowUpPayload).FullName}({f.Text},{f.Channel},{f.Priority},[{string.Join("|", f.Tags)}],{f.NotBefore:O})",
        IMessage m => m.GetType().FullName + ":" + JsonFormatter.Default.Format(m),
        double d => "System.Double:" + d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => o.GetType().FullName + ":" + f.ToString(null, CultureInfo.InvariantCulture),
        _ => o.GetType().FullName + ":" + o
    };
}

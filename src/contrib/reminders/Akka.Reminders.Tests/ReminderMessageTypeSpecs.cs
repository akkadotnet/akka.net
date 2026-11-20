using System.Text;
using Akka.Actor;
using Akka.Hosting;
using Akka.Reminders.Sharding;
using Akka.Serialization;
using FluentAssertions;

namespace Akka.Reminders.Tests;

/// <summary>
/// Unit specs for <see cref="ReminderMessageTypes"/> and the envelope factory behind it.
/// Each spec uses its own message type, because registration is process-wide.
/// </summary>
public class ReminderMessageTypeSpecs
{
    private static readonly ReminderEntity Entity = new("billing", "customer-1");
    private static readonly ReminderKey Key = new("retry");
    private static readonly DateTimeOffset Due = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReminderDeadline Deadline = new(Due.AddMinutes(5));

    public sealed record RegisteredMessage(string Text);

    public sealed record UnregisteredMessage(string Text);

    public sealed record FallbackMessage(string Text);

    public sealed record BuilderMessage(string Text);

    [Fact(DisplayName = "Should_BuildTypedEnvelope_When_TypeIsRegistered_And_ReflectionIsUnavailable")]
    public void Should_BuildTypedEnvelope_When_TypeIsRegistered_And_ReflectionIsUnavailable()
    {
        ReminderMessageTypes.Register<RegisteredMessage>();

        var envelope = ReminderEnvelopeFactory.Create(
            Entity, Key, Due, Deadline, new RegisteredMessage("hi"), allowReflection: false);

        var typed = envelope.Should().BeOfType<ReminderEnvelope<RegisteredMessage>>().Subject;
        typed.Message.Text.Should().Be("hi");
        typed.Entity.Should().Be(Entity);
        typed.Key.Should().Be(Key);
        typed.DueTimeUtc.Should().Be(Due);
        typed.Deadline.Should().Be(Deadline);
        ReminderMessageTypes.IsRegistered(typeof(RegisteredMessage)).Should().BeTrue();
    }

    [Fact(DisplayName = "Should_ThrowWithRegistrationHint_When_TypeIsUnregistered_And_ReflectionIsUnavailable")]
    public void Should_ThrowWithRegistrationHint_When_TypeIsUnregistered_And_ReflectionIsUnavailable()
    {
        var act = () => ReminderEnvelopeFactory.Create(
            Entity, Key, Due, Deadline, new UnregisteredMessage("hi"), allowReflection: false);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain(typeof(UnregisteredMessage).FullName!)
            .And.Contain("WithReminderMessage<UnregisteredMessage>()")
            .And.Contain("Akka.DynamicTypeLoading");
        ReminderMessageTypes.IsRegistered(typeof(UnregisteredMessage)).Should().BeFalse();
    }

    [Fact(DisplayName = "Should_FallBackToReflection_When_TypeIsUnregistered_On_The_JIT")]
    public void Should_FallBackToReflection_When_TypeIsUnregistered_On_The_JIT()
    {
        ReminderEnvelopeFactory.IsReflectionFallbackAvailable.Should().BeTrue(
            "specs run on the JIT with Akka.DynamicTypeLoading at its default");
        ReminderEnvelopeFactory.CanCreate(typeof(FallbackMessage)).Should().BeTrue();

        var envelope = ReminderEnvelopeFactory.Create(Entity, Key, Due, Deadline, new FallbackMessage("hi"));

        envelope.Should().BeOfType<ReminderEnvelope<FallbackMessage>>()
            .Which.Message.Text.Should().Be("hi");
        ReminderMessageTypes.IsRegistered(typeof(FallbackMessage)).Should().BeFalse(
            "the reflection fallback does not count as a registration");
    }

    [Fact(DisplayName = "Should_RegisterType_When_WithReminderMessage_IsCalledOnEitherBuilder")]
    public void Should_RegisterType_When_WithReminderMessage_IsCalledOnEitherBuilder()
    {
        var local = new LocalReminderConfigurationBuilder();
        local.WithReminderMessage<BuilderMessage>().Should().BeSameAs(local);
        ReminderMessageTypes.IsRegistered(typeof(BuilderMessage)).Should().BeTrue();

        // Idempotent, and available on the clustered builder too.
        var clustered = new ReminderConfigurationBuilder();
        clustered.WithReminderMessage<BuilderMessage>().Should().BeSameAs(clustered);
        ReminderMessageTypes.IsRegistered(typeof(BuilderMessage)).Should().BeTrue();
    }
}

/// <summary>
/// Runs local reminders on the JIT with <c>Akka.DynamicTypeLoading</c> switched off, which is how
/// Native AOT behaves: no reflection fallback, so only registered message types work.
/// </summary>
/// <remarks>
/// The switch is process-wide, so this collection runs on its own, after every parallel spec,
/// and its fixture turns the switch off before the specs start and back on after they finish.
/// </remarks>
[CollectionDefinition(nameof(DynamicTypeLoadingOffCollection), DisableParallelization = true)]
public sealed class DynamicTypeLoadingOffCollection : ICollectionFixture<DynamicTypeLoadingOffFixture>;

public sealed class DynamicTypeLoadingOffFixture : IDisposable
{
    private readonly bool? _previous;

    public DynamicTypeLoadingOffFixture()
    {
        _previous = AppContext.TryGetSwitch(ReminderEnvelopeFactory.DynamicTypeLoadingSwitch, out var value)
            ? value
            : null;
        AppContext.SetSwitch(ReminderEnvelopeFactory.DynamicTypeLoadingSwitch, false);
    }

    // AppContext has no "unset"; no switch means enabled.
    public void Dispose() => AppContext.SetSwitch(ReminderEnvelopeFactory.DynamicTypeLoadingSwitch, _previous ?? true);
}

[Collection(nameof(DynamicTypeLoadingOffCollection))]
public class DynamicTypeLoadingOffSpecs : Akka.Hosting.TestKit.TestKit
{
    private readonly TestShardRegionResolver _resolver = new();

    // The fixture parameter makes xunit create it, and switch dynamic type loading off,
    // before this class builds its actor system.
    public DynamicTypeLoadingOffSpecs(DynamicTypeLoadingOffFixture fixture, ITestOutputHelper output)
        : base(output: output)
    {
    }

    public sealed record StrictPayload(string Text);

    public sealed record StrictUnregistered(string Text);

    public sealed class StrictPayloadSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public override int Identifier => 9302;

        public override string Manifest(object o) => o switch
        {
            StrictPayload => "p",
            StrictUnregistered => "u",
            _ => throw new ArgumentException($"Unsupported type [{o.GetType()}]", nameof(o))
        };

        public override byte[] ToBinary(object obj) => obj switch
        {
            StrictPayload p => Encoding.UTF8.GetBytes(p.Text),
            StrictUnregistered u => Encoding.UTF8.GetBytes(u.Text),
            _ => throw new ArgumentException($"Unsupported type [{obj.GetType()}]", nameof(obj))
        };

        public override object FromBinary(byte[] bytes, string manifest) => manifest switch
        {
            "p" => new StrictPayload(Encoding.UTF8.GetString(bytes)),
            "u" => new StrictUnregistered(Encoding.UTF8.GetString(bytes)),
            _ => throw new ArgumentException($"Unknown manifest [{manifest}]", nameof(manifest))
        };
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder
            .WithCustomSerializer(
                "strict-payload",
                [typeof(StrictPayload), typeof(StrictUnregistered)],
                system => new StrictPayloadSerializer(system))
            .WithLocalReminders(reminders => reminders
                .WithReminderMessage<StrictPayload>()
                .WithInMemoryStorage()
                .WithResolver(_ => _resolver)
                .WithSettings(new ReminderSettings
                {
                    MaxSlippage = TimeSpan.FromMilliseconds(100),
                    StorageTimeout = TimeSpan.FromSeconds(1)
                }));
    }

    [Fact(DisplayName = "Should_DeliverAndAck_When_MessageTypeIsRegistered_And_DynamicTypeLoadingIsOff")]
    public async Task Should_DeliverAndAck_When_MessageTypeIsRegistered_And_DynamicTypeLoadingIsOff()
    {
        ReminderEnvelopeFactory.IsReflectionFallbackAvailable.Should().BeFalse();

        var probe = CreateTestProbe();
        _resolver.RegisterShardRegion("billing", probe);
        var client = Sys.ReminderClient().CreateClient("billing", "customer-1");
        var ct = TestContext.Current.CancellationToken;

        var scheduled = await client.ScheduleSingleReminderAsync(
            new ReminderKey("retry"), DateTimeOffset.UtcNow.AddMilliseconds(200), new StrictPayload("hi"), ct: ct);
        scheduled.ResponseCode.Should().Be(ReminderScheduleResponseCode.Success, scheduled.Message);

        var envelope = await probe.ExpectMsgAsync<ReminderEnvelope<StrictPayload>>(
            TimeSpan.FromSeconds(5), cancellationToken: ct);
        envelope.Message.Text.Should().Be("hi");

        var ack = await client.AckAsync(envelope, ct);
        ack.ResponseCode.Should().Be(ReminderAckResponseCode.Success);
    }

    [Fact(DisplayName = "Should_RejectAtScheduleTime_When_MessageTypeIsUnregistered_And_DynamicTypeLoadingIsOff")]
    public async Task Should_RejectAtScheduleTime_When_MessageTypeIsUnregistered_And_DynamicTypeLoadingIsOff()
    {
        var probe = CreateTestProbe();
        _resolver.RegisterShardRegion("billing", probe);
        var client = Sys.ReminderClient().CreateClient("billing", "customer-2");
        var ct = TestContext.Current.CancellationToken;

        var scheduled = await client.ScheduleSingleReminderAsync(
            new ReminderKey("retry"), DateTimeOffset.UtcNow.AddMilliseconds(200), new StrictUnregistered("hi"), ct: ct);

        scheduled.ResponseCode.Should().Be(ReminderScheduleResponseCode.Error);
        scheduled.Message.Should().Contain("WithReminderMessage<StrictUnregistered>()");

        var stored = await client.ListRemindersAsync(ct);
        stored.Reminders.Should().BeEmpty("a rejected reminder must not reach storage");
        await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500), ct);
    }

    [Fact(DisplayName = "Should_RoundTripThroughSerializer_When_MessageTypeIsRegistered_And_DynamicTypeLoadingIsOff")]
    public void Should_RoundTripThroughSerializer_When_MessageTypeIsRegistered_And_DynamicTypeLoadingIsOff()
    {
        var envelope = new ReminderEnvelope<StrictPayload>(
            new ReminderEntity("billing", "customer-3"),
            new ReminderKey("retry"),
            DateTimeOffset.UtcNow,
            ReminderDeadline.Infinite,
            new StrictPayload("hi"));

        var serializer = (SerializerWithStringManifest)Sys.Serialization.FindSerializerFor(envelope);
        var bytes = serializer.ToBinary(envelope);
        var back = serializer.FromBinary(bytes, serializer.Manifest(envelope));

        back.Should().BeOfType<ReminderEnvelope<StrictPayload>>()
            .Which.Message.Text.Should().Be("hi");
    }

    [Fact(DisplayName = "Should_FailDeserializationWithRegistrationHint_When_MessageTypeIsUnregistered_And_DynamicTypeLoadingIsOff")]
    public void Should_FailDeserializationWithRegistrationHint_When_MessageTypeIsUnregistered_And_DynamicTypeLoadingIsOff()
    {
        var envelope = new ReminderEnvelope<StrictUnregistered>(
            new ReminderEntity("billing", "customer-4"),
            new ReminderKey("retry"),
            DateTimeOffset.UtcNow,
            ReminderDeadline.Infinite,
            new StrictUnregistered("hi"));

        var serializer = (SerializerWithStringManifest)Sys.Serialization.FindSerializerFor(envelope);
        var bytes = serializer.ToBinary(envelope);
        var act = () => serializer.FromBinary(bytes, serializer.Manifest(envelope));

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("WithReminderMessage<StrictUnregistered>()");
    }
}

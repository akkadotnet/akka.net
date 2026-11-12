// -----------------------------------------------------------------------
// <copyright file="TransportRestartCleanupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.Artery;
using Akka.Remote.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.MultiNode;

public sealed class TransportRestartCleanupConfig : MultiNodeConfig
{
    public RoleName First { get; }

    public TransportRestartCleanupConfig() => First = Role("first");
}

/// <summary>Uses the real runner/conductor to exercise failed restart ownership and recovery.</summary>
public sealed class TransportRestartCleanupSpec : MultiNodeSpec
{
    private ActorSystem? _created;

    public TransportRestartCleanupSpec() : this(new TransportRestartCleanupConfig()) { }

    private TransportRestartCleanupSpec(TransportRestartCleanupConfig config) : base(config, typeof(TransportRestartCleanupSpec)) { }

    protected override int InitialParticipantsValueFactory => 1;

    protected override ActorSystem CreateActorSystem(string name, ActorSystemSetup setup)
    {
        var system = base.CreateActorSystem(name, setup);
        _created = system;
        return system;
    }

    [MultiNodeFact]
    public async Task Restart_must_terminate_a_system_when_conductor_attachment_is_cancelled()
    {
        await Sys.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        ActorSystem? failed = null;
        try
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var exception = await Record.ExceptionAsync(() => StartNewSystemAsync(cancelled.Token));
            exception.Should().NotBeNull();
            cancelled.IsCancellationRequested.Should().BeTrue();
            // Conductor attachment uses Ask, whose cancellation is surfaced as a timeout.
            exception!.InnerException.Should().BeOfType<TimeoutException>();
            failed = _created ?? throw new InvalidOperationException("The test did not create a replacement system.");
            failed.WhenTerminated.IsCompleted.Should().BeTrue("failed attachment must settle the owned actor system before returning");
            if (failed.Settings.Setup.Get<ArteryTlsSetup>() is { HasValue: true } tls)
            {
                var certificate = tls.Value.Settings.ClientSettings.Certificate!;
                Assert.ThrowsAny<Exception>(() => certificate.GetRSAPrivateKey());
            }
        }
        finally
        {
            if (failed is not null)
                await failed.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
            // Restore a live conductor and test system so normal fixture shutdown is also exercised.
            var recovered = await StartNewSystemAsync();
            InitializeTest(recovered, recovered.Settings.Setup, null, null);
        }
        await EnterBarrierAsync("restart-recovered");
    }
}

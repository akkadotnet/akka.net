//-----------------------------------------------------------------------
// <copyright file="ArteryUnknownOriginDropWarningSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Configs;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// The unknown-origin drop warning (issue #8496) is rate limited to one warning per 10s per
    /// connection, with the drops in between folded into a suppressed count. The window is measured
    /// on an injected <see cref="ITimeProvider"/>, so this spec crosses it with
    /// <see cref="TestScheduler.Advance(TimeSpan)"/> instead of waiting 10 real seconds.
    ///
    /// <para>
    /// It lives in its own spec class because virtualizing the scheduler also freezes the timers
    /// the sibling handshake specs rely on.
    /// </para>
    /// </summary>
    public class ArteryUnknownOriginDropWarningSpec : AkkaSpec
    {
        public ArteryUnknownOriginDropWarningSpec(ITestOutputHelper output)
            : base(TestConfigs.TestSchedulerConfig, output)
        {
        }

        private static UniqueAddress NewLocal() => new(new Address("akka", "local-sys", "local-host", 2551), 111L);

        private static IInboundEnvelope OrdinaryInbound(object message, long originUid) =>
            new InboundEnvelope(message, null, "akka://remote-sys@remote-host:2552/user/recipient", originUid,
                SerializerId: 0, Manifest: "test-manifest");

        [Fact(DisplayName = "InboundHandshakeStage should rate-limit its unknown-origin drop warning on the injected clock, and report what it suppressed")]
        public async Task InboundHandshakeStage_should_rate_limit_the_unknown_origin_warning_on_virtual_time()
        {
            var scheduler = (TestScheduler)Sys.Scheduler;

            var registry = new AssociationRegistry();
            var context = new AssociationRegistryInboundContext(registry, NewLocal(), (_, _) => { });

            var materializer = ActorMaterializer.Create(Sys);

            // No explicit provider: the stage resolves the materializing system's scheduler, which
            // under TestSchedulerConfig IS the virtual clock advanced below.
            var (pub, sub) = this.SourceProbe<IInboundEnvelope>()
                .ViaMaterialized(Flow.FromGraph(new InboundHandshakeStage(context)), Keep.Left)
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);

            await sub.RequestAsync(1);

            // First drop of the window warns at once, with nothing suppressed yet.
            await EventFilter.Warning(contains: "[0] further drop(s) suppressed").ExpectOneAsync(async () =>
            {
                await pub.SendNextAsync(OrdinaryInbound("first-drop", originUid: 101L));
                await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
            });

            // Still inside the window on the virtual clock, so this one only bumps the count.
            await EventFilter.Warning(contains: "unknown origin uid").ExpectAsync(0, async () =>
            {
                await pub.SendNextAsync(OrdinaryInbound("suppressed-drop", originUid: 102L));
                await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
            });

            // Cross the 10s window without spending it: real time has barely moved, only the
            // scheduler's Now has -- and Now is what the throttle reads.
            scheduler.Advance(TimeSpan.FromSeconds(11));

            // Warns again, accounting for the one it swallowed in between.
            await EventFilter.Warning(contains: "[1] further drop(s) suppressed").ExpectOneAsync(async () =>
            {
                await pub.SendNextAsync(OrdinaryInbound("third-drop", originUid: 103L));
                await sub.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
            });
        }
    }
}

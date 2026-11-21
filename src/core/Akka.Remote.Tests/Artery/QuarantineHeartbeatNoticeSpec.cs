//-----------------------------------------------------------------------
// <copyright file="QuarantineHeartbeatNoticeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Actor;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// A quarantined peer that only sends heartbeats must still be told, now and then, that it is
    /// quarantined. Otherwise a node that missed the one-time notice never finds out. See #9010.
    /// </summary>
    public class QuarantineHeartbeatNoticeSpec
    {
        private static readonly Address Remote = new("akka", "sys", "host-b", 2552);
        private static readonly UniqueAddress Local = new(new Address("akka", "sys", "host-a", 2551), 1L);
        private const long RemoteUid = 42L;

        private static (AssociationRegistryInboundContext Context, Association Association) QuarantinedPeer()
        {
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryInboundContext(registry, Local, static (_, _) => { });
            context.CompleteHandshake(new UniqueAddress(Remote, RemoteUid));
            var association = registry.AssociationFor(Remote);
            association.Quarantine(RemoteUid).Should().BeTrue();
            return (context, association);
        }

        [Fact(DisplayName = "A heartbeat from a quarantined peer triggers one notice, then none until the interval passes")]
        public void Heartbeat_should_trigger_a_rate_limited_notice()
        {
            var now = TimeSpan.FromSeconds(100);
            var association = new Association(Remote, monotonicClock: () => now);
            var interval = TimeSpan.FromSeconds(1);

            association.TryClaimHeartbeatQuarantineNotice(interval).Should().BeTrue();
            association.TryClaimHeartbeatQuarantineNotice(interval).Should().BeFalse();

            now += TimeSpan.FromMilliseconds(999);
            association.TryClaimHeartbeatQuarantineNotice(interval).Should().BeFalse();

            now += TimeSpan.FromMilliseconds(1);
            association.TryClaimHeartbeatQuarantineNotice(interval).Should().BeTrue();
            association.TryClaimHeartbeatQuarantineNotice(interval).Should().BeFalse();
        }

        [Fact(DisplayName = "ShouldNotifyOrigin sends one notice for a burst of heartbeats, across every heartbeat type")]
        public void ShouldNotifyOrigin_should_rate_limit_heartbeats_per_association()
        {
            var (context, _) = QuarantinedPeer();

            // the limit is shared, so the first heartbeat of any type claims it for all of them
            InboundQuarantineCheckStage.ShouldNotifyOrigin(new ArteryHeartbeat(), context, RemoteUid).Should().BeTrue();
            InboundQuarantineCheckStage.ShouldNotifyOrigin(new ArteryHeartbeat(), context, RemoteUid).Should().BeFalse();
            InboundQuarantineCheckStage.ShouldNotifyOrigin(new ArteryHeartbeatRsp(), context, RemoteUid).Should().BeFalse();
            InboundQuarantineCheckStage.ShouldNotifyOrigin(RemoteWatcher.Heartbeat.Instance, context, RemoteUid).Should().BeFalse();
        }

        [Fact(DisplayName = "ShouldNotifyOrigin always sends a notice for ordinary messages, even right after a heartbeat notice")]
        public void ShouldNotifyOrigin_should_not_rate_limit_ordinary_messages()
        {
            var (context, _) = QuarantinedPeer();

            InboundQuarantineCheckStage.ShouldNotifyOrigin(new ArteryHeartbeat(), context, RemoteUid).Should().BeTrue();
            InboundQuarantineCheckStage.ShouldNotifyOrigin("hello", context, RemoteUid).Should().BeTrue();
            InboundQuarantineCheckStage.ShouldNotifyOrigin("hello", context, RemoteUid).Should().BeTrue();
        }

        [Fact(DisplayName = "ShouldNotifyOrigin never answers a quarantine notice from the peer")]
        public void ShouldNotifyOrigin_should_never_answer_a_notice()
        {
            var (context, _) = QuarantinedPeer();
            var notice = new ArteryQuarantined(new UniqueAddress(Remote, RemoteUid), Local.Uid);

            InboundQuarantineCheckStage.ShouldNotifyOrigin(notice, context, RemoteUid).Should().BeFalse();
            InboundQuarantineCheckStage.ShouldNotifyOrigin(notice, context, RemoteUid).Should().BeFalse();
        }

        [Fact(DisplayName = "ShouldNotifyOrigin sends no heartbeat notice for an unknown uid")]
        public void ShouldNotifyOrigin_should_ignore_heartbeats_from_an_unknown_uid()
        {
            var (context, _) = QuarantinedPeer();

            InboundQuarantineCheckStage.ShouldNotifyOrigin(new ArteryHeartbeat(), context, RemoteUid + 1).Should().BeFalse();
        }
    }
}

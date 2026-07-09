//-----------------------------------------------------------------------
// <copyright file="IInboundCompressionContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Actor;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The narrow transport seam the RECEIVER-side inbound compression machinery
    /// (<see cref="InboundCompressionsImpl"/> + its owning decode stage) needs from the surrounding
    /// <c>ArteryRemoting</c>: who "we" are, whether an origin is currently advertisable, how to send a
    /// table advertisement / Ack over the control stream, how to publish an observability event, and how
    /// to subscribe to inbound control messages (to receive Acks). Sized to exactly what Stage 2b-ii
    /// needs; mirrors the role Pekko's <c>InboundContext</c> plays for the Decoder's compression access.
    ///
    /// <para>
    /// <b>Threading.</b> One instance is shared across every inbound decode stage (it is a stateless
    /// bundle of delegates over the thread-safe <c>AssociationRegistry</c> / control-subscriber list /
    /// <see cref="Akka.Event.EventStream"/>). The stage calls <see cref="ResolveAdvertisableOrigin"/>,
    /// <see cref="SendControl"/> and <see cref="PublishEvent"/> only from its own interpreter thread
    /// (during observation, the advertisement timer, or an Ack async-callback); those targets are all
    /// independently thread-safe. <see cref="SubscribeControl"/>/<see cref="UnsubscribeControl"/> are the
    /// existing globally-broadcast control-subscriber hooks -- see <see cref="IControlMessageSubscriber"/>.
    /// </para>
    /// </summary>
    internal interface IInboundCompressionContext
    {
        /// <summary>This system's own unique address -- the <c>From</c> stamped into every advertisement it sends.</summary>
        UniqueAddress LocalAddress { get; }

        /// <summary>
        /// The remote address to advertise a compression table to for <paramref name="originUid"/>, or
        /// <see langword="null"/> when the origin is NOT currently advertisable -- either no association
        /// has completed its handshake yet, or the origin is quarantined. Mirrors Pekko's
        /// "advertise only when <c>association(originUid)</c> resolves and is not quarantined" gate; a
        /// <see langword="null"/> result tells the caller to drop that origin's compression state
        /// (Pekko's <c>close(originUid)</c>).
        /// </summary>
        Address? ResolveAdvertisableOrigin(long originUid);

        /// <summary>Sends <paramref name="message"/> (a compression advertisement or Ack) to <paramref name="to"/> over the outbound CONTROL stream.</summary>
        void SendControl(Address to, object message);

        /// <summary>Publishes <paramref name="evt"/> (an <see cref="ArteryInboundCompressionEvent"/>) to the system <see cref="Akka.Event.EventStream"/> for test/ops observability.</summary>
        void PublishEvent(object evt);

        /// <summary>Registers <paramref name="subscriber"/> for inbound control messages (so a decode stage can receive compression Acks). See <see cref="IControlMessageSubscriber"/>.</summary>
        void SubscribeControl(IControlMessageSubscriber subscriber);

        /// <summary>Reverses <see cref="SubscribeControl"/>.</summary>
        void UnsubscribeControl(IControlMessageSubscriber subscriber);
    }

    /// <summary>
    /// INTERNAL API. Delegate-backed <see cref="IInboundCompressionContext"/> built once by
    /// <c>ArteryRemoting</c> and shared across all inbound decode stages -- the same delegate-bundle
    /// pattern as <see cref="AssociationRegistryInboundContext"/> and
    /// <see cref="AssociationRegistryOutboundContext"/>.
    /// </summary>
    internal sealed class DelegateInboundCompressionContext : IInboundCompressionContext
    {
        private readonly Func<long, Address?> _resolveAdvertisableOrigin;
        private readonly Action<Address, object> _sendControl;
        private readonly Action<object> _publishEvent;
        private readonly Action<IControlMessageSubscriber> _subscribeControl;
        private readonly Action<IControlMessageSubscriber> _unsubscribeControl;

        public DelegateInboundCompressionContext(
            UniqueAddress localAddress,
            Func<long, Address?> resolveAdvertisableOrigin,
            Action<Address, object> sendControl,
            Action<object> publishEvent,
            Action<IControlMessageSubscriber> subscribeControl,
            Action<IControlMessageSubscriber> unsubscribeControl)
        {
            LocalAddress = localAddress;
            _resolveAdvertisableOrigin = resolveAdvertisableOrigin;
            _sendControl = sendControl;
            _publishEvent = publishEvent;
            _subscribeControl = subscribeControl;
            _unsubscribeControl = unsubscribeControl;
        }

        /// <inheritdoc/>
        public UniqueAddress LocalAddress { get; }

        /// <inheritdoc/>
        public Address? ResolveAdvertisableOrigin(long originUid) => _resolveAdvertisableOrigin(originUid);

        /// <inheritdoc/>
        public void SendControl(Address to, object message) => _sendControl(to, message);

        /// <inheritdoc/>
        public void PublishEvent(object evt) => _publishEvent(evt);

        /// <inheritdoc/>
        public void SubscribeControl(IControlMessageSubscriber subscriber) => _subscribeControl(subscriber);

        /// <inheritdoc/>
        public void UnsubscribeControl(IControlMessageSubscriber subscriber) => _unsubscribeControl(subscriber);
    }
}

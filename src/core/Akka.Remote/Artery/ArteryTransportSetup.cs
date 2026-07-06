//-----------------------------------------------------------------------
// <copyright file="ArteryTransportSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using Akka.Actor.Setup;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Programmatic (<see cref="Akka.Actor.Setup.Setup"/>-based) configuration for
    /// <see cref="ArteryRemoting"/>. Replaces the mutable static test hook that used to live on
    /// <c>ArteryRemoting</c> (<c>EncodePoolOverrideForTests</c>) with the idiomatic
    /// <see cref="ActorSystemSetup"/> mechanism every other programmatically-configurable Akka.NET
    /// subsystem uses (see e.g. <c>SerializationSetup</c>).
    ///
    /// <para>
    /// <b>Read once at <see cref="ArteryRemoting.Start"/>.</b> <c>ArteryRemoting</c> looks this up
    /// via <c>System.Settings.Setup.Get&lt;ArteryTransportSetup&gt;()</c> and hands
    /// <see cref="EncodeBufferPool"/> to EVERY <see cref="ArteryEncodeStage"/> it subsequently
    /// materializes -- both the ordinary AND the control outbound stream (see
    /// <c>ArteryRemoting.MaterializeOutboundStream</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Test usage.</b> A poison-pool test constructs its <see cref="Akka.Actor.ActorSystem"/>
    /// with <c>ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), new
    /// ArteryTransportSetup(poisonPool))</c> (or <c>BootstrapSetup...WithConfig(config).And(new
    /// ArteryTransportSetup(poisonPool))</c>) instead of mutating a shared static field -- so
    /// concurrently-running tests can each use their OWN pool override without racing each other.
    /// </para>
    ///
    /// <para>
    /// <b>Production seam.</b> Beyond testing, this is also where a future dedicated/POH
    /// (pinned-object-heap) buffer pool would be wired in, once measurement justifies one over
    /// <see cref="ArrayPool{T}.Shared"/> (design.md Decision 9's ".NET primitive mapping" row for
    /// <c>EnvelopeBufferPool</c>).
    /// </para>
    /// </summary>
    internal sealed class ArteryTransportSetup : Setup
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryTransportSetup"/> class.
        /// </summary>
        /// <param name="encodeBufferPool">
        /// The <see cref="ArrayPool{T}"/> every materialized outbound stream's
        /// <see cref="ArteryEncodeStage"/> rents its encode buffers from (and returns them to).
        /// <see langword="null"/> (the default -- and the value used when no
        /// <see cref="ArteryTransportSetup"/> is present at all) means <see cref="ArrayPool{T}.Shared"/>.
        /// </param>
        /// <param name="dropOutboundControlMessage">
        /// Fault-injection test hook (design.md gate G3's G3 correctness suite -- "DeathWatch
        /// end-to-end under loss": induced ack loss, without which deterministically dropping just
        /// the peer's <see cref="Ack"/>/<see cref="Nack"/> replies -- while leaving handshake/heartbeat/
        /// <see cref="SystemMessageEnvelope"/> traffic alone -- would require reaching into the TCP
        /// socket layer). When non-null, <see cref="ArteryRemoting.EnqueueControl"/> calls it for
        /// every outbound CONTROL message before enqueueing; a <see langword="true"/> result silently
        /// drops that one message (never enqueued -- simulating loss on the wire) instead of sending
        /// it. <see langword="null"/> (the default, and the only production value) disables this
        /// entirely -- every control message is enqueued normally.
        /// </param>
        public ArteryTransportSetup(ArrayPool<byte>? encodeBufferPool = null, Func<object, bool>? dropOutboundControlMessage = null)
        {
            EncodeBufferPool = encodeBufferPool;
            DropOutboundControlMessage = dropOutboundControlMessage;
        }

        /// <summary>
        /// The <see cref="ArrayPool{T}"/> every materialized outbound stream's
        /// <see cref="ArteryEncodeStage"/> rents its encode buffers from. <see langword="null"/>
        /// means <see cref="ArrayPool{T}.Shared"/>.
        /// </summary>
        public ArrayPool<byte>? EncodeBufferPool { get; }

        /// <summary>
        /// Fault-injection test hook: when it returns <see langword="true"/> for a given outbound
        /// CONTROL message, that message is silently dropped (never enqueued) instead of being sent.
        /// <see langword="null"/> (the default) disables this -- see the constructor parameter docs.
        /// </summary>
        public Func<object, bool>? DropOutboundControlMessage { get; }
    }
}

//-----------------------------------------------------------------------
// <copyright file="RemoteTempActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Util;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The sender of an Ask request received as a numeric record: a temporary ref owned by the
    /// peer system <see cref="OwnerUid"/> (the frame's origin), identified by <see cref="TempId"/>. It
    /// holds the <see cref="Association"/> the request arrived on, so <c>Tell</c> can address the reply
    /// numerically without formatting or parsing any path.
    ///
    /// <para>
    /// To everything else it is an ordinary <see cref="RemoteActorRef"/>: <see cref="Path"/> is
    /// materialised on first use (<c>remoteAddress / "temp" / Base64Encode(tempId)</c>, exactly the path the
    /// literal form parses to) and cached; <see cref="object.Equals(object)"/> and
    /// <see cref="object.GetHashCode"/> are the base class's path-based ones, so this ref equals, and hashes
    /// like, a <see cref="RemoteActorRef"/> resolved from the literal path. Watch, serialization inside a
    /// payload, <c>ToString</c> and <c>ActorSelection</c> all go through <see cref="Path"/>.
    /// </para>
    /// </summary>
    internal sealed class RemoteTempActorRef : RemoteActorRef
    {
        private readonly ArteryRemoting _artery;
        private readonly Address _remoteAddress;
        private ActorPath? _path;

        public RemoteTempActorRef(
            ArteryRemoting artery,
            Address localAddressToUse,
            Association association,
            Address remoteAddress,
            long ownerUid,
            long tempId)
            : base(artery, localAddressToUse)
        {
            _artery = artery;
            Association = association;
            _remoteAddress = remoteAddress;
            OwnerUid = ownerUid;
            TempId = tempId;
        }

        /// <summary>The association the Ask request arrived on, used to route the reply.</summary>
        public Association Association { get; }

        /// <summary>The uid of the system that owns the temp ref (the request's origin).</summary>
        public long OwnerUid { get; }

        /// <summary>The counter value naming the temp ref below the owner's <c>/temp</c>.</summary>
        public long TempId { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// Materialised lazily, once: <see cref="ActorPath"/> is immutable and the path is a pure function of
        /// immutable state, so racing first calls may each build one, but the first published wins and every
        /// caller sees that instance.
        /// </remarks>
        public override ActorPath Path => Volatile.Read(ref _path) ?? CreatePath();

        private ActorPath CreatePath()
        {
            var path = new RootActorPath(_remoteAddress) / "temp" / TempId.Base64Encode();
            return Interlocked.CompareExchange(ref _path, path, null) ?? path;
        }

        /// <inheritdoc/>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if (message == null) throw new InvalidMessageException("Message is null.");
            try
            {
                _artery.SendToTempRef(message, sender, this);
            }
            catch (Exception ex)
            {
                _artery.System.EventStream.Publish(
                    new Error(ex, Path.ToString(), GetType(), "swallowing exception during message send"));
            }
        }
    }
}

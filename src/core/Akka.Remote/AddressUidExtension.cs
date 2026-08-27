//-----------------------------------------------------------------------
// <copyright file="AddressUidExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using Akka.Actor;

namespace Akka.Remote
{
    /// <summary>
    /// <see cref="IExtension"/> provider for <see cref="AddressUid"/>
    /// </summary>
    public class AddressUidExtension : ExtensionIdProvider<AddressUid>
    {
        /// <summary>
        /// Creates the <see cref="AddressUid"/> extension for <paramref name="system"/>, honoring the
        /// <c>akka.remote.use-64bit-system-uids</c> config switch.
        /// </summary>
        /// <param name="system">The actor system that owns this extension.</param>
        /// <returns>A new <see cref="AddressUid"/> instance.</returns>
        public override AddressUid CreateExtension(ExtendedActorSystem system)
        {
            var use64BitUid = system.Settings.Config.GetBoolean("akka.remote.use-64bit-system-uids", false);
            return new AddressUid(use64BitUid);
        }

        #region Static methods

        /// <summary>
        /// Returns the unique identifier for this incarnation of <paramref name="system"/>.
        /// </summary>
        /// <param name="system">The actor system whose UID is being retrieved.</param>
        /// <returns>The address/system UID for this actor system incarnation.</returns>
        public static long Uid(ActorSystem system)
        {
            return system.WithExtension<AddressUid, AddressUidExtension>().Uid;
        }

        #endregion
    }

    /// <summary>
    /// Extension that holds a UID that is assigned as a random <see cref="long"/>.
    ///
    /// The UID is intended to be used together with an <see cref="Address"/> to be
    /// able to distinguish restarted actor system using the same host and port.
    /// </summary>
    public class AddressUid : IExtension
    {
        /// <summary>
        /// Creates a new <see cref="AddressUid"/> using the legacy int-range (rolling-upgrade safe) generation.
        /// </summary>
        public AddressUid() : this(false)
        {
        }

        /// <summary>
        /// Creates a new <see cref="AddressUid"/>, optionally generating a full-range 64-bit UID.
        /// </summary>
        /// <param name="use64BitUid">
        /// When <c>true</c>, generates a uniformly random nonzero <see cref="long"/> across the full 64-bit range
        /// (negative values allowed). When <c>false</c> (default), generates a value in <c>[0, int.MaxValue]</c>
        /// for rolling-upgrade compatibility with pre-v1.6 nodes.
        /// </param>
        internal AddressUid(bool use64BitUid)
        {
            Uid = GenerateUid(use64BitUid);
        }

        /// <summary>
        /// The random unique identifier for this incarnation of the ActorSystem.
        /// </summary>
        public readonly long Uid;

        /// <summary>
        /// Generates a uniformly random nonzero UID -- full 64-bit range when
        /// <paramref name="use64Bit"/> is <c>true</c>, else <c>[1, int.MaxValue]</c> (the legacy
        /// rolling-upgrade-safe int range). Zero is reserved as a sentinel value and is never
        /// returned.
        ///
        /// <para>
        /// Deliberately uses a cryptographic <see cref="RandomNumberGenerator"/> rather than
        /// <c>Akka.Util.ThreadLocalRandom</c>: the latter is seeded from
        /// <see cref="Environment.TickCount"/>, so MULTIPLE PROCESSES started within the same
        /// millisecond tick (e.g. the multi-node test runner spawning every node of a spec at
        /// once) draw identical seeds and produce IDENTICAL system UIDs. The system UID is this
        /// incarnation's remoting identity -- Artery keys handshakes, quarantines, and its
        /// association registry's reverse index by it -- so cross-process collisions silently
        /// corrupt uid-keyed behavior (e.g. traffic from one peer attributed to another).
        /// Cost is irrelevant here: one draw per ActorSystem. netstandard2.0-safe.
        /// </para>
        /// </summary>
        private static long GenerateUid(bool use64Bit)
        {
            using var rng = RandomNumberGenerator.Create();
            var buf = new byte[8];
            long candidate;
            do
            {
                rng.GetBytes(buf);
                candidate = use64Bit
                    ? BitConverter.ToInt64(buf, 0)
                    : BitConverter.ToInt32(buf, 0) & int.MaxValue;
            } while (candidate == 0);

            return candidate;
        }
    }
}


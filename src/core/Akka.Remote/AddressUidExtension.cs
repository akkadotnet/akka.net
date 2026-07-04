//-----------------------------------------------------------------------
// <copyright file="AddressUidExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util;

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
            Uid = use64BitUid ? Generate64BitUid() : ThreadLocalRandom.Current.Next();
        }

        /// <summary>
        /// The random unique identifier for this incarnation of the ActorSystem.
        /// </summary>
        public readonly long Uid;

        /// <summary>
        /// Generates a uniformly random nonzero <see cref="long"/> across the full 64-bit range.
        /// Zero is reserved as a sentinel value and is never returned. netstandard2.0-safe
        /// (does not rely on <c>Random.NextInt64()</c>, which is only available on net6.0+).
        /// </summary>
        private static long Generate64BitUid()
        {
            var buf = new byte[8];
            long candidate;
            do
            {
                ThreadLocalRandom.Current.NextBytes(buf);
                candidate = BitConverter.ToInt64(buf, 0);
            } while (candidate == 0);

            return candidate;
        }
    }
}


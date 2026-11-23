//-----------------------------------------------------------------------
// <copyright file="ArteryLaneKey.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The one function that maps a recipient to a lane (outbound: which lane queue; inbound: which lane
    /// consumer). The key is a 64-bit id -- an actor's UID, or for a numeric temporary ref its
    /// counter value with <see cref="ArteryNumericRef.TemporaryNamespaceBit"/> set.
    ///
    /// <para>
    /// The key is mixed before it is reduced: ids are handed out sequentially, so a plain modulus aliases
    /// whenever the creation pattern has a period that divides the lane count (e.g. an entity that creates
    /// three children makes every fourth id an entity, and with four lanes every entity would share one).
    /// </para>
    /// </summary>
    internal static class ArteryLaneKey
    {
        // 2^64 / golden ratio, odd: a Fibonacci-hashing multiplier.
        private const ulong Multiplier = 0x9E37_79B9_7F4A_7C15UL;

        /// <summary>The lane key of a numeric ref: its id, namespace bit included.</summary>
        public static long For(in ArteryNumericRef value) => value.Id;

        /// <summary>The lane key of the temporary ref whose name encodes <paramref name="counter"/>.</summary>
        public static long ForTemporary(long counter) => counter | ArteryNumericRef.TemporaryNamespaceBit;

        /// <summary>Maps <paramref name="key"/> to a lane in <c>[0, lanes)</c>: multiply by an odd constant, keep the high 32 bits, scale to the lane count.</summary>
        public static int Select(long key, int lanes)
        {
            if (lanes <= 1)
                return 0;

            var mixed = unchecked((ulong)key * Multiplier) >> 32;
            return (int)((mixed * (ulong)lanes) >> 32);
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="DDataGenerators.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Cluster;
using FsCheck;
using FsCheck.Fluent;

namespace Akka.DistributedData.Tests
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// FsCheck data generators for Akka.DistributedData CRDT types.
    /// Designed for reuse across fuzz tests of <see cref="ORSet{T}"/>,
    /// <see cref="ORDictionary{TKey,TValue}"/>, and their derivatives.
    /// </summary>
    [InternalApi]
    public static class DDataGenerators
    {
        /// <summary>
        /// Small fixed pool of <see cref="UniqueAddress"/>es so that fuzzed
        /// operations have a non-trivial probability of colliding on the same
        /// writer identity — the production case for a single
        /// cluster-singleton writer.
        /// </summary>
        public static Arbitrary<UniqueAddress> UniqueAddressGenerator()
        {
            var addresses = Enumerable.Range(1, 4)
                .Select(i => new UniqueAddress(
                    new Address("akka.tcp", "system", "host", 2550 + i),
                    i))
                .ToArray();

            return Arb.From(Gen.Elements(addresses));
        }
    }
}

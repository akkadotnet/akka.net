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
    /// Designed for fuzzing merge invariants of <see cref="ORSet{T}"/>,
    /// <see cref="ORDictionary{TKey,TValue}"/>, and their derivatives.
    /// </summary>
    [InternalApi]
    public static class DDataGenerators
    {
        /// <summary>
        /// Small fixed pool of <see cref="UniqueAddress"/>es so that fuzzed
        /// operations have a non-trivial probability of colliding on the same
        /// writer identity — which is the production case for a single
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

        /// <summary>
        /// A single writer operation: <c>SetItem(key)</c> against an
        /// <see cref="ORDictionary{TKey,TValue}"/> with int keys in a small
        /// range so that updates frequently land on the same key
        /// (modelling the customer's "update existing entries every second"
        /// pattern).
        /// </summary>
        public static Arbitrary<WriterSetItem> WriterSetItemGenerator()
        {
            var gen = Gen.Choose(0, 9)
                .Select(key => new WriterSetItem(key));
            return Arb.From(gen);
        }

        /// <summary>
        /// A sequence of writer operations (5..50 operations long). The
        /// resulting list, when applied in order, exercises both new-key adds
        /// and repeated updates of existing keys. We sample the length via
        /// <see cref="Gen.Choose(int,int)"/> rather than filtering with
        /// <see cref="Gen.Where{T}"/> so FsCheck doesn't waste iterations
        /// rejecting too-small or too-large arrays.
        /// </summary>
        public static Arbitrary<WriterSetItem[]> WriterSetItemSequenceGenerator()
        {
            var gen = Gen.Choose(5, 50)
                .SelectMany(len => WriterSetItemGenerator().Generator.ArrayOf(len));
            return Arb.From(gen);
        }
    }

    /// <summary>
    /// Generated value representing a single writer <c>SetItem</c> operation.
    /// </summary>
    public sealed record WriterSetItem(int Key);
}

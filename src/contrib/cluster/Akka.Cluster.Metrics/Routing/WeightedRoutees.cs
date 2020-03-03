//-----------------------------------------------------------------------
// <copyright file="WeightedRoutees.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

// //-----------------------------------------------------------------------
// // <copyright file="WeightedRoutees.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Annotations;
using Akka.Pattern;
using Akka.Routing;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Pick routee based on its weight. Higher weight, higher probability.
    /// </summary>
    [InternalApi]
    public class WeightedRoutees
    {
        private readonly ImmutableArray<Routee> _routees;
        private readonly Actor.Address _selfAddress;
        private readonly IImmutableDictionary<Actor.Address, int> _weights;
        private readonly int[] _buckets;

        public WeightedRoutees(ImmutableArray<Routee> routees, Actor.Address selfAddress, IImmutableDictionary<Actor.Address, int> weights)
        {
            _routees = routees;
            _selfAddress = selfAddress;
            _weights = weights;

            // fill an array of same size as the refs with accumulated weights,
            // binarySearch is used to pick the right bucket from a requested value
            // from 1 to the total sum of the used weights.
            _buckets = InitBuckets();
        }

        public bool IsEmpty => _buckets.Length == 0 || _buckets.Last() == 0;

        public int Total
        {
            get
            {
                if (IsEmpty)
                    throw new IllegalStateException("WeightedRoutees must not be used when empty");

                return _buckets.Last();
            }
        }

        /// <summary>
        /// Pick the routee matching a value, from 1 to total.
        /// </summary>
        public Routee this[int value]
        {
            get
            {
                if (value < 1 || value > Total)
                    throw new ArgumentException(nameof(value), $"value must be between [1 - {Total}]");

                return _routees[GetIndex(Array.BinarySearch(_buckets, value))];
            }
        }

        /// <summary>
        /// Converts the result of Arrays.IndexOf into a index in the buckets array
        /// </summary>
        private int GetIndex(int i)
        {
            if (i >= 0) return i;

            var j = Math.Abs(i + 1);
            if (j >= _buckets.Length)
                throw new IndexOutOfRangeException($"Requested index [{i}] is > max index [{_buckets.Length - 1}]");

            return j;
        }

        private int[] InitBuckets()
        {
            Actor.Address FullAddress(Routee routee)
            {
                Actor.Address a;
                switch (routee)
                {
                    case ActorRefRoutee @ref:
                        a = @ref.Actor.Path.Address;
                        break;
                    case ActorSelectionRoutee selection:
                        a = selection.Selection.Anchor.Path.Address;
                        break;
                    default:
                        throw new ArgumentException(nameof(routee), $"Unexpected routee type: {routee.GetType()}");
                }

                if (a.Host == null && a.Port == null)
                    return _selfAddress;

                return a;
            }

            var buckets = new int[_routees.Length];
            var meanWeight = _weights.Count == 0 ? 1 : _weights.Values.Sum() / _weights.Count;
            var i = 0;
            var sum = 0;
            foreach (var routee in _routees)
            {
                sum += _weights.GetValueOrDefault(FullAddress(routee), meanWeight);
                buckets[i] = sum;
                i += 1;
            }

            return buckets;
        }
    }
}

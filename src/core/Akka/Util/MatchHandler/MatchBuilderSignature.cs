//-----------------------------------------------------------------------
// <copyright file="MatchBuilderSignature.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// This class contains the handled <see cref="Type">Types</see> and <see cref="HandlerKind">HandlerKinds</see> 
    /// that has been added to a <see cref="MatchBuilder"/>.
    /// Two signatures are equal if they contain the same <see cref="Type">Types</see> and <see cref="HandlerKind">HandlerKinds</see>
    /// in the same order.
    /// </summary>
    internal class MatchBuilderSignature : IEquatable<MatchBuilderSignature>
    {
        private readonly IReadOnlyList<object> _list;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="signature">TBD</param>
        public MatchBuilderSignature(IReadOnlyList<object> signature)
        {
            _list = signature;
        }

        /// <inheritdoc/>
        public bool Equals(MatchBuilderSignature other)
        {
            if(ReferenceEquals(null, other)) return false;
            if(ReferenceEquals(this, other)) return true;
            return ListsEqual(_list, other._list);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if(ReferenceEquals(null, obj)) return false;
            if(ReferenceEquals(this, obj)) return true;
            if(obj.GetType() != this.GetType()) return false;
            return ListsEqual(_list, ((MatchBuilderSignature)obj)._list);
        }

        // Two signatures are equal if they contain the same <see cref="Type">Types</see> and <see cref="HandlerKind">HandlerKinds</see>
        // in the same order.
        private bool ListsEqual(IReadOnlyList<object> x, IReadOnlyList<object> y)
        {
            if(x == null) return y == null || y.Count == 0;
            var xCount = x.Count;
            if(y == null) return xCount == 0;
            if(xCount != y.Count) return false;
            for(var i = 0; i < xCount; i++)
            {
                if(!Equals(x[i], y[i])) return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if(_list == null) return 0;
            var count = _list.Count;
            if(count == 0) return 0;
            var hashCode = _list[0].GetHashCode();
            for(var i = 1; i < count; i++)
            {
                hashCode = (hashCode * 397) ^ _list[i].GetHashCode();
            }
            return hashCode;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var types = _list.Select(o => (o as Type)?.Name ?? o);
            return $"[{string.Join(", ", types)}]";
        }
    }
}


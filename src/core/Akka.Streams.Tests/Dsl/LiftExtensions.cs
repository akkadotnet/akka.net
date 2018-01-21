//-----------------------------------------------------------------------
// <copyright file="LiftExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;

namespace Akka.Streams.Tests.Dsl
{
    internal static class LiftExtensions
    {
        public static Source<Source<int, NotUsed>, TMat> Lift<TMat>(this SubFlow<int, TMat, IRunnableGraph<TMat>> source)
        {
            return
                (Source<Source<int, NotUsed>, TMat>)
                    source.PrefixAndTail(0).Select(x => x.tail).ConcatSubstream();
        }

        public static Source<(int, Source<int, NotUsed>), TMat> Lift<TMat>(this SubFlow<int, TMat, IRunnableGraph<TMat>> source, Func<int, int> key)
        {
            return
                (Source<(int, Source<int, NotUsed>), TMat>)
                source.PrefixAndTail(1).Select(p => (key(p.prefix.First()), Source.Single(p.prefix.First()).Concat(p.tail))).ConcatSubstream();
        }
    }
}
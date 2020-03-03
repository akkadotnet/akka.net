//-----------------------------------------------------------------------
// <copyright file="LiftExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
                    source.PrefixAndTail(0).Select(x => x.Item2).ConcatSubstream();
        }

        public static Source<(int, Source<int, NotUsed>), TMat> Lift<TMat>(this SubFlow<int, TMat, IRunnableGraph<TMat>> source, Func<int, int> key)
        {
            return
                (Source<(int, Source<int, NotUsed>), TMat>)
                source.PrefixAndTail(1).Select(p => (key(p.Item1.First()), Source.Single(p.Item1.First()).Concat(p.Item2))).ConcatSubstream();
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="LiftExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;

namespace Akka.Streams.Tests.Dsl
{
    internal static class LiftExtensions
    {
        public static Source<Source<int, NotUsed>, TMat> Lift<TMat>(this SubFlow<int, TMat, IRunnableGraph<TMat>> source)
        {
            return
                (Source<Source<int, NotUsed>, TMat>)
                    ((SubFlow<Source<int, NotUsed>, TMat, IRunnableGraph<TMat>>) source.PrefixAndTail(0).Select(x => x.Item2))
                        .ConcatSubstream();
        }

        public static Source<Tuple<int, Source<int, NotUsed>>, TMat> Lift<TMat>(this SubFlow<int, TMat, IRunnableGraph<TMat>> source, Func<int, int> key)
        {
            return
                (Source<Tuple<int, Source<int, NotUsed>>, TMat>)
                ((SubFlow<Tuple<int, Source<int, NotUsed>>, TMat, IRunnableGraph<TMat>>)source.PrefixAndTail(1).Select(p => Tuple.Create(key(p.Item1.First()), Source.Single(p.Item1.First()).Concat(p.Item2)))).ConcatSubstream();
        }
    }
}
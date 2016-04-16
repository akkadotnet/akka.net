using System;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;

namespace Akka.Streams.Tests.Dsl
{
    internal static class LiftExtensions
    {
        public static Source<Source<int, Unit>, TMat> Lift<TMat>(this SubFlow<int, TMat, IRunnableGraph<TMat>> source)
        {
            return
                (Source<Source<int, Unit>, TMat>)
                    ((SubFlow<Source<int, Unit>, TMat, IRunnableGraph<TMat>>) source.PrefixAndTail(0).Map(x => x.Item2))
                        .ConcatSubstream();
        }

        public static Source<Tuple<int, Source<int, Unit>>, TMat> Lift<TMat>(this SubFlow<int, TMat, IRunnableGraph<TMat>> source, Func<int, int> key)
        {
            return
                (Source<Tuple<int, Source<int, Unit>>, TMat>)
                ((SubFlow<Tuple<int, Source<int, Unit>>, TMat, IRunnableGraph<TMat>>)source.PrefixAndTail(1).Map(p => Tuple.Create(key(p.Item1.First()), Source.Single(p.Item1.First()).Concat(p.Item2)))).ConcatSubstream();
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation;

namespace Akka.Streams.Tests.Dsl
{
    internal static class LiftExtensions
    {
        public static SubFlowImpl<int, Source<int, Unit>, TMat> Lift<TMat>(this SubFlow<int, TMat> source)
        {
            return
                (SubFlowImpl<int, Source<int, Unit>, TMat>)
                    ((SubFlow<Source<int, Unit>, TMat>)source.PrefixAndTail(0).Map(x => x.Item2))
                        .ConcatSubstream();
        }

        public static SubFlowImpl<int, KeyValuePair<KeyValuePair<int, Source<int, TMat>>, Source<KeyValuePair<int, Source<int, TMat>>, TMat>>, TMat> Lift<TMat>(this SubFlow<KeyValuePair<int, Source<int, TMat>>, TMat> source, Func<int, int> key)
        {
            return
                (SubFlowImpl<int, KeyValuePair<KeyValuePair<int, Source<int, TMat>>, Source<KeyValuePair<int, Source<int, TMat>>, TMat>>, TMat>)
                    ((SubFlow<KeyValuePair<KeyValuePair<int, Source<int, TMat>>, Source<KeyValuePair<int, Source<int, TMat>>, TMat>>, TMat>)source.PrefixAndTail(1).Map(x =>
                    {
                        var s = Source.Combine(Source.Single(x.Item1.First()), x.Item2, i => new Merge<KeyValuePair<int, Source<int, TMat>>>(i)).MapMaterializedValue(_=>default(TMat));
                        return new KeyValuePair<KeyValuePair<int, Source<int, TMat>>, Source<KeyValuePair<int, Source<int, TMat>>, TMat>>(x.Item1.First(), s);
                    })).ConcatSubstream();
        }
    }
}
//-----------------------------------------------------------------------
// <copyright file="FlowWithContextOperations.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Implementation.Fusing;

namespace Akka.Streams.Dsl
{
    public static class FlowWithContextOperations
    {
        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Select{TIn,T,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TIn, TCtx, TOut2, TCtx, TMat> Select<TIn, TCtx, TOut, TOut2, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, Func<TOut, TOut2> fn)
        {
            var stage = new Select<(TOut, TCtx), (TOut2, TCtx)>(x => (fn(x.Item1), x.Item2));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.SelectAsync{TIn,T,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TIn, TCtx, TOut2, TCtx, TMat> SelectAsync<TIn, TCtx, TOut, TOut2, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, int parallelism, Func<TOut, Task<TOut2>> fn)
        {
            var stage = new SelectAsync<(TOut, TCtx), (TOut2, TCtx)>(parallelism,
                async x => (await fn(x.Item1), x.Item2));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Collect{TIn,T,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TIn, TCtx, TOut2, TCtx, TMat> Collect<TIn, TCtx, TOut, TOut2, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, Func<TOut, TOut2> fn) where TOut2 : class
        {
            var stage = new Collect<(TOut, TCtx), (TOut2, TCtx)>(func: x =>
            {
                var result = fn(x.Item1);
                return ReferenceEquals(result, null) ? default((TOut2, TCtx)) : (result, x.Item2);
            });
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Where{TIn,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> Where<TIn, TCtx, TOut, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, Func<TOut, bool> predicate)
        {
            var stage = new Where<(TOut, TCtx)>(x => predicate(x.Item1));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Grouped{TIn,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> WhereNot<TIn, TCtx, TOut, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, Func<TOut, bool> predicate)
        {
            var stage = new Where<(TOut, TCtx)>(x => !predicate(x.Item1));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Grouped{TIn,TOut,TMat}"/>
        /// Each output group will be associated with a `Seq` of corresponding context elements.
        /// </summary>
        public static FlowWithContext<TIn, TCtx, IReadOnlyList<TOut>, IReadOnlyList<TCtx>, TMat> Grouped<TIn, TCtx, TOut, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, int n)
        {
            var stage = new Grouped<(TOut, TCtx)>(n);
            return flow.Via(Flow.FromGraph(stage).Select(itemsWithContexts =>
            {
                var items = new List<TOut>(n);
                var ctxs = new List<TCtx>(n);

                foreach (var tuple in itemsWithContexts)
                {
                    items.Add(tuple.Item1);
                    ctxs.Add(tuple.Item2);
                }

                return ((IReadOnlyList<TOut>)items, (IReadOnlyList<TCtx>)ctxs);
            }));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Sliding{TIn,TOut,TMat}"/>
        /// Each output group will be associated with a `Seq` of corresponding context elements.
        /// </summary>
        public static FlowWithContext<TIn, TCtx, IReadOnlyList<TOut>, IReadOnlyList<TCtx>, TMat> Sliding<TIn, TCtx, TOut, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, int n, int step = 1)
        {
            var stage = new Sliding<(TOut, TCtx)>(n, step);
            return flow.Via(Flow.FromGraph(stage).Select(itemsWithContexts =>
            {
                var items = new List<TOut>(n);
                var ctxs = new List<TCtx>(n);

                foreach (var tuple in itemsWithContexts)
                {
                    items.Add(tuple.Item1);
                    ctxs.Add(tuple.Item2);
                }

                return ((IReadOnlyList<TOut>)items, (IReadOnlyList<TCtx>)ctxs);
            }));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.SelectMany{TIn,T,TOut,TMat}"/>.
        /// The context of the input element will be associated with each of the output elements calculated from
        /// this input element.
        /// </summary>
        public static FlowWithContext<TIn, TCtx, TOut2, TCtx, TMat> SelectConcat<TIn, TCtx, TOut, TOut2, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, Func<TOut, IEnumerable<TOut2>> fn) =>
            StatefulSelectConcat(flow, () => fn);

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.StatefulSelectMany{TIn,T,TOut,TMat}"/>.
        /// </summary>
        public static FlowWithContext<TIn, TCtx, TOut2, TCtx, TMat> StatefulSelectConcat<TIn, TCtx, TOut, TOut2, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, Func<Func<TOut, IEnumerable<TOut2>>> fn)
        {
            var stage = new StatefulSelectMany<(TOut, TCtx), (TOut2, TCtx)>(() =>
            {
                var fun = fn();
                return itemWithContext =>
                {
                    var items = fun(itemWithContext.Item1);
                    return items.Select(i => (i, itemWithContext.Item2));
                };
            });
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Apply the given function to each context element (leaving the data elements unchanged).
        /// </summary>
        public static FlowWithContext<TIn, TCtx, TOut, TCtx2, TMat> SelectContext<TIn, TCtx, TOut, TCtx2, TMat>(
            this FlowWithContext<TIn, TCtx, TOut, TCtx, TMat> flow, Func<TCtx, TCtx2> mapContext)
        {
            var stage = new Select<(TOut, TCtx), (TOut, TCtx2)>(x =>
                (x.Item1, mapContext(x.Item2)));
            return flow.Via(Flow.FromGraph(stage));
        }
    }

    public static class SourceWithContextOperations
    {
        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Select{TOut,T,TMat}"/>
        /// </summary>
        public static SourceWithContext<TOut2, TCtx, TMat> Select<TOut, TCtx, TOut2, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, Func<TOut, TOut2> fn)
        {
            var stage = new Select<(TOut, TCtx), (TOut2, TCtx)>(x => (fn(x.Item1), x.Item2));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.SelectAsync{TOut,T,TMat}"/>
        /// </summary>
        public static SourceWithContext<TOut2, TCtx, TMat> SelectAsync<TOut, TCtx, TOut2, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, int parallelism, Func<TOut, Task<TOut2>> fn)
        {
            var stage = new SelectAsync<(TOut, TCtx), (TOut2, TCtx)>(parallelism,
                async x => (await fn(x.Item1), x.Item2));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Collect{T,TOut,TMat}"/>
        /// </summary>
        public static SourceWithContext<TOut2, TCtx, TMat> Collect<TOut, TCtx, TOut2, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, Func<TOut, TOut2> fn) where TOut2 : class
        {
            var stage = new Collect<(TOut, TCtx), (TOut2, TCtx)>(func: x =>
            {
                var result = fn(x.Item1);
                return ReferenceEquals(result, null) ? default((TOut2, TCtx)) : (result, x.Item2);
            });
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Where{TOut,TMat}"/>
        /// </summary>
        public static SourceWithContext<TOut, TCtx, TMat> Where<TOut, TCtx, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, Func<TOut, bool> predicate)
        {
            var stage = new Where<(TOut, TCtx)>(x => predicate(x.Item1));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Grouped{TOut,TMat}"/>
        /// </summary>
        public static SourceWithContext<TOut, TCtx, TMat> WhereNot<TOut, TCtx, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, Func<TOut, bool> predicate)
        {
            var stage = new Where<(TOut, TCtx)>(x => !predicate(x.Item1));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Grouped{TOut,TMat}"/>
        /// Each output group will be associated with a `Seq` of corresponding context elements.
        /// </summary>
        public static SourceWithContext<IReadOnlyList<TOut>, IReadOnlyList<TCtx>, TMat> Grouped<TOut, TCtx, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, int n)
        {
            var stage = new Grouped<(TOut, TCtx)>(n);
            return flow.Via(Flow.FromGraph(stage).Select(itemsWithContexts =>
            {
                var items = new List<TOut>(n);
                var ctxs = new List<TCtx>(n);

                foreach (var tuple in itemsWithContexts)
                {
                    items.Add(tuple.Item1);
                    ctxs.Add(tuple.Item2);
                }

                return ((IReadOnlyList<TOut>)items, (IReadOnlyList<TCtx>)ctxs);
            }));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Sliding{TOut,TMat}"/>
        /// Each output group will be associated with a `Seq` of corresponding context elements.
        /// </summary>
        public static SourceWithContext<IReadOnlyList<TOut>, IReadOnlyList<TCtx>, TMat> Sliding<TOut, TCtx, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, int n, int step = 1)
        {
            var stage = new Sliding<(TOut, TCtx)>(n, step);
            return flow.Via(Flow.FromGraph(stage).Select(itemsWithContexts =>
            {
                var items = new List<TOut>(n);
                var ctxs = new List<TCtx>(n);

                foreach (var tuple in itemsWithContexts)
                {
                    items.Add(tuple.Item1);
                    ctxs.Add(tuple.Item2);
                }

                return ((IReadOnlyList<TOut>)items, (IReadOnlyList<TCtx>)ctxs);
            }));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.SelectMany{T,TOut,TMat}"/>.
        /// The context of the input element will be associated with each of the output elements calculated from
        /// this input element.
        /// </summary>
        public static SourceWithContext<TOut2, TCtx, TMat> SelectConcat<TOut, TCtx, TOut2, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, Func<TOut, IEnumerable<TOut2>> fn) =>
            StatefulSelectConcat(flow, () => fn);

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.StatefulSelectMany{T,TOut,TMat}"/>.
        /// </summary>
        public static SourceWithContext<TOut2, TCtx, TMat> StatefulSelectConcat<TOut, TCtx, TOut2, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, Func<Func<TOut, IEnumerable<TOut2>>> fn)
        {
            var stage = new StatefulSelectMany<(TOut, TCtx), (TOut2, TCtx)>(() =>
            {
                var fun = fn();
                return itemWithContext =>
                {
                    var items = fun(itemWithContext.Item1);
                    return items.Select(i => (i, itemWithContext.Item2));
                };
            });
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Apply the given function to each context element (leaving the data elements unchanged).
        /// </summary>
        public static SourceWithContext<TOut, TCtx2, TMat> SelectContext<TOut, TCtx, TCtx2, TMat>(
            this SourceWithContext<TOut, TCtx, TMat> flow, Func<TCtx, TCtx2> mapContext)
        {
            var stage = new Select<(TOut, TCtx), (TOut, TCtx2)>(x =>
                (x.Item1, mapContext(x.Item2)));
            return flow.Via(Flow.FromGraph(stage));
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="FlowWithContextOperations.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Akka.Streams.Implementation.Fusing;

namespace Akka.Streams.Dsl
{
    public static class FlowWithContextOperations
    {
        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Select{T,TIn,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TCtx, TIn, TCtx, TOut2, TMat> Select<TCtx, TIn, TOut, TOut2, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, Func<TOut, TOut2> fn)
        {
            var stage = new Select<(TOut, TCtx), (TOut2, TCtx)>(x => (fn(x.Item1), x.Item2));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.SelectAsync{T,TIn,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TCtx, TIn, TCtx, TOut2, TMat> SelectAsync<TCtx, TIn, TOut, TOut2, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, int parallelism, Func<TOut, Task<TOut2>> fn)
        {
            var stage = new SelectAsync<(TOut, TCtx), (TOut2, TCtx)>(parallelism,
                async x => (await fn(x.Item1), x.Item2));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Collect{T,TIn,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TCtx, TIn, TCtx, TOut2, TMat> Collect<TCtx, TIn, TOut, TOut2, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, Func<TOut, TOut2> fn) where TOut2 : class
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
        public static FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> Where<TCtx, TIn, TOut, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, Func<TOut, bool> predicate)
        {
            var stage = new Where<(TOut, TCtx)>(x => predicate(x.Item1));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Grouped{TIn,TOut,TMat}"/>
        /// </summary>
        public static FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> WhereNot<TCtx, TIn, TOut, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, Func<TOut, bool> predicate)
        {
            var stage = new Where<(TOut, TCtx)>(x => !predicate(x.Item1));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.Grouped{TIn,TOut,TMat}"/>
        /// Each output group will be associated with a `Seq` of corresponding context elements.
        /// </summary>
        public static FlowWithContext<TCtx, TIn, IReadOnlyList<TCtx>, IReadOnlyList<TOut>, TMat> Grouped<TCtx, TIn,
            TOut, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, int n)
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
        public static FlowWithContext<TCtx, TIn, IReadOnlyList<TCtx>, IReadOnlyList<TOut>, TMat> Sliding<TCtx, TIn,
            TOut, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, int n, int step = 1)
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
        /// Context-preserving variant of <see cref="FlowOperations.SelectMany{T,TIn,TOut,TMat}"/>.
        /// The context of the input element will be associated with each of the output elements calculated from
        /// this input element.
        /// </summary>
        public static FlowWithContext<TCtx, TIn, TCtx, TOut2, TMat> SelectConcat<TCtx, TIn, TOut, TOut2, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, Func<TOut, IEnumerable<TOut2>> fn) =>
            StatefulSelectConcat(flow, () => fn);

        /// <summary>
        /// Context-preserving variant of <see cref="FlowOperations.StatefulSelectMany{T,TIn,TOut,TMat}"/>.
        /// </summary>
        public static FlowWithContext<TCtx, TIn, TCtx, TOut2, TMat> StatefulSelectConcat<TCtx, TIn, TOut, TOut2, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, Func<Func<TOut, IEnumerable<TOut2>>> fn)
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
        public static FlowWithContext<TCtx, TIn, TCtx2, TOut, TMat> SelectContext<TCtx, TIn, TCtx2, TOut, TMat>(
            this FlowWithContext<TCtx, TIn, TCtx, TOut, TMat> flow, Func<TCtx, TCtx2> mapContext)
        {
            var stage = new Select<(TOut, TCtx), (TOut, TCtx2)>(x =>
                (x.Item1, mapContext(x.Item2)));
            return flow.Via(Flow.FromGraph(stage));
        }
    }
    
    
    public static class SourceWithContextOperations
    {
        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Select{T,TOut,TMat}"/>
        /// </summary>
        public static SourceWithContext<TCtx, TOut2, TMat> Select<TCtx, TOut, TOut2, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, Func<TOut, TOut2> fn)
        {
            var stage = new Select<(TOut, TCtx), (TOut2, TCtx)>(x => (fn(x.Item1), x.Item2));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.SelectAsync{T,TOut,TMat}"/>
        /// </summary>
        public static SourceWithContext<TCtx, TOut2, TMat> SelectAsync<TCtx, TOut, TOut2, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, int parallelism, Func<TOut, Task<TOut2>> fn)
        {
            var stage = new SelectAsync<(TOut, TCtx), (TOut2, TCtx)>(parallelism,
                async x => (await fn(x.Item1), x.Item2));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Collect{T,TOut,TMat}"/>
        /// </summary>
        public static SourceWithContext<TCtx, TOut2, TMat> Collect<TCtx, TOut, TOut2, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, Func<TOut, TOut2> fn) where TOut2 : class
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
        public static SourceWithContext<TCtx, TOut, TMat> Where<TCtx, TOut, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, Func<TOut, bool> predicate)
        {
            var stage = new Where<(TOut, TCtx)>(x => predicate(x.Item1));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Grouped{TOut,TMat}"/>
        /// </summary>
        public static SourceWithContext<TCtx, TOut, TMat> WhereNot<TCtx, TOut, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, Func<TOut, bool> predicate)
        {
            var stage = new Where<(TOut, TCtx)>(x => !predicate(x.Item1));
            return flow.Via(Flow.FromGraph(stage));
        }

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.Grouped{TOut,TMat}"/>
        /// Each output group will be associated with a `Seq` of corresponding context elements.
        /// </summary>
        public static SourceWithContext<IReadOnlyList<TCtx>, IReadOnlyList<TOut>, TMat> Grouped<TCtx, TOut, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, int n)
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
        public static SourceWithContext<IReadOnlyList<TCtx>, IReadOnlyList<TOut>, TMat> Sliding<TCtx, TOut, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, int n, int step = 1)
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
        public static SourceWithContext<TCtx, TOut2, TMat> SelectConcat<TCtx, TOut, TOut2, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, Func<TOut, IEnumerable<TOut2>> fn) =>
            StatefulSelectConcat(flow, () => fn);

        /// <summary>
        /// Context-preserving variant of <see cref="SourceOperations.StatefulSelectMany{T,TOut,TMat}"/>.
        /// </summary>
        public static SourceWithContext<TCtx, TOut2, TMat> StatefulSelectConcat<TCtx, TOut, TOut2, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, Func<Func<TOut, IEnumerable<TOut2>>> fn)
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
        public static SourceWithContext<TCtx2, TOut, TMat> SelectContext<TCtx, TCtx2, TOut, TMat>(
            this SourceWithContext<TCtx, TOut, TMat> flow, Func<TCtx, TCtx2> mapContext)
        {
            var stage = new Select<(TOut, TCtx), (TOut, TCtx2)>(x =>
                (x.Item1, mapContext(x.Item2)));
            return flow.Via(Flow.FromGraph(stage));
        }
    }
}

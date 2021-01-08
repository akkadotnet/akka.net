//-----------------------------------------------------------------------
// <copyright file="EnumeratorInterpreter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// TBD
    /// </summary>
    internal static class EnumeratorInterpreter
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        public sealed class EnumeratorUpstream<TIn> : GraphInterpreter.UpstreamBoundaryStageLogic
        {
            /// <summary>
            /// TBD
            /// </summary>
            public bool HasNext;

            private readonly Outlet<TIn> _outlet;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="input">TBD</param>
            public EnumeratorUpstream(IEnumerator<TIn> input)
            {
                _outlet = new Outlet<TIn>("IteratorUpstream.out") { Id = 0 };

                SetHandler(_outlet, onPull: () =>
                {
                    if (!HasNext) CompleteStage();
                    else
                    {
                        var element = input.Current;
                        HasNext = input.MoveNext();
                        if (!HasNext)
                        {
                            Push(_outlet, element);
                            Complete(_outlet);
                        }
                        else Push(_outlet, element);
                    }
                },
                onDownstreamFinish: CompleteStage);
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override Outlet Out => _outlet;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        public sealed class EnumeratorDownstream<TOut> : GraphInterpreter.DownstreamBoundaryStageLogic, IEnumerator<TOut>
        {
            /// <summary>
            /// TBD
            /// </summary>
            internal bool IsDone;
            /// <summary>
            /// TBD
            /// </summary>
            internal TOut NextElement;
            /// <summary>
            /// TBD
            /// </summary>
            internal bool NeedsPull = true;
            /// <summary>
            /// TBD
            /// </summary>
            internal Exception LastFailure;

            private readonly Inlet<TOut> _inlet;

            /// <summary>
            /// TBD
            /// </summary>
            public EnumeratorDownstream()
            {
                _inlet = new Inlet<TOut>("IteratorDownstream.in") { Id = 0 };

                SetHandler(_inlet, onPush: () =>
                {
                    NextElement = Grab(_inlet);
                    NeedsPull = false;
                }, 
                onUpstreamFinish: () =>
                {
                    IsDone = true;
                    CompleteStage();
                }, 
                onUpstreamFailure: cause =>
                {
                    IsDone = true;
                    LastFailure = cause;
                    CompleteStage();
                });
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override Inlet In => _inlet;

            /// <summary>
            /// TBD
            /// </summary>
            public void Dispose() { }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public bool MoveNext()
            {
                if (LastFailure != null)
                {
                    var e = LastFailure;
                    LastFailure = null;
                    throw e;
                }
                if (!HasNext())
                    return false;

                NeedsPull = true;
                return true;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void Reset()
            {
                IsDone = false;
                NextElement = default(TOut);
                NeedsPull = true;
                LastFailure = null;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public bool HasNext()
            {
                if(!IsDone)
                    PullIfNeeded();

                return !(IsDone && NeedsPull) || LastFailure != null;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public TOut Current => NextElement;

            object IEnumerator.Current => Current;

            private void PullIfNeeded()
            {
                if (NeedsPull)
                {
                    Pull(_inlet);
                    Interpreter.Execute(int.MaxValue);
                }
            }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    internal sealed class EnumeratorInterpreter<TIn, TOut> : IEnumerable<TOut>
    {
        private readonly IEnumerable<PushPullStage<TIn, TOut>> _ops;
        private readonly EnumeratorInterpreter.EnumeratorUpstream<TIn> _upstream;
        private readonly EnumeratorInterpreter.EnumeratorDownstream<TOut> _downstream = new EnumeratorInterpreter.EnumeratorDownstream<TOut>();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="input">TBD</param>
        /// <param name="ops">TBD</param>
        public EnumeratorInterpreter(IEnumerator<TIn> input, IEnumerable<PushPullStage<TIn, TOut>> ops)
        {
            _ops = ops;
            _upstream = new EnumeratorInterpreter.EnumeratorUpstream<TIn>(input);

            Init();
        }

        private void Init()
        {
            var i = 0;
            var length = _ops.Count();
            var attributes = new Attributes[length];
            for (var j = 0; j < length; j++) attributes[j] = Attributes.None;
            var ins = new Inlet[length + 1];
            var inOwners = new int[length + 1];
            var outs = new Outlet[length + 1];
            var outOwners = new int[length + 1];
            var stages = new IGraphStageWithMaterializedValue<Shape, object>[length];

            ins[length] = null;
            inOwners[length] = GraphInterpreter.Boundary;
            outs[0] = null;
            outOwners[0] = GraphInterpreter.Boundary;

            var opsEnumerator = _ops.GetEnumerator();
            while (opsEnumerator.MoveNext())
            {
                var op = opsEnumerator.Current;
                var stage = new PushPullGraphStage<TIn, TOut>(_ => op, Attributes.None);
                stages[i] = stage;
                ins[i] = stage.Shape.Inlet;
                inOwners[i] = i;
                outs[i + 1] = stage.Shape.Outlet;
                outOwners[i + 1] = i;

                i++;
            }

            var assembly = new GraphAssembly(stages, attributes, ins, inOwners, outs, outOwners);
            var tup = assembly.Materialize(Attributes.None, assembly.Stages.Select(x => x.Module).ToArray(), new Dictionary<IModule, object>(), _ => { });
            var connections = tup.Item1;
            var logics = tup.Item2;

            var interpreter = new GraphInterpreter(
                assembly: assembly, 
                materializer: NoMaterializer.Instance, 
                log: NoLogger.Instance,
                connections: connections,
                logics: logics,
                onAsyncInput: (_1, _2, _3) => throw new NotSupportedException("IteratorInterpreter does not support asynchronous events."),
                fuzzingMode: false,
                context: null);
            interpreter.AttachUpstreamBoundary(0, _upstream);
            interpreter.AttachDownstreamBoundary(length, _downstream);
            interpreter.Init(null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerator<TOut> GetEnumerator() => _downstream;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

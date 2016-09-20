//-----------------------------------------------------------------------
// <copyright file="EnumeratorInterpreter.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    internal static class EnumeratorInterpreter
    {
        public sealed class EnumeratorUpstream<TIn> : GraphInterpreter.UpstreamBoundaryStageLogic
        {
            public bool HasNext;
            public EnumeratorUpstream(IEnumerator<TIn> input)
            {
                Out = new Outlet<TIn>("IteratorUpstream.out") { Id = 0 };
                SetHandler(Out, onPull: () =>
                {
                    if (!HasNext) CompleteStage();
                    else
                    {
                        var element = input.Current;
                        HasNext = input.MoveNext();
                        if (!HasNext)
                        {
                            Push(Out, element);
                            Complete(Out);
                        }
                        else Push(Out, element);
                    }
                },
                onDownstreamFinish: CompleteStage);
            }

            public override Outlet Out { get; }
        }

        public sealed class EnumeratorDownstream<TOut> : GraphInterpreter.DownstreamBoundaryStageLogic, IEnumerator<TOut>
        {
            internal bool IsDone;
            internal TOut NextElement;
            internal bool NeedsPull = true;
            internal Exception LastFailure;

            public EnumeratorDownstream()
            {
                In = new Inlet<TOut>("IteratorDownstream.in") { Id = 0 };
                SetHandler(In, onPush: () =>
                {
                    NextElement = Grab<TOut>(In);
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

            public override Inlet In { get; }

            public void Dispose() { }

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

            public void Reset()
            {
                IsDone = false;
                NextElement = default(TOut);
                NeedsPull = true;
                LastFailure = null;
            }

            public bool HasNext()
            {
                if(!IsDone)
                    PullIfNeeded();

                return !(IsDone && NeedsPull) || LastFailure != null;
            }

            public TOut Current => NextElement;

            object IEnumerator.Current => Current;

            private void PullIfNeeded()
            {
                if (NeedsPull)
                {
                    Pull(In);
                    Interpreter.Execute(int.MaxValue);
                }
            }
        }
    }

    internal sealed class EnumeratorInterpreter<TIn, TOut> : IEnumerable<TOut>
    {
        private readonly IEnumerable<PushPullStage<TIn, TOut>> _ops;
        private readonly EnumeratorInterpreter.EnumeratorUpstream<TIn> _upstream;
        private readonly EnumeratorInterpreter.EnumeratorDownstream<TOut> _downstream = new EnumeratorInterpreter.EnumeratorDownstream<TOut>();
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
            var inHandlers = tup.Item1;
            var outHandlers = tup.Item2;
            var logics = tup.Item3;

            var interpreter = new GraphInterpreter(
                assembly: assembly, 
                materializer: NoMaterializer.Instance, 
                log: NoLogger.Instance, 
                inHandlers: inHandlers,
                outHandlers: outHandlers,
                logics: logics,
                onAsyncInput: (_1, _2, _3) => { throw new NotSupportedException("IteratorInterpreter does not support asynchronous events.");},
                fuzzingMode: false,
                context: null);
            interpreter.AttachUpstreamBoundary(0, _upstream);
            interpreter.AttachDownstreamBoundary(length, _downstream);
            interpreter.Init(null);
        }

        public IEnumerator<TOut> GetEnumerator() => _downstream;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
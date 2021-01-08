//-----------------------------------------------------------------------
// <copyright file="SourceGen.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Dsl
{
    public static class SourceGen
    {
        /// <summary>
        /// EXPERIMENTAL API
        /// <para>
        /// Create a source that will unfold a value of type <typeparamref name="TState"/> by
        /// passing it through a flow. The flow should emit a
        /// pair of the next state <typeparamref name="TState"/> and output elements of type <typeparamref name="TOut"/>.
        /// Source completes when the flow completes.
        /// </para>
        /// <para>
        /// The <paramref name="timeout"/> parameter specifies waiting time after inner
        /// flow provided by the user for unfold flow API cancels
        /// upstream, to get also the downstream cancelation (as
        /// graceful completion or failure which is propagated).
        /// If inner flow fails to complete/fail downstream, stage is failed.                
        /// </para>
        /// <para>
        /// IMPORTANT CAVEAT:
        /// The given flow must not change the number of elements passing through it(i.e.it should output
        /// exactly one element for every received element). Ignoring this, will have an unpredicted result,
        /// and may result in a deadlock.
        /// </para>
        /// </summary>
        /// <typeparam name="TState">state type</typeparam>
        /// <typeparam name="TOut">output elements type</typeparam>
        /// <typeparam name="TMat">materialized value type</typeparam>
        /// <param name="seed">intial state</param>
        /// <param name="flow">flow, through which value is passed</param>
        /// <param name="timeout">timeout</param>
        [ApiMayChange]
        public static Source<TOut, TMat> UnfoldFlow<TState, TOut, TMat>(TState seed, IGraph<FlowShape<TState, (TState, TOut)>, TMat> flow, TimeSpan timeout)
        {
            return UnfoldFlowGraph(new FanOut2UnfoldingStage<(TState, TOut), TState, TOut>(shape => new UnfoldFlowGraphStageLogic<TState, TOut>(shape, seed, timeout)), flow);
        }

        /// <summary>
        /// EXPERIMENTAL API
        /// <para>
        /// Create a source that will unfold a value of type <typeparamref name="TState"/> by
        /// passing it through a flow. The flow should emit an output
        /// value of type <typeparamref name="TFlowOut"/>, that when fed to the unfolding function,
        /// generates a pair of the next state <typeparamref name="TState"/> and output elements of type <typeparamref name="TOut"/>.
        /// </para>
        /// <para>
        /// The <paramref name="timeout"/> parameter specifies waiting time after inner
        /// flow provided by the user for unfold flow API cancels
        /// upstream, to get also the downstream cancelation(as
        /// graceful completion or failure which is propagated).
        /// If inner flow fails to complete/fail downstream, stage is failed.
        /// </para>
        /// <para>
        /// IMPORTANT CAVEAT:
        /// The given flow must not change the number of elements passing through it(i.e.it should output
        /// exactly one element for every received element). Ignoring this, will have an unpredicted result,
        /// and may result in a deadlock.
        /// </para>
        /// </summary>
        /// <typeparam name="TOut">output elements type</typeparam>
        /// <typeparam name="TState">state type</typeparam>
        /// <typeparam name="TFlowOut">flow output value type</typeparam>
        /// <typeparam name="TMat">materialized value type</typeparam>
        /// <param name="seed">intial state</param>
        /// <param name="flow">flow through which value is passed</param>
        /// <param name="unfoldWith">unfolding function</param>
        /// <param name="timeout">timeout</param>
        [ApiMayChange]
        public static Source<TOut, TMat> UnfoldFlowWith<TOut, TState, TFlowOut, TMat>(TState seed, IGraph<FlowShape<TState, TFlowOut>, TMat> flow, Func<TFlowOut, Option<(TState, TOut)>> unfoldWith, TimeSpan timeout)
        {
            return UnfoldFlowGraph(new FanOut2UnfoldingStage<TFlowOut, TState, TOut>(shape => new UnfoldFlowWithGraphStageLogic<TFlowOut, TState, TOut>(shape, seed, unfoldWith, timeout)), flow);
        }

        private class UnfoldFlowGraphStageLogic<TState, TOut> : UnfoldFlowGraphStageLogic<(TState, TOut), TState, TOut>, IInHandler
        {
            public UnfoldFlowGraphStageLogic(FanOutShape<(TState, TOut), TState, TOut> shape, TState seed, TimeSpan timeout) : base(shape, seed, timeout)
            {
                SetHandler(_nextElem, this);
            }

            public void OnPush()
            {
                var t = Grab(_nextElem);
                var s = t.Item1;
                var e = t.Item2;
                _pending = s;
                Push(_output, e);
                _pushedToCycle = false;
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);
        }

        private class UnfoldFlowWithGraphStageLogic<TFlowOut, TState, TOut> : UnfoldFlowGraphStageLogic<TFlowOut, TState, TOut>, IInHandler
        {
            private readonly Func<TFlowOut, Option<(TState, TOut)>> _unfoldWith;

            public UnfoldFlowWithGraphStageLogic(FanOutShape<TFlowOut, TState, TOut> shape, TState seed, Func<TFlowOut, Option<(TState, TOut)>> unfoldWith, TimeSpan timeout) : base(shape, seed, timeout)
            {
                _unfoldWith = unfoldWith;

                SetHandler(_nextElem, this);
            }

            public void OnPush()
            {
                var elem = Grab(_nextElem);
                var unfolded = _unfoldWith(elem);

                if (unfolded.HasValue)
                {
                    var s = unfolded.Value.Item1;
                    var e = unfolded.Value.Item2;
                    _pending = s;
                    Push(_output, e);
                    _pushedToCycle = false;
                }
                else
                    CompleteStage();
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);
        }

        private static Source<TOut, TMat> UnfoldFlowGraph<TOut, TState, TFlowOut, TMat>(GraphStage<FanOutShape<TFlowOut, TState, TOut>> fanOut2Stage, IGraph<FlowShape<TState, TFlowOut>, TMat> flow)
        {
            var graph = GraphDsl.Create(flow, (b, f) =>
            {
                var fo2 = b.Add(fanOut2Stage);
                b.From(fo2.Out0).Via(f).To(fo2.In);

                return new SourceShape<TOut>(fo2.Out1);
            });

            return Source.FromGraph(graph);
        }
    }
}

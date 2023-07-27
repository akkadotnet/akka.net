//-----------------------------------------------------------------------
// <copyright file="Sources.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Annotations;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace Akka.Streams.Implementation
{
    [InternalApi]
    public sealed class SetupFlowStage<TIn, TOut, TMat> : GraphStageWithMaterializedValue<FlowShape<TIn, TOut>, Task<TMat>>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly SetupFlowStage<TIn, TOut, TMat> _stage;
            private readonly TaskCompletionSource<TMat> _matPromise;
            private readonly Attributes _inheritedAttributes;
            private readonly SubSinkInlet<TOut> _subInlet;
            private readonly SubSourceOutlet<TIn> _subOutlet;

            public Logic(SetupFlowStage<TIn, TOut, TMat> stage, TaskCompletionSource<TMat> matPromise, Attributes inheritedAttributes)
                : base(stage.Shape)
            {
                _stage = stage;
                _matPromise = matPromise;
                _inheritedAttributes = inheritedAttributes;

                _subInlet = new SubSinkInlet<TOut>(this, "SetupFlowStage");
                _subOutlet = new SubSourceOutlet<TIn>(this, "SetupFlowStage");

                _subInlet.SetHandler(new LambdaInHandler(
                    onPush: () => Push(_stage.Out, _subInlet.Grab()),
                    onUpstreamFinish: () => Complete(_stage.Out),
                    onUpstreamFailure: ex => Fail(_stage.Out, ex)));
                _subOutlet.SetHandler(new LambdaOutHandler(
                    onPull: () => Pull(_stage.In),
                    onDownstreamFinish: ex => Cancel(_stage.In, ex)));

                SetHandler(_stage.In, new LambdaInHandler(
                    onPush: () => Grab(_stage.In),
                    onUpstreamFinish: _subOutlet.Complete,
                    onUpstreamFailure: _subOutlet.Fail));
                SetHandler(_stage.Out, new LambdaOutHandler(
                    onPull: _subInlet.Pull,
                    onDownstreamFinish: _subInlet.Cancel));
            }

            public override void PreStart()
            {
                base.PreStart();

                try
                {
                    var flow = _stage._factory(ActorMaterializerHelper.Downcast(Materializer), _inheritedAttributes);
                    var mat = SubFusingMaterializer.Materialize(
                        Source.FromGraph(_subOutlet.Source)
                            .ViaMaterialized(flow, Keep.Right)
                            .To(Sink.FromGraph(_subInlet.Sink)), _inheritedAttributes);
                    _matPromise.SetResult(mat);
                }
                catch (Exception ex)
                {
                    _matPromise.SetException(ex);
                    throw;
                }
            }
        }

        #endregion

        private readonly Func<ActorMaterializer, Attributes, Flow<TIn, TOut, TMat>> _factory;

        public SetupFlowStage(Func<ActorMaterializer, Attributes, Flow<TIn, TOut, TMat>> factory)
        {
            _factory = factory;
            Shape = new FlowShape<TIn, TOut>(In, Out);
        }

        public Inlet<TIn> In { get; } = new("SetupFlowStage.in");

        public Outlet<TOut> Out { get; } = new("SetupFlowStage.out");

        public override FlowShape<TIn, TOut> Shape { get; }

        protected override Attributes InitialAttributes => Attributes.CreateName("setup");

        public override ILogicAndMaterializedValue<Task<TMat>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var matPromise = new TaskCompletionSource<TMat>();
            var logic = new Logic(this, matPromise, inheritedAttributes);
            return new LogicAndMaterializedValue<Task<TMat>>(logic, matPromise.Task);
        }
    }

    [InternalApi]
    public sealed class SetupSourceStage<TOut, TMat> : GraphStageWithMaterializedValue<SourceShape<TOut>, Task<TMat>>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly SetupSourceStage<TOut, TMat> _stage;
            private readonly TaskCompletionSource<TMat> _matPromise;
            private readonly Attributes _inheritedAttributes;
            private readonly SubSinkInlet<TOut> _subInlet;

            public Logic(SetupSourceStage<TOut, TMat> stage, TaskCompletionSource<TMat> matPromise, Attributes inheritedAttributes)
                : base(stage.Shape)
            {
                _stage = stage;
                _matPromise = matPromise;
                _inheritedAttributes = inheritedAttributes;

                _subInlet = new SubSinkInlet<TOut>(this, "SetupSourceStage");
                _subInlet.SetHandler(new LambdaInHandler(
                    onPush: () => Push(_stage.Out, _subInlet.Grab()),
                    onUpstreamFinish: () => Complete(_stage.Out),
                    onUpstreamFailure: ex => Fail(_stage.Out, ex)));

                SetHandler(_stage.Out, new LambdaOutHandler(onPull: _subInlet.Pull, onDownstreamFinish: _subInlet.Cancel));
            }

            public override void PreStart()
            {
                base.PreStart();

                try
                {
                    var source = _stage._factory(ActorMaterializerHelper.Downcast(Materializer), _inheritedAttributes);
                    var mat = SubFusingMaterializer.Materialize(source.To(Sink.FromGraph(_subInlet.Sink)), _inheritedAttributes);
                    _matPromise.SetResult(mat);
                }
                catch (Exception ex)
                {
                    _matPromise.SetException(ex);
                    throw;
                }
            }
        }

        #endregion

        private readonly Func<ActorMaterializer, Attributes, Source<TOut, TMat>> _factory;

        /// <summary>
        /// Creates a new <see cref="LazySource{TOut,TMat}"/>
        /// </summary>
        /// <param name="factory">The factory that generates the source when needed</param>
        public SetupSourceStage(Func<ActorMaterializer, Attributes, Source<TOut, TMat>> factory)
        {
            _factory = factory;
            Shape = new SourceShape<TOut>(Out);
        }

        public Outlet<TOut> Out { get; } = new("SetupSourceStage.out");

        public override SourceShape<TOut> Shape { get; }

        protected override Attributes InitialAttributes => Attributes.CreateName("setup");

        public override ILogicAndMaterializedValue<Task<TMat>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var matPromise = new TaskCompletionSource<TMat>();
            var logic = new Logic(this, matPromise, inheritedAttributes);
            return new LogicAndMaterializedValue<Task<TMat>>(logic, matPromise.Task);
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="ReuseLatest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Combines the latest elements from a set inputs. Emits first time when all inputs have an element available, then
    /// each time a new value is available
    ///
    /// </summary>
    /// <typeparam name="TIn">The input type.</typeparam>
    /// <typeparam name="TOut">The combined output type.</typeparam>

    public sealed class CombineLatest<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> {
        
        private readonly Func<IImmutableList<TIn>, TOut> _combine;
        private readonly int _n;

        public CombineLatest(Func<IImmutableList<TIn>, TOut> combine, int n) {
            _combine = combine;
            _n = n;
            Shape = new UniformFanInShape<TIn, TOut>(n);
            Out = Shape.Out;
            Inlets = Shape.Ins;
        }

        private Outlet<TOut> Out { get; }
        private IImmutableList<Inlet<TIn>> Inlets { get; }
        public override UniformFanInShape<TIn, TOut> Shape { get; }

        private sealed class Logic : OutGraphStageLogic {
            private readonly CombineLatest<TIn, TOut> _stage;

            public Logic(CombineLatest<TIn, TOut> stage) : base(stage.Shape) {
                
                _stage = stage;

                var buffer = new Option<TIn>[_stage._n];

                _stage.Inlets.ForEach(inlet => {
                    SetHandler(inlet, onPush: () => {
                        
                        buffer[_stage.Inlets.IndexOf(inlet)] = Grab(inlet);
                        
                        if (buffer.All((t) => t.HasValue) && IsAvailable(_stage.Out))
                            Push(_stage.Out, _stage._combine(buffer.Select((t) => t.Value).ToImmutableArray()));
                        else
                            PullIfNeeded(inlet);
                    }, onUpstreamFinish: () => {
                        
                        if (!IsAvailable(inlet))
                            CompleteStage();
                        
                    });
                });

                SetHandler(_stage.Out, this);
            }

            public override void OnPull() {
                _stage.Inlets.ForEach(PullIfNeeded);
            }

            private void PullIfNeeded(Inlet<TIn> inlet) {
                if (!HasBeenPulled(inlet))
                    TryPull(inlet);
            }

            public override string ToString() => "CombineLatestLogic";
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "CombineLatest";
    }
}

//-----------------------------------------------------------------------
// <copyright file="AccumulateWhileUnchanged.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Accumulates elements of type <typeparamref name="TElement"/> while extracted property of type <typeparamref name="TProperty"/> remains unchanged,
    /// emits an accumulated sequence when the property changes
    /// </summary>
    /// <typeparam name="TElement">type of accumulated elements</typeparam>
    /// <typeparam name="TProperty">type of the observed property</typeparam>
    public class AccumulateWhileUnchanged<TElement, TProperty> : GraphStage<FlowShape<TElement, IEnumerable<TElement>>>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            private Option<TProperty> _currentState = Option<TProperty>.None;
            private readonly List<TElement> _buffer = new List<TElement>();

            public Logic(AccumulateWhileUnchanged<TElement, TProperty> accumulateWhileUnchanged) : base(accumulateWhileUnchanged.Shape)
            {
                SetHandler(accumulateWhileUnchanged.In, onPush: () =>
                {
                    var nextElement = Grab(accumulateWhileUnchanged.In);
                    var nextState = accumulateWhileUnchanged._propertyExtractor(nextElement);

                    if (!_currentState.HasValue)
                        _currentState = new Option<TProperty>(nextState);

                    if (EqualityComparer<TProperty>.Default.Equals(_currentState.Value, nextState))
                    {
                        _buffer.Add(nextElement);
                        Pull(accumulateWhileUnchanged.In);
                    }
                    else
                    {
                        var result = _buffer.ToArray();
                        _buffer.Clear();
                        _buffer.Add(nextElement);
                        Push(accumulateWhileUnchanged.Out, result);
                        _currentState = new Option<TProperty>(nextState);
                    }
                }, onUpstreamFinish: () =>
                {
                    var result = _buffer.ToArray();
                    if (result.Any())
                        Emit(accumulateWhileUnchanged.Out, result);
                    CompleteStage();
                });

                SetHandler(accumulateWhileUnchanged.Out, onPull: () =>
                {
                    Pull(accumulateWhileUnchanged.In);
                });
            }

            public override void PostStop()
            {
                _buffer.Clear();
            }
        }

        #endregion

        private readonly Func<TElement, TProperty> _propertyExtractor;

        /// <param name="propertyExtractor">a function to extract the observed element property</param>
        public AccumulateWhileUnchanged(Func<TElement, TProperty> propertyExtractor)
        {
            _propertyExtractor = propertyExtractor;

            In = new Inlet<TElement>("AccumulateWhileUnchanged.in");
            Out = new Outlet<IEnumerable<TElement>>("AccumulateWhileUnchanged.out");
            Shape = new FlowShape<TElement, IEnumerable<TElement>>(In, Out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override FlowShape<TElement, IEnumerable<TElement>> Shape { get; }

        public Inlet<TElement> In { get; }
        public Outlet<IEnumerable<TElement>> Out { get; }
    }
}

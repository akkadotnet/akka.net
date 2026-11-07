//-----------------------------------------------------------------------
// <copyright file="One2OneBidiFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Pattern;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Factory for bidirectional flows that pair each input with one output.
    /// </summary>
    public static class One2OneBidiFlow
    {
        /// <summary>
        /// Creates a bidirectional flow that allows each side's responses to arrive independently while preserving the request-response count.
        /// </summary>
        /// <typeparam name="TIn">The type of elements on the request side.</typeparam>
        /// <typeparam name="TOut">The type of elements on the response side.</typeparam>
        /// <param name="maxPending">The maximum number of requests awaiting corresponding responses, or -1 for no limit.</param>
        /// <returns>A bidirectional flow with identity transformations in both directions and no materialized value.</returns>
        public static BidiFlow<TIn, TIn, TOut, TOut, NotUsed> Apply<TIn, TOut>(int maxPending)
        {
            return BidiFlow.FromGraph(new One2OneBidi<TIn, TOut>(maxPending));
        }
    }

    /// <summary>
    /// Signals that a response arrived without a corresponding request-side element.
    /// </summary>
    public class UnexpectedOutputException : Exception
    {
        /// <summary>
        /// Creates an exception for an unexpected response element.
        /// </summary>
        /// <param name="element">The response that had no pending request.</param>
        public UnexpectedOutputException(object element) : base(element.ToString())
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnexpectedOutputException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected UnexpectedOutputException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Signals that the response side completed while requests were still awaiting responses.
    /// </summary>
    public class OutputTruncationException : Exception
    {
        public OutputTruncationException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="UserCalledFailException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected OutputTruncationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// A bidirectional stage that forwards request-side and response-side elements while enforcing one response per request.
    /// </summary>
    /// <typeparam name="TIn">The type of elements on the request side.</typeparam>
    /// <typeparam name="TOut">The type of elements on the response side.</typeparam>
    public class One2OneBidi<TIn, TOut> : GraphStage<BidiShape<TIn, TIn, TOut, TOut>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly int _maxPending;
            private readonly Inlet<TIn> _inInlet;
            private readonly Outlet<TIn> _inOutlet;
            private readonly Inlet<TOut> _outInlet;
            private readonly Outlet<TOut> _outOutlet;
            private int _pending;
            private bool _pullSuppressed;

            public Logic(One2OneBidi<TIn, TOut> stage) : base(stage.Shape)
            {
                _maxPending = stage._maxPending;
                _inInlet = stage._inInlet;
                _inOutlet = stage._inOutlet;
                _outInlet = stage._outInlet;
                _outOutlet = stage._outOutlet;

                SetInInletHandler();
                SetInOutletHandler();
                SetOutInletHandler();
                SetOutOutletHandler();
            }

            private void SetInInletHandler()
            {
                SetHandler(_inInlet, onPush: () =>
                {
                    _pending += 1;
                    Push(_inOutlet, Grab(_inInlet));
                },
                    onUpstreamFinish: () => Complete(_inOutlet));
            }

            private void SetInOutletHandler()
            {
                SetHandler(_inOutlet, onPull: () =>
                {
                    if (_pending < _maxPending || _maxPending == -1)
                        Pull(_inInlet);
                    else
                        _pullSuppressed = true;
                },
                    onDownstreamFinish: cause => Cancel(_inInlet, cause));
            }

            private void SetOutInletHandler()
            {
                SetHandler(_outInlet, onPush: () =>
                {
                    var element = Grab(_outInlet);

                    if (_pending <= 0)
                        throw new UnexpectedOutputException(element);

                    _pending -= 1;

                    Push(_outOutlet, element);

                    if (_pullSuppressed)
                    {
                        _pullSuppressed = false;
                        if(!IsClosed(_inInlet))
                            Pull(_inInlet);
                    }
                }, onUpstreamFinish: () =>
                {
                    if (_pending != 0)
                        throw new OutputTruncationException();

                    Complete(_outOutlet);
                });
            }

            private void SetOutOutletHandler()
            {
                SetHandler(_outOutlet, onPull: () => Pull(_outInlet), onDownstreamFinish: cause => Cancel(_outInlet, cause));
            }
        }

        #endregion

        private readonly int _maxPending;
        private readonly Inlet<TIn> _inInlet = new("inIn");
        private readonly Outlet<TIn> _inOutlet = new("inOut");
        private readonly Inlet<TOut> _outInlet = new("outIn");
        private readonly Outlet<TOut> _outOutlet = new("outOut");

        /// <summary>
        /// Creates the one-to-one bidirectional stage.
        /// </summary>
        /// <param name="maxPending">The maximum number of requests awaiting responses, or -1 for no limit.</param>
        public One2OneBidi(int maxPending)
        {
            _maxPending = maxPending;
            Shape = new BidiShape<TIn, TIn, TOut, TOut>(_inInlet, _inOutlet, _outInlet, _outOutlet);
        }

        /// <summary>
        /// The input and output ports for the request and response directions.
        /// </summary>
        public override BidiShape<TIn, TIn, TOut, TOut> Shape { get; }

        /// <summary>
        /// The default name attribute for this stage.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("One2OneBidi");

        /// <summary>
        /// Creates the stage logic that tracks pending request elements.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited from the enclosing graph.</param>
        /// <returns>The logic instance for this one-to-one stage.</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// Returns the stage name.
        /// </summary>
        /// <returns><c>One2OneBidi</c>.</returns>
        public override string ToString() => "One2OneBidi";
    }
}

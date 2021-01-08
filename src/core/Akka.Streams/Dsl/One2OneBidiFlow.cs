//-----------------------------------------------------------------------
// <copyright file="One2OneBidiFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class One2OneBidiFlow
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="maxPending">TBD</param>
        /// <returns>TBD</returns>
        public static BidiFlow<TIn, TIn, TOut, TOut, NotUsed> Apply<TIn, TOut>(int maxPending)
        {
            return BidiFlow.FromGraph(new One2OneBidi<TIn, TOut>(maxPending));
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class UnexpectedOutputException : Exception
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public UnexpectedOutputException(object element) : base(element.ToString())
        {

        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class OutputTruncationException : Exception
    {

    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
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
                    onDownstreamFinish: () => Cancel(_inInlet));
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
                SetHandler(_outOutlet, onPull: () => Pull(_outInlet), onDownstreamFinish: () => Cancel(_outInlet));
            }
        }

        #endregion

        private readonly int _maxPending;
        private readonly Inlet<TIn> _inInlet = new Inlet<TIn>("inIn");
        private readonly Outlet<TIn> _inOutlet = new Outlet<TIn>("inOut");
        private readonly Inlet<TOut> _outInlet = new Inlet<TOut>("outIn");
        private readonly Outlet<TOut> _outOutlet = new Outlet<TOut>("outOut");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxPending">TBD</param>
        public One2OneBidi(int maxPending)
        {
            _maxPending = maxPending;
            Shape = new BidiShape<TIn, TIn, TOut, TOut>(_inInlet, _inOutlet, _outInlet, _outOutlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override BidiShape<TIn, TIn, TOut, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("One2OneBidi");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "One2OneBidi";
    }
}

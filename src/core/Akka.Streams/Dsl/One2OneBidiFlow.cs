using System;
using System.Reactive.Streams;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    public static class One2OneBidiFlow
    {
        public static BidiFlow<TIn, TIn, TOut, TOut, Unit> Apply<TIn, TOut>(int maxPending)
        {
            return BidiFlow.FromGraph(new One2OneBidi<TIn, TOut>(maxPending));
        }
    }

    public class UnexpectedOutputException : SystemException
    {
        public UnexpectedOutputException(object element) : base(element.ToString())
        {

        }
    }

    public class OutputTruncationException : SystemException
    {

    }

    public class One2OneBidi<TIn, TOut> : GraphStage<BidiShape<TIn, TIn, TOut, TOut>>
    {
        #region internal classes

        private sealed class One2OneBidiGraphStateLogic<TIn, TOut> : GraphStageLogic
        {
            private readonly int _maxPending;
            private readonly Inlet<TIn> _inletIn;
            private readonly Outlet<TIn> _inletOut;
            private readonly Inlet<TOut> _outletIn;
            private readonly Outlet<TOut> _outletOut;
            private int _pending;
            private bool _pullSuppressed;

            public One2OneBidiGraphStateLogic(Shape shape, int maxPending, Inlet<TIn> inletIn, Outlet<TIn> inletOut,
                Inlet<TOut> outletIn, Outlet<TOut> outletOut) : base(shape)
            {
                _maxPending = maxPending;
                _inletIn = inletIn;
                _inletOut = inletOut;
                _outletIn = outletIn;
                _outletOut = outletOut;

                SetInletInHandler();
                SetInletOutHandler();
                SetOutletInHandler();
                SetOutletOutHandler();
            }

            private void SetInletInHandler()
            {
                SetHandler(_inletIn, onPush: () =>
                {
                    _pending += 1;
                    Push(_inletOut, Grab(_inletIn));
                },
                    onUpstreamFinish: () => Complete(_inletOut));
            }

            private void SetInletOutHandler()
            {
                SetHandler(_inletOut, onPull: () =>
                {
                    if (_pending < _maxPending || _maxPending == -1)
                        Pull(_inletIn);
                    else
                        _pullSuppressed = true;
                },
                    onDownstreamFinish: () => Cancel(_inletIn));
            }

            private void SetOutletInHandler()
            {
                SetHandler(_outletIn, onPush: () =>
                {
                    var element = Grab(_outletIn);

                    if (_pending <= 0)
                        throw new UnexpectedOutputException(element);

                    _pending -= 1;

                    Push(_outletOut, element);

                    if (_pullSuppressed)
                    {
                        _pullSuppressed = false;
                        Pull(_inletIn);
                    }
                }, onUpstreamFinish: () =>
                {
                    if (_pending != 0)
                        throw new OutputTruncationException();

                    Complete(_outletOut);
                });
            }

            private void SetOutletOutHandler()
            {
                SetHandler(_outletOut, onPull: () => Pull(_outletIn), onDownstreamFinish: () => Cancel(_outletIn));
            }
        }

        #endregion

        private readonly int _maxPending;
        private readonly Inlet<TIn> _inletIn = new Inlet<TIn>("inIn");
        private readonly Outlet<TIn> _inletOut = new Outlet<TIn>("inOut");
        private readonly Inlet<TOut> _outletIn = new Inlet<TOut>("outIn");
        private readonly Outlet<TOut> _outletOut = new Outlet<TOut>("outOut");
        private readonly BidiShape<TIn, TIn, TOut, TOut> _shape;
        private readonly Attributes _initialAttributes = Attributes.CreateName("One2OneBidi");

        public One2OneBidi(int maxPending)
        {
            _maxPending = maxPending;
            _shape = new BidiShape<TIn, TIn, TOut, TOut>(_inletIn, _inletOut, _outletIn, _outletOut);
        }

        public override BidiShape<TIn, TIn, TOut, TOut> Shape => _shape;

        protected override Attributes InitialAttributes => _initialAttributes;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new One2OneBidiGraphStateLogic<TIn, TOut>(Shape, _maxPending, _inletIn, _inletOut, _outletIn, _outletOut);
        
        public override string ToString() => "One2OneBidi";
    }
}

using System;
using Akka.Streams.Stage;
using Akka.Util;
using Directive = Akka.Streams.Supervision.Directive;

namespace Akka.Streams.Implementation.Fusing
{
    internal sealed class Filter<T> : SimpleLinearGraphStage<T>
    {
        #region Logic

        private sealed class Logic: InAndOutGraphStageLogic
        {
            private readonly Filter<T> _stage;

            private readonly Inlet<T> _in;
            private readonly Outlet<T> _out;
            private readonly Func<T, bool> _filter;

            private readonly Supervision.Decider _decider;

            private Option<T> _buffer = Option<T>.None;

            public Logic(Filter<T> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _in = stage.Inlet;
                _out = stage.Outlet;
                _filter = stage._filterAction;
                _decider = inheritedAttributes
                    .GetMandatoryAttribute<ActorAttributes.SupervisionStrategy>().Decider;
                SetHandler(_in, _out, this);
            }

            public override void PreStart()
            {
                Pull(_stage.Inlet);
            }

            public override void OnPush()
            {
                try
                {
                    var elem = Grab(_in);
                    if (_filter(elem))
                    {
                        if (IsAvailable(_out))
                        {
                            Push(_out, elem);
                            Pull(_in);
                        }
                        else
                        {
                            _buffer = elem;
                        }
                    }
                    else
                    {
                        Pull(_in);
                    }
                }
                catch (Exception e)
                {
                    if(_decider(e) == Directive.Stop)
                    {
                        FailStage(e);
                    }
                    else
                    {
                        Pull(_in);
                    }
                }
            }

            public override void OnPull()
            {
                if (_buffer.HasValue)
                {
                    Push(_out, _buffer.Value);
                    _buffer = Option<T>.None;
                    if (!IsClosed(_in))
                        Pull(_in);
                    else 
                        CompleteStage();
                }
                // else already pulled
            }

            public override void OnUpstreamFinish()
            {
                if(_buffer.IsEmpty)
                    base.OnUpstreamFinish();
                // else onPull will complete
            }
        }

        #endregion

        private readonly Func<T, bool> _filterAction;

        public Filter(Func<T, bool> filterAction)
        {
            _filterAction = filterAction;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);
    }
}

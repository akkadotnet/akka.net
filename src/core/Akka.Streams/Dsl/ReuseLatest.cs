﻿//-----------------------------------------------------------------------
// <copyright file="ReuseLatest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Reuses the latest element from upstream until it's replaced by a new value.
    ///
    /// This is designed to allow fan-in stages where output from one of the sources is intermittent / infrequent
    /// and users just want the previous value to be reused.
    /// </summary>
    /// <typeparam name="T">The output type.</typeparam>
    public sealed class ReuseLatest<T> : GraphStage<FlowShape<T, T>>
    {
        private readonly Inlet<T> _in = new("RepeatPrevious.in");
        private readonly Outlet<T> _out = new("RepeatPrevious.out");

        public override FlowShape<T, T> Shape => new(_in, _out);
        private readonly Action<T,T> _onItemChanged;

        /// <summary>
        /// Do nothing by default
        /// </summary>
        private static readonly Action<T,T> DefaultSwap = (_, _) => { };

        public ReuseLatest() : this(DefaultSwap)
        {
        }

        public ReuseLatest(Action<T, T> onItemChanged)
        {
            _onItemChanged = onItemChanged;
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) =>
            new Logic(this, _onItemChanged);

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly ReuseLatest<T> _stage;
            private Option<T> _last;
            private readonly Action<T,T> _onItemChanged;

            public Logic(ReuseLatest<T> stage, Action<T,T> onItemChanged) : base(stage.Shape)
            {
                _stage = stage;
                _onItemChanged = onItemChanged;

                SetHandler(_stage._in, this);
                SetHandler(_stage._out, this);
            }

            public override void OnPush()
            {
                var next = Grab(_stage._in);
                if (_last.HasValue)
                    _onItemChanged(_last.Value, next);
                _last = next;

                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, _last.Value);
                }
            }

            public override void OnPull()
            {
                if (_last.HasValue)
                {
                    if (!HasBeenPulled(_stage._in))
                    {
                        Pull(_stage._in);
                    }
                    
                    Push(_stage._out, _last.Value);
                }
                else
                {
                    Pull(_stage._in);
                }
            }
        }

        public override string ToString()
        {
            return "RepeatPrevious";
        }
    }
}

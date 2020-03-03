//-----------------------------------------------------------------------
// <copyright file="PartitionWith.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// This stage partitions input to 2 different outlets,
    /// applying different transformations on the elements,
    /// according to the partition function.
    /// </summary>
    /// <typeparam name="TIn">input type</typeparam>
    /// <typeparam name="TOut0">left output type</typeparam>
    /// <typeparam name="TOut1">right output type</typeparam>
    public class PartitionWith<TIn, TOut0, TOut1> : GraphStage<FanOutShape<TIn, TOut0, TOut1>>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            private Either<TOut0, TOut1> _pending;

            public Logic(PartitionWith<TIn, TOut0, TOut1> partitionWith) : base(partitionWith.Shape)
            {
                SetHandler(partitionWith.Shape.In, onPush: () =>
                {
                    var elem = Grab(partitionWith.Shape.In);
                    var either = partitionWith._partitionWith(elem);

                    if ((either is Left<TOut0, TOut1> left) && IsAvailable(partitionWith.Shape.Out0))
                    {
                        Push(partitionWith.Shape.Out0, left.Value);
                        if (IsAvailable(partitionWith.Shape.Out1))
                            Pull(partitionWith.Shape.In);
                    }
                    else if ((either is Right<TOut0, TOut1> right) && IsAvailable(partitionWith.Shape.Out1))
                    {
                        Push(partitionWith.Shape.Out1, right.Value);
                        if (IsAvailable(partitionWith.Shape.Out0))
                            Pull(partitionWith.Shape.In);
                    }
                    else
                        _pending = either;

                }, onUpstreamFinish: () =>
                {
                    if (_pending == null)
                        CompleteStage();
                });

                SetHandler(partitionWith.Shape.Out0, onPull: () =>
                {
                    if (_pending is Left<TOut0, TOut1> left)
                    {
                        Push(partitionWith.Shape.Out0, left.Value);
                        if (IsClosed(partitionWith.Shape.In))
                            CompleteStage();
                        else
                        {
                            _pending = null;
                            if (IsAvailable(partitionWith.Shape.Out1))
                                Pull(partitionWith.Shape.In);
                        }
                    }
                    else if (!HasBeenPulled(partitionWith.Shape.In))
                        Pull(partitionWith.Shape.In);
                });

                SetHandler(partitionWith.Shape.Out1, onPull: () =>
                {
                    if (_pending is Right<TOut0, TOut1> right)
                    {
                        Push(partitionWith.Shape.Out1, right.Value);
                        if (IsClosed(partitionWith.Shape.In))
                            CompleteStage();
                        else
                        {
                            _pending = null;
                            if (IsAvailable(partitionWith.Shape.Out0))
                                Pull(partitionWith.Shape.In);
                        }
                    }
                    else if (!HasBeenPulled(partitionWith.Shape.In))
                        Pull(partitionWith.Shape.In);
                });
            }
        }

        #endregion

        private readonly Func<TIn, Either<TOut0, TOut1>> _partitionWith;

        /// <param name="partitionWith">partition function</param>
        public PartitionWith(Func<TIn, Either<TOut0, TOut1>> partitionWith)
        {
            _partitionWith = partitionWith;

            Shape = new FanOutShape<TIn, TOut0, TOut1>("partitionWith");
        }

        public override FanOutShape<TIn, TOut0, TOut1> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}

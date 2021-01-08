//-----------------------------------------------------------------------
// <copyright file="LastElement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// This stage materializes to the last element pushed before upstream completion, if any, thereby recovering from any
    /// failure. Pushed elements are just passed along.
    /// </summary>
    /// <typeparam name="T">input and output type</typeparam>
    public class LastElement<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, Task<Option<T>>>
    {
        #region Logic

        private sealed class Logic : GraphStageLogic
        {
            public Logic(LastElement<T> lastElement, TaskCompletionSource<Option<T>> completion) : base(lastElement.Shape)
            {
                var currentElement = Option<T>.None;

                SetHandler(lastElement.In, onPush: () =>
                {
                    var element = Grab(lastElement.In);
                    currentElement = new Option<T>(element);
                    Push(lastElement.Out, element);
                }, onUpstreamFinish: () =>
                {
                    completion.SetResult(currentElement);
                    CompleteStage();
                }, onUpstreamFailure: ex =>
                {
                    completion.SetResult(currentElement);
                    CompleteStage();
                });

                SetHandler(lastElement.Out, onPull: () =>
                {
                    Pull(lastElement.In);
                });
            }
        }

        #endregion

        public LastElement()
        {
            In = new Inlet<T>("lastElement.in");
            Out = new Outlet<T>("lastElement.out");
            Shape = new FlowShape<T, T>(In, Out);
        }

        public override ILogicAndMaterializedValue<Task<Option<T>>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<Option<T>>();
            var logic = new Logic(this, completion);

            return new LogicAndMaterializedValue<Task<Option<T>>>(logic, completion.Task);
        }

        public override FlowShape<T, T> Shape { get; }

        public Inlet<T> In { get; }
        public Outlet<T> Out { get; }
    }
}

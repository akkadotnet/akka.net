//-----------------------------------------------------------------------
// <copyright file="Valve.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Mode for <see cref="IValveSwitch"/>
    /// </summary>
    public enum SwitchMode
    {
        Open,
        Close
    }

    /// <summary>
    /// Pause/resume a Flow
    /// </summary>
    public interface IValveSwitch
    {
        /// <summary>
        /// Change the state of the valve
        /// </summary>
        /// <param name="mode">expected mode to switch on</param>
        /// <returns>A task that completes with true if the mode did change and false if it already was in the requested mode</returns>
        Task<bool> Flip(SwitchMode mode);

        /// <summary>
        /// Obtain the state of the valve
        /// </summary>
        /// <returns>A task that completes with <see cref="SwitchMode"/> to indicate the current state of the valve</returns>
        Task<SwitchMode> GetMode();
    }

    class ValveSwitch : IValveSwitch
    {
        private readonly Action<(SwitchMode, TaskCompletionSource<bool>)> _flipCallback;
        private readonly Action<TaskCompletionSource<SwitchMode>> _getModeCallback;

        public ValveSwitch(Action<(SwitchMode, TaskCompletionSource<bool>)> flipCallback, Action<TaskCompletionSource<SwitchMode>> getModeCallback)
        {
            _flipCallback = flipCallback;
            _getModeCallback = getModeCallback;
        }

        public Task<bool> Flip(SwitchMode flipToMode)
        {
            var completion = new TaskCompletionSource<bool>();
            _flipCallback((flipToMode, completion));
            return completion.Task;
        }

        public Task<SwitchMode> GetMode()
        {
            var completion = new TaskCompletionSource<SwitchMode>();
            _getModeCallback(completion);
            return completion.Task;
        }
    }

    /// <summary>
    /// Materializes into a task of <see cref="IValveSwitch"/> which provides a the method flip that stops or restarts the flow of elements passing through the stage. 
    /// As long as the valve is closed it will backpressure.
    /// Note that closing the valve could result in one element being buffered inside the stage, and if the stream completes or fails while being closed, that element may be lost.
    /// </summary>
    /// <typeparam name="T">type of element</typeparam>
    public class Valve<T> : GraphStageWithMaterializedValue<FlowShape<T, T>, Task<IValveSwitch>>
    {
        #region Logic

        private sealed class ValveGraphStageLogic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly Valve<T> _valve;
            private readonly TaskCompletionSource<IValveSwitch> _completion;
            private SwitchMode _mode;
            private readonly IValveSwitch _switch;

            private bool IsOpen => _mode == SwitchMode.Open;

            public ValveGraphStageLogic(Valve<T> valve, TaskCompletionSource<IValveSwitch> completion) : base(valve.Shape)
            {
                _valve = valve;
                _completion = completion;
                _mode = valve._mode;

                var flipCallback = GetAsyncCallback<(SwitchMode, TaskCompletionSource<bool>)>(FlipHandler);
                var getModeCallback = GetAsyncCallback<TaskCompletionSource<SwitchMode>>(t => t.SetResult(_mode));

                _switch = new ValveSwitch(flipCallback, getModeCallback);

                SetHandler(_valve.In, this);
                SetHandler(_valve.Out, this);
            }

            public override void PreStart()
            {
                _completion.SetResult(_switch);
            }

            void FlipHandler((SwitchMode, TaskCompletionSource<bool>) t)
            {
                var flipToMode = t.Item1;
                var completion = t.Item2;

                var succeed = false;

                if (flipToMode != _mode)
                {
                    if (_mode == SwitchMode.Close)
                    {
                        if (IsAvailable(_valve.In))
                            Push(_valve.Out, Grab(_valve.In));
                        else if (IsAvailable(_valve.Out) && !HasBeenPulled(_valve.In))
                            Pull(_valve.In);

                        _mode = SwitchMode.Open;
                    }
                    else
                        _mode = SwitchMode.Close;

                    succeed = true;
                }

                completion.SetResult(succeed);
            }

            public void OnPush()
            {
                if (IsOpen)
                    Push(_valve.Out, Grab(_valve.In));
            }

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnPull()
            {
                if (IsOpen)
                    Pull(_valve.In);
            }

            public void OnDownstreamFinish() => CompleteStage();
        }

        #endregion

        private readonly SwitchMode _mode;

        /// <summary>
        /// Creates already open <see cref="Valve{T}"/>
        /// </summary>
        public Valve() : this(SwitchMode.Open)
        {
        }

        /// <summary>
        /// Creates <see cref="Valve{T}"/> with inital <paramref name="mode"/>
        /// </summary>
        /// <param name="mode">state of the valve at the startup of the flow</param>
        public Valve(SwitchMode mode)
        {
            _mode = mode;

            In = new Inlet<T>("valve.in");
            Out = new Outlet<T>("valve.out");
            Shape = new FlowShape<T, T>(In, Out);
        }

        public override ILogicAndMaterializedValue<Task<IValveSwitch>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<IValveSwitch>();
            var logic = new ValveGraphStageLogic(this, completion);

            return new LogicAndMaterializedValue<Task<IValveSwitch>>(logic, completion.Task);
        }

        public override FlowShape<T, T> Shape { get; }

        public Inlet<T> In { get; }
        public Outlet<T> Out { get; }
    }
}

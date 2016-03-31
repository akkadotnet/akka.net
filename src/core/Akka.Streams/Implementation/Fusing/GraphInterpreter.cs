using System;
using System.Linq;
using System.Threading;
using Akka.Event;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation.Fusing
{
    /**
     * INTERNAL API
     *
     * From an external viewpoint, the GraphInterpreter takes an assembly of graph processing stages encoded as a
     * [[GraphInterpreter#GraphAssembly]] object and provides facilities to execute and interact with this assembly.
     * The lifecycle of the Interpreter is roughly the following:
     *  - Boundary logics are attached via [[attachDownstreamBoundary()]] and [[attachUpstreamBoundary()]]
     *  - [[init()]] is called
     *  - [[execute()]] is called whenever there is need for execution, providing an upper limit on the processed events
     *  - [[finish()]] is called before the interpreter is disposed, preferably after [[isCompleted]] returned true, although
     *    in abort cases this is not strictly necessary
     *
     * The [[execute()]] method of the interpreter accepts an upper bound on the events it will process. After this limit
     * is reached or there are no more pending events to be processed, the call returns. It is possible to inspect
     * if there are unprocessed events left via the [[isSuspended]] method. [[isCompleted]] returns true once all stages
     * reported completion inside the interpreter.
     *
     * The internal architecture of the interpreter is based on the usage of arrays and optimized for reducing allocations
     * on the hot paths.
     *
     * One of the basic abstractions inside the interpreter is the notion of *connection*. In the abstract sense a
     * connection represents an output-input port pair (an analogue for a connected RS Publisher-Subscriber pair),
     * while in the practical sense a connection is a number which represents slots in certain arrays.
     * In particular
     *  - portStates contains a bitfield that tracks the states of the ports (output-input) corresponding to this
     *    connection. This bitfield is used to decode the event that is in-flight.
     *  - connectionSlots is a mapping from a connection id to a potential element or exception that accompanies the
     *    event encoded in the portStates bitfield
     *  - inHandlers is a mapping from a connection id to the [[InHandler]] instance that handles the events corresponding
     *    to the input port of the connection
     *  - outHandlers is a mapping from a connection id to the [[OutHandler]] instance that handles the events corresponding
     *    to the output port of the connection
     *
     * On top of these lookup tables there is an eventQueue, represented as a circular buffer of integers. The integers
     * it contains represents connections that have pending events to be processed. The pending event itself is encoded
     * in the portStates bitfield. This implies that there can be only one event in flight for a given connection, which
     * is true in almost all cases, except a complete-after-push or fail-after-push.
     *
     * The layout of the portStates bitfield is the following:
     *
     *             |- state machn.-| Only one bit is hot among these bits
     *  64  32  16 | 8   4   2   1 |
     * +---+---+---|---+---+---+---|
     *   |   |   |   |   |   |   |
     *   |   |   |   |   |   |   |  From the following flags only one is active in any given time. These bits encode
     *   |   |   |   |   |   |   |  state machine states, and they are "moved" around using XOR masks to keep other bits
     *   |   |   |   |   |   |   |  intact.
     *   |   |   |   |   |   |   |
     *   |   |   |   |   |   |   +- InReady:  The input port is ready to be pulled
     *   |   |   |   |   |   +----- Pulling:  A pull is active, but have not arrived yet (queued)
     *   |   |   |   |   +--------- Pushing:  A push is active, but have not arrived yet (queued)
     *   |   |   |   +------------- OutReady: The output port is ready to be pushed
     *   |   |   |
     *   |   |   +----------------- InClosed:  The input port is closed and will not receive any events.
     *   |   |                                 A push might be still in flight which will be then processed first.
     *   |   +--------------------- OutClosed: The output port is closed and will not receive any events.
     *   +------------------------- InFailed:  Always set in conjunction with InClosed. Indicates that the close event
     *                                         is a failure
     *
     * Sending an event is usually the following sequence:
     *  - An action is requested by a stage logic (push, pull, complete, etc.)
     *  - the state machine in portStates is transitioned from a ready state to a pending event
     *  - the id of the affected connection is enqueued
     *
     * Receiving an event is usually the following sequence:
     *  - id of connection to be processed is dequeued
     *  - the type of the event is determined from the bits set on portStates
     *  - the state machine in portStates is transitioned to a ready state
     *  - using the inHandlers/outHandlers table the corresponding callback is called on the stage logic.
     *
     * Because of the FIFO construction of the queue the interpreter is fair, i.e. a pending event is always executed
     * after a bounded number of other events. This property, together with suspendability means that even infinite cycles can
     * be modeled, or even dissolved (if preempted and a "stealing" external event is injected; for example the non-cycle
     * edge of a balance is pulled, dissolving the original cycle).
     */

    public sealed class GraphInterpreter
    {
        #region internal classes
        /// <summary>
        /// Marker object that indicates that a port holds no element since it was already grabbed. 
        /// The port is still pullable, but there is no more element to grab.
        /// </summary>
        public sealed class Empty
        {
            public static readonly Empty Instance = new Empty();
            private Empty() { }

            public override string ToString()
            {
                return "Empty";
            }
        }


        public sealed class Failed
        {
            public readonly Exception Reason;
            public readonly object PreviousElement;

            public Failed(Exception reason, object previousElement)
            {
                Reason = reason;
                PreviousElement = previousElement;
            }
        }

        public abstract class UpstreamBoundaryStageLogic : GraphStageLogic
        {
            public abstract Outlet Out { get; }
            protected UpstreamBoundaryStageLogic() : base(inCount: 0, outCount: 1) { }
        }

        public abstract class DownstreamBoundaryStageLogic : GraphStageLogic
        {
            public abstract Inlet In { get; }
            protected DownstreamBoundaryStageLogic() : base(inCount: 1, outCount: 0) { }
        }

        #endregion

#if !DEBUG
        public const bool IsDebug = false;
#else
        public const bool IsDebug = true;
#endif

        public const int NoEvent = -1;
        public const int Boundary = -1;

        public const int InReady = 1;
        public const int Pulling = 1 << 1;
        public const int Pushing = 1 << 2;
        public const int OutReady = 1 << 3;

        public const int InClosed = 1 << 4;
        public const int OutClosed = 1 << 5;
        public const int InFailed = 1 << 6;

        public const int PullStartFlip = InReady | Pulling;
        public const int PullEndFlip = Pulling | OutReady;
        public const int PushStartFlip = Pushing | OutReady;
        public const int PushEndFlip = InReady | Pushing;

        public const int KeepGoingFlag = 0x4000000;
        public const int KeepGoingMask = 0x3ffffff;

        private static readonly ThreadLocal<GraphInterpreter> _currentInterpreter = new ThreadLocal<GraphInterpreter>(() => null);

        public static GraphInterpreter Current
        {
            get
            {
                if (_currentInterpreter.Value == null)
                    throw new ApplicationException("Something went terribly wrong!");
                return _currentInterpreter.Value;
            }
        }

        public static GraphInterpreter CurrentInterpreterOrNull => _currentInterpreter.Value;

        public static readonly Attributes[] SingleNoAttribute = { Attributes.None };

        public readonly GraphStageLogic[] Logics;
        public readonly GraphAssembly Assembly;
        public readonly IMaterializer Materializer;
        public readonly ILoggingAdapter Log;
        public readonly IInHandler[] InHandlers;
        public readonly IOutHandler[] OutHandlers;
        public readonly Action<GraphStageLogic, object, Action<object>> OnAsyncInput;
        public readonly bool FuzzingMode;

        // Maintains additional information for events, basically elements in-flight, or failure.
        // Other events are encoded in the portStates bitfield.
        public readonly object[] ConnectionSlots;

        // Bitfield encoding pending events and various states for efficient querying and updates. See the documentation
        // of the class for a full description.
        public readonly int[] PortStates;

        // The number of currently running stages. Once this counter reaches zero, the interpreter is considered to be completed.
        public int RunningStagesCount;

        //Counts how many active connections a stage has. Once it reaches zero, the stage is automatically stopped.
        private readonly int[] _shutdownCounter;

        // An event queue implemented as a circular buffer
        private readonly int[] _eventQueue;
        private readonly int _mask;
        private int _queueHead = 0;
        private int _queueTail = 0;

        public GraphInterpreter(
            GraphAssembly assembly,
            IMaterializer materializer,
            ILoggingAdapter log,
            InHandler[] inHandlers,
            OutHandler[] outHandlers,
            GraphStageLogic[] logics,
            Action<GraphStageLogic, object, Action<object>> onAsyncInput,
            bool fuzzingMode)
        {
            Logics = logics;
            Assembly = assembly;
            Materializer = materializer;
            Log = log;
            InHandlers = inHandlers;
            OutHandlers = outHandlers;
            OnAsyncInput = onAsyncInput;
            FuzzingMode = fuzzingMode;

            ConnectionSlots = new object[assembly.ConnectionCount];
            for (int i = 0; i < ConnectionSlots.Length; i++) ConnectionSlots[i] = Empty.Instance;

            PortStates = new int[assembly.ConnectionCount];
            for (int i = 0; i < PortStates.Length; i++) PortStates[i] = InReady;

            RunningStagesCount = Assembly.Stages.Length;

            _shutdownCounter = new int[assembly.Stages.Length];
            for (int i = 0; i < _shutdownCounter.Length; i++)
            {
                var shape = assembly.Stages[i].Shape;
                _shutdownCounter[i] = shape.Inlets.Count() + shape.Outlets.Count();
            }

            _eventQueue = new int[1 << (32 - Int32Extensions.NumberOfLeadingZeros(assembly.ConnectionCount - 1))];
            _mask = _eventQueue.Length - 1;
        }

        internal GraphStageLogic ActiveStage { get; private set; }
        internal IMaterializer SubFusingMaterializer { get; private set; }

        private string QueueStatus()
        {
            var contents = Enumerable.Range(_queueHead, _queueTail - _queueHead).Select(i =>
            {
                var conn = _eventQueue[i & _mask];
                return Tuple.Create(conn, PortStates[conn], ConnectionSlots[conn]);
            });
            return $"({_eventQueue.Length}, {_queueHead}, {_queueTail})({string.Join(", ", contents)})";
        }

        private string _name;
        internal string Name => _name ?? (_name = GetHashCode().ToString("x"));

        /// <summary>
        /// Assign the boundary logic to a given connection. This will serve as the interface to the external world
        /// (outside the interpreter) to process and inject events.
        /// </summary>
        public void AttachUpstreamBoundary(int connection, UpstreamBoundaryStageLogic logic)
        {
            logic.PortToConn[logic.Out.Id + logic.InCount] = connection;
            logic.Interpreter = this;
            OutHandlers[connection] = (OutHandler)logic.Handlers[0];
        }

        /// <summary>
        /// Assign the boundary logic to a given connection. This will serve as the interface to the external world
        /// (outside the interpreter) to process and inject events.
        /// </summary>
        public void AttachDownstreamBoundary(int connection, DownstreamBoundaryStageLogic logic)
        {
            logic.PortToConn[logic.In.Id] = connection;
            logic.Interpreter = this;
            InHandlers[connection] = (InHandler)logic.Handlers[0];
        }

        /// <summary>
        /// Dynamic handler changes are communicated from a GraphStageLogic by this method.
        /// </summary>
        public void SetHandler(int connection, IInHandler handler)
        {
            if (IsDebug) Console.WriteLine($"{Name} SETHANDLER {OutOwnerName(connection)} (in) {handler}");
            InHandlers[connection] = handler;
        }

        /// <summary>
        /// Dynamic handler changes are communicated from a GraphStageLogic by this method.
        /// </summary>
        public void SetHandler(int connection, IOutHandler handler)
        {
            if (IsDebug) Console.WriteLine($"{Name} SETHANDLER {OutOwnerName(connection)} (out) {handler}");
            OutHandlers[connection] = handler;
        }

        /// <summary>
        /// Returns true if there are pending unprocessed events in the event queue.
        /// </summary>
        public bool IsSuspended { get { return _queueHead != _queueTail; } }

        /// <summary>
        /// Returns true if there are no more running stages and pending events.
        /// </summary>
        public bool IsCompleted { get { return RunningStagesCount == 0 && !IsSuspended; } }

        /// <summary>
        /// Initializes the states of all the stage logics by calling <see cref="GraphStageLogic.PreStart"/>.
        /// The passed-in materializer is intended to be a <see cref="SubFusingMaterializer"/>
        /// that avoids creating new Actors when stages materialize sub-flows.If no
        /// such materializer is available, passing in null will reuse the normal
        /// materializer for the GraphInterpreter—fusing is only an optimization.
        /// </summary>
        public void Init(IMaterializer subMaterializer)
        {
            SubFusingMaterializer = subMaterializer ?? Materializer;
            for (int i = 0; i < Logics.Length; i++)
            {
                var logic = Logics[i];
                logic.StageId = i;
                logic.Interpreter = this;
                try
                {
                    logic.BeforePreStart();
                    logic.PreStart();
                }
                catch (Exception e)
                {
                    if (Log.IsErrorEnabled)
                        Log.Error(e, $"Error during PreStart in [{Assembly.Stages[logic.StageId]}]");
                    logic.FailStage(e);
                }
                AfterStageHasRun(logic);
            }
        }

        /// <summary>
        /// Finalizes the state of all stages by calling <see cref="GraphStageLogic.PostStop"/> (if necessary).
        /// </summary>
        public void Finish()
        {
            foreach (var logic in Logics)
            {
                if (!IsStageCompleted(logic)) FinalizeStage(logic);
            }
        }

        // Debug name for a connections input part
        private string InOwnerName(int connection)
        {
            var owner = Assembly.InletOwners[connection];
            return owner == Boundary ? "DownstreamBoundary" : Assembly.Stages[owner].ToString();
        }

        // Debug name for a connections output part
        private string OutOwnerName(int connection)
        {
            var owner = Assembly.OutletOwners[connection];
            return owner == Boundary ? "UpstreamBoundary" : Assembly.Stages[owner].ToString();
        }

        // Debug name for a connections input part
        private string InLogicName(int connection)
        {
            var owner = Assembly.InletOwners[connection];
            return owner == Boundary ? "DownstreamBoundary" : Logics[owner].ToString();
        }

        // Debug name for a connections output part
        private string OutLogicName(int connection)
        {
            var owner = Assembly.OutletOwners[connection];
            return owner == Boundary ? "UpstreamBoundary" : Logics[owner].ToString();
        }

        private string ShutdownCounters()
        {
            return string.Join(",",
                _shutdownCounter.Select(x => x >= KeepGoingFlag ? $"{x & KeepGoingMask}(KeepGoing)" : x.ToString()));
        }

        /// <summary>
        /// Executes pending events until the given limit is met. If there were remaining events, <see cref="IsSuspended"/> will return true.
        /// </summary>
        public void Execute(int eventLimit)
        {
            if (IsDebug) Console.WriteLine($"{Name} ---------------- EXECUTE {QueueStatus()} (running={RunningStagesCount}, shutdown={ShutdownCounters()})");
            var previousInterpreter = _currentInterpreter.Value;
            _currentInterpreter.Value = this;
            try
            {
                var eventsRemaining = eventLimit;
                while (eventsRemaining > 0 && _queueTail != _queueHead)
                {
                    var connection = Dequeue();
                    try
                    {
                        ProcessEvent(connection);
                    }
                    catch (Exception e)
                    {
                        if (ActiveStage == null) throw e;
                        else
                        {
                            var stage = Assembly.Stages[ActiveStage.StageId];
                            if (Log.IsErrorEnabled)
                                Log.Error(e, $"Error in stage [{stage}]: {e.Message}");

                            ActiveStage.FailStage(e);
                        }
                    }
                    AfterStageHasRun(ActiveStage);
                    eventsRemaining--;
                }
            }
            finally
            {
                _currentInterpreter.Value = previousInterpreter;
            }
            if (IsDebug) Console.WriteLine($"{Name} ---------------- {QueueStatus()} (running={RunningStagesCount}, shutdown={ShutdownCounters()})");
            // TODO: deadlock detection
        }

        public void RunAsyncInput(GraphStageLogic logic, object evt, Action<object> handler)
        {
            if (!IsStageCompleted(logic))
            {
                if (IsDebug) Console.WriteLine($"{Name} ASYNC {evt} ({handler}) [{logic}]");
                var previousInterpreter = _currentInterpreter.Value;
                _currentInterpreter.Value = this;
                try
                {
                    ActiveStage = logic;
                    try
                    {
                        handler(evt);
                    }
                    catch (Exception e)
                    {
                        logic.FailStage(e);
                    }
                    AfterStageHasRun(logic);
                }
                finally
                {
                    _currentInterpreter.Value = previousInterpreter;
                }
            }
        }

        /// <summary>
        /// Decodes and processes a single event for the given connection
        /// </summary>
        private void ProcessEvent(int connection)
        {
            // this must be the state after returning without delivering any signals, to avoid double-finalization of some unlucky stage
            // (this can happen if a stage completes voluntarily while connection close events are still queued)
            ActiveStage = null;
            var code = PortStates[connection];

            // Manual fast decoding, fast paths are PUSH and PULL
            if ((code & (Pushing | InClosed | OutClosed)) == Pushing)
            {
                // PUSH
                ProcessElement(connection);
            }
            else if ((code & (Pulling | OutClosed | InClosed)) == Pulling)
            {
                // PULL
                if (IsDebug) Console.WriteLine($"{Name} PULL {InOwnerName(connection)} -> {OutOwnerName(connection)} ({OutHandlers[connection]}) [{OutLogicName(connection)}]");
                PortStates[connection] ^= PullEndFlip;
                ActiveStage = SafeLogics(Assembly.OutletOwners[connection]);
                OutHandlers[connection].OnPull();
            }
            else if ((code & (OutClosed | InClosed)) == InClosed)
            {
                // CANCEL
                var stageId = Assembly.OutletOwners[connection];
                ActiveStage = SafeLogics(stageId);
                if (IsDebug) Console.WriteLine($"{Name} CANCEL {InOwnerName(connection)} -> {OutOwnerName(connection)} ({OutHandlers[connection]}) [{OutLogicName(connection)}]");
                PortStates[connection] |= OutClosed;
                CompleteConnection(stageId);
                OutHandlers[connection].OnDownstreamFinish();
            }
            else if ((code & (OutClosed | InClosed)) == OutClosed)
            {
                // COMPLETIONS
                if ((code & Pushing) == 0)
                {
                    // Normal completion (no push pending)
                    if (IsDebug) Console.WriteLine($"{Name} COMPLETE {OutOwnerName(connection)} -> {InOwnerName(connection)} ({InHandlers[connection]}) [{InLogicName(connection)}]");
                    PortStates[connection] |= InClosed;
                    var stageId = Assembly.InletOwners[connection];
                    ActiveStage = SafeLogics(stageId);
                    CompleteConnection(stageId);

                    if ((PortStates[connection] & InFailed) == 0)
                        InHandlers[connection].OnUpstreamFinish();
                    else
                        InHandlers[connection].OnUpstreamFailure(((Failed)ConnectionSlots[connection]).Reason);
                }
                else
                {
                    // Push is pending, first process push, then re-enqueue closing event
                    ProcessElement(connection);
                    Enqueue(connection);
                }
            }
        }

        private GraphStageLogic SafeLogics(int id)
        {
            return id == Boundary ? null : Logics[id];
        }

        public void ProcessElement(int connection)
        {
            if (IsDebug) Console.WriteLine($"{Name} PUSH {OutOwnerName(connection)} -> {InOwnerName(connection)},  {ConnectionSlots[connection]} ({InHandlers[connection]}) [{InLogicName(connection)}]");
            ActiveStage = SafeLogics(Assembly.InletOwners[connection]);
            PortStates[connection] ^= PushEndFlip;
            InHandlers[connection].OnPush();
        }

        private int Dequeue()
        {
            var idx = _queueHead & _mask;
            if (FuzzingMode)
            {
                var swapWith = (ThreadLocalRandom.Current.Next(_queueTail - _queueHead) + _queueHead) & _mask;
                var ev = _eventQueue[swapWith];
                _eventQueue[swapWith] = _eventQueue[idx];
                _eventQueue[idx] = ev;
            }
            var element = _eventQueue[idx];
            _eventQueue[idx] = NoEvent;
            _queueHead++;
            return element;
        }

        private void Enqueue(int connection)
        {
            if (IsDebug && _queueTail - _queueHead > _mask) throw new Exception($"{Name} internal queue full ({QueueStatus()}) + {connection}");
            _eventQueue[_queueTail & _mask] = connection;
            _queueTail++;
        }

        internal void AfterStageHasRun(GraphStageLogic logic)
        {
            if (IsStageCompleted(logic))
            {
                RunningStagesCount--;
                FinalizeStage(logic);
            }
        }

        /// <summary>
        /// Returns true if the given stage is alredy completed
        /// </summary>
        internal bool IsStageCompleted(GraphStageLogic stage)
        {
            return stage != null && _shutdownCounter[stage.StageId] == 0;
        }

        /// <summary>
        ///  Register that a connection in which the given stage participated has been completed and therefore the stage itself might stop, too.
        /// </summary>
        private void CompleteConnection(int stageId)
        {
            if (stageId != Boundary)
            {
                var activeConnections = _shutdownCounter[stageId];
                if (activeConnections > 0) _shutdownCounter[stageId] = activeConnections - 1;
            }
        }

        internal void SetKeepGoing(GraphStageLogic logic, bool enabled)
        {
            if (enabled) _shutdownCounter[logic.StageId] |= KeepGoingFlag;
            else _shutdownCounter[logic.StageId] &= KeepGoingMask;
        }

        private void FinalizeStage(GraphStageLogic logic)
        {
            try
            {
                logic.PostStop();
                logic.AfterPostStop();
            }
            catch (Exception err)
            {
                if (Log.IsErrorEnabled)
                    Log.Error(err, "Error during PostStop in [{0}]", Assembly.Stages[logic.StageId]);
            }
        }

        internal void Push(int connection, object element)
        {
            var currentState = PortStates[connection];
            PortStates[connection] = currentState ^ PushStartFlip;
            if ((currentState & InClosed) == 0)
            {
                ConnectionSlots[connection] = element;
                Enqueue(connection);
            }
        }

        internal void Pull(int connection)
        {
            var currentState = PortStates[connection];
            PortStates[connection] = currentState ^ PullStartFlip;
            if ((currentState & OutClosed) == 0)
            {
                Enqueue(connection);
            }
        }

        internal void Complete(int connection)
        {
            var currentState = PortStates[connection];
            if (IsDebug) Console.WriteLine($"{Name}   Complete({connection}) [{currentState}]");
            PortStates[connection] = currentState | OutClosed;
            if ((currentState & (InClosed | Pushing | Pulling | OutClosed)) == 0) Enqueue(connection);
            if ((currentState & OutClosed) == 0) CompleteConnection(Assembly.OutletOwners[connection]);
        }

        internal void Fail(int connection, Exception reason)
        {
            var currentState = PortStates[connection];
            if (IsDebug) Console.WriteLine($"{Name}   Fail({connection}, {reason}) [{currentState}]");
            PortStates[connection] = currentState | OutClosed;
            if ((currentState & (InClosed | OutClosed)) == 0)
            {
                PortStates[connection] = currentState | (OutClosed | InFailed);
                ConnectionSlots[connection] = new Failed(reason, ConnectionSlots[connection]);
                if ((currentState & (Pulling | Pushing)) == 0) Enqueue(connection);
            }

            if ((currentState & OutClosed) == 0) CompleteConnection(Assembly.OutletOwners[connection]);
        }

        internal void Cancel(int connection)
        {
            var currentState = PortStates[connection];
            if (IsDebug) Console.WriteLine($"{Name}   Cancel({connection}) [{currentState}]");
            PortStates[connection] = currentState | InClosed;
            if ((currentState & OutClosed) == 0)
            {
                ConnectionSlots[connection] = Empty.Instance;
                if ((currentState & (Pulling | Pushing | InClosed)) == 0) Enqueue(connection);
            }

            if ((currentState & InClosed) == 0) CompleteConnection(Assembly.InletOwners[connection]);
        }

        /// <summary>
        /// Debug utility to dump the "waits-on" relationships in DOT format to the console for analysis of deadlocks.
        /// 
        /// Only invoke this after the interpreter completely settled, otherwise the results might be off. This is a very
        /// simplistic tool, make sure you are understanding what you are doing and then it will serve you well.
        /// </summary>
        public void DumpWaits()
        {
            Console.WriteLine("digraph waits {");
            for (var i = 0; i < Assembly.Stages.Length; i++)
                Console.WriteLine($@"N{i} [label=""{Assembly.Stages[i]}""]");
        }
    }
}
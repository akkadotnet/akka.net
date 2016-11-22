//-----------------------------------------------------------------------
// <copyright file="GraphInterpreter.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// INTERNAL API
    ///
    /// From an external viewpoint, the GraphInterpreter takes an assembly of graph processing stages encoded as a
    /// <see cref="Assembly"/> object and provides facilities to execute and interact with this assembly.
    /// <para/> The lifecycle of the Interpreter is roughly the following:
    /// <para/> - Boundary logics are attached via <see cref="AttachDownstreamBoundary(Connection,DownstreamBoundaryStageLogic)"/> and <see cref="AttachUpstreamBoundary(Connection,UpstreamBoundaryStageLogic)"/>
    /// <para/> - <see cref="Init"/> is called
    /// <para/> - <see cref="Execute"/> is called whenever there is need for execution, providing an upper limit on the processed events
    /// <para/> - <see cref="Finish"/> is called before the interpreter is disposed, preferably after <see cref="IsCompleted"/> returned true, although
    ///    in abort cases this is not strictly necessary
    ///
    /// The <see cref="Execute"/> method of the interpreter accepts an upper bound on the events it will process. After this limit
    /// is reached or there are no more pending events to be processed, the call returns. It is possible to inspect
    /// if there are unprocessed events left via the <see cref="IsSuspended"/> method. <see cref="IsCompleted"/> returns true once all stages
    /// reported completion inside the interpreter.
    ///
    /// The internal architecture of the interpreter is based on the usage of arrays and optimized for reducing allocations
    /// on the hot paths.
    ///
    /// One of the basic abstractions inside the interpreter is the <see cref="Connection"/>. A connection represents an output-input port pair
    /// (an analogue for a connected RS Publisher-Subscriber pair). The Connection object contains all the necessary data for the interpreter 
    /// to pass elements, demand, completion or errors across the Connection.
    /// <para/> In particular
    /// <para/> - portStates contains a bitfield that tracks the states of the ports (output-input) corresponding to this
    ///    connection. This bitfield is used to decode the event that is in-flight.
    /// <para/> - connectionSlot contains a potential element or exception that accompanies the
    ///    event encoded in the portStates bitfield
    /// <para/> - inHandler contains the <see cref="InHandler"/> instance that handles the events corresponding
    ///    to the input port of the connection
    /// <para/> - outHandler contains the <see cref="OutHandler"/> instance that handles the events corresponding
    ///    to the output port of the connection
    ///
    /// On top of the Connection table there is an eventQueue, represented as a circular buffer of Connections. The queue
    /// contains the Connections that have pending events to be processed. The pending event itself is encoded
    /// in the portState bitfield of the Connection. This implies that there can be only one event in flight for a given
    /// Connection, which is true in almost all cases, except a complete-after-push or fail-after-push which has to
    /// be decoded accordingly.
    ///
    /// The layout of the portState  bitfield is the following:
    ///
    ///             |- state machn.-| Only one bit is hot among these bits
    ///  64  32  16 | 8   4   2   1 |
    /// +---+---+---|---+---+---+---|
    ///   |   |   |   |   |   |   |
    ///   |   |   |   |   |   |   |  From the following flags only one is active in any given time. These bits encode
    ///   |   |   |   |   |   |   |  state machine states, and they are "moved" around using XOR masks to keep other bits
    ///   |   |   |   |   |   |   |  intact.
    ///   |   |   |   |   |   |   |
    ///   |   |   |   |   |   |   +- InReady:  The input port is ready to be pulled
    ///   |   |   |   |   |   +----- Pulling:  A pull is active, but have not arrived yet (queued)
    ///   |   |   |   |   +--------- Pushing:  A push is active, but have not arrived yet (queued)
    ///   |   |   |   +------------- OutReady: The output port is ready to be pushed
    ///   |   |   |
    ///   |   |   +----------------- InClosed:  The input port is closed and will not receive any events.
    ///   |   |                                 A push might be still in flight which will be then processed first.
    ///   |   +--------------------- OutClosed: The output port is closed and will not receive any events.
    ///   +------------------------- InFailed:  Always set in conjunction with InClosed. Indicates that the close event
    ///                                         is a failure
    ///
    /// Sending an event is usually the following sequence:
    ///  - An action is requested by a stage logic (push, pull, complete, etc.)
    ///  - the state machine in portStates is transitioned from a ready state to a pending event
    ///  - the affected Connection is enqueued
    ///
    /// Receiving an event is usually the following sequence:
    ///  - the connection to be processed is dequeued
    ///  - the type of the event is determined from the bits set on portStates
    ///  - the state machine in portStates is transitioned to a ready state
    ///  - using the inHandlers/outHandlers table the corresponding callback is called on the stage logic.
    ///
    /// Because of the FIFO construction of the queue the interpreter is fair, i.e. a pending event is always executed
    /// after a bounded number of other events. This property, together with suspendability means that even infinite cycles can
    /// be modeled, or even dissolved (if preempted and a "stealing" external event is injected; for example the non-cycle
    /// edge of a balance is pulled, dissolving the original cycle).
    ///
    /// </summary>
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

            private Empty()
            {
            }

            public override string ToString() => "Empty";
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

            protected UpstreamBoundaryStageLogic() : base(inCount: 0, outCount: 1)
            {
            }
        }

        public abstract class DownstreamBoundaryStageLogic : GraphStageLogic
        {
            public abstract Inlet In { get; }

            protected DownstreamBoundaryStageLogic() : base(inCount: 1, outCount: 0)
            {
            }
        }

        /// <summary>
        /// INTERNAL API
        /// 
        /// Contains all the necessary information for the GraphInterpreter to be able to implement a connection
        /// between an output and input ports.
        /// </summary>
        public sealed class Connection
        {
            /// <param name="id">Identifier of the connection. Corresponds to the array slot in the <see cref="GraphAssembly"/></param>
            /// <param name="inOwnerId">Identifier of the owner of the input side of the connection. Corresponds to the array slot in the <see cref="GraphAssembly"/></param>
            /// <param name="inOwner">The stage logic that corresponds to the input side of the connection.</param>
            /// <param name="outOwnerId">Identifier of the owner of the output side of the connection. Corresponds to the array slot in the <see cref="GraphAssembly"/></param>
            /// <param name="outOwner">The stage logic that corresponds to the output side of the connection.</param>
            /// <param name="inHandler">The handler that contains the callback for input events.</param>
            /// <param name="outHandler">The handler that contains the callback for output events.</param>
            public Connection(int id, int inOwnerId, GraphStageLogic inOwner, int outOwnerId, GraphStageLogic outOwner,
                IInHandler inHandler, IOutHandler outHandler)
            {
                Id = id;
                InOwnerId = inOwnerId;
                InOwner = inOwner;
                OutOwnerId = outOwnerId;
                OutOwner = outOwner;
                InHandler = inHandler;
                OutHandler = outHandler;
            }

            public int Id { get; }

            public int InOwnerId { get; }

            public GraphStageLogic InOwner { get; }

            public int OutOwnerId { get; }

            public GraphStageLogic OutOwner { get; }

            public IInHandler InHandler { get; set; }

            public IOutHandler OutHandler { get; set; }

            public int PortState { get; set; } = InReady;

            public object Slot { get; set; } = Empty.Instance;

            public override string ToString() => $"Connection({Id}, {PortState}, {Slot}, {InHandler}, {OutHandler})";
        }

        #endregion

        public static readonly bool IsDebug = false;

        public const Connection NoEvent = null;
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

        // Using an Object-array avoids holding on to the GraphInterpreter class
        // when this accidentally leaks onto threads that are not stopped when this
        // class should be unloaded.
        private static readonly ThreadLocal<object[]> CurrentInterpreter = new ThreadLocal<object[]>(() => new object[1]);

        public static GraphInterpreter Current
        {
            get
            {
                if (CurrentInterpreter.Value[0] == null)
                    throw new InvalidOperationException("Something went terribly wrong!");
                return (GraphInterpreter) CurrentInterpreter.Value[0];
            }
        }

        public static GraphInterpreter CurrentInterpreterOrNull => (GraphInterpreter) CurrentInterpreter.Value[0];

        public static readonly Attributes[] SingleNoAttribute = {Attributes.None};

        public readonly GraphStageLogic[] Logics;
        public readonly GraphAssembly Assembly;
        public readonly IMaterializer Materializer;
        public readonly ILoggingAdapter Log;
        public readonly Connection[] Connections;
        public readonly Action<GraphStageLogic, object, Action<object>> OnAsyncInput;
        public readonly bool FuzzingMode;

        public IActorRef Context { get; }

        // The number of currently running stages. Once this counter reaches zero, the interpreter is considered to be completed.
        public int RunningStagesCount;

        //Counts how many active connections a stage has. Once it reaches zero, the stage is automatically stopped.
        private readonly int[] _shutdownCounter;

        // An event queue implemented as a circular buffer
        private readonly Connection[] _eventQueue;
        private readonly int _mask;
        private int _queueHead;
        private int _queueTail;

        // the first events in preStart blocks should be not chased
        private int _chaseCounter;
        private Connection _chasedPush = NoEvent;
        private Connection _chasedPull = NoEvent;

        public GraphInterpreter(
            GraphAssembly assembly,
            IMaterializer materializer,
            ILoggingAdapter log,
            GraphStageLogic[] logics,
            Connection[] connections,
            Action<GraphStageLogic, object, Action<object>> onAsyncInput,
            bool fuzzingMode,
            IActorRef context)
        {
            Logics = logics;
            Assembly = assembly;
            Materializer = materializer;
            Log = log;
            Connections = connections;
            OnAsyncInput = onAsyncInput;
            FuzzingMode = fuzzingMode;
            Context = context;

            RunningStagesCount = Assembly.Stages.Length;

            _shutdownCounter = new int[assembly.Stages.Length];
            for (var i = 0; i < _shutdownCounter.Length; i++)
            {
                var shape = assembly.Stages[i].Shape;
                _shutdownCounter[i] = shape.Inlets.Count() + shape.Outlets.Count();
            }

            _eventQueue = new Connection[1 << (32 - (assembly.ConnectionCount - 1).NumberOfLeadingZeros())];
            _mask = _eventQueue.Length - 1;
        }

        private int ChaseLimit => FuzzingMode ? 0 : 16;

        internal GraphStageLogic ActiveStage { get; private set; }

        internal IMaterializer SubFusingMaterializer { get; private set; }

        private string QueueStatus()
        {
            var contents = Enumerable.Range(_queueHead, _queueTail - _queueHead).Select(i => _eventQueue[i & _mask]);
            return $"({_eventQueue.Length}, {_queueHead}, {_queueTail})({string.Join(", ", contents)})";
        }

        private string _name;
        internal string Name => _name ?? (_name = GetHashCode().ToString("x"));

        /// <summary>
        /// Assign the boundary logic to a given connection. This will serve as the interface to the external world
        /// (outside the interpreter) to process and inject events.
        /// </summary>
        public void AttachUpstreamBoundary(Connection connection, UpstreamBoundaryStageLogic logic)
        {
            logic.PortToConn[logic.Out.Id + logic.InCount] = connection;
            logic.Interpreter = this;
            connection.OutHandler = (IOutHandler) logic.Handlers[0];
        }

        public void AttachUpstreamBoundary(int connection, UpstreamBoundaryStageLogic logic)
            => AttachUpstreamBoundary(Connections[connection], logic);

        /// <summary>
        /// Assign the boundary logic to a given connection. This will serve as the interface to the external world
        /// (outside the interpreter) to process and inject events.
        /// </summary>
        public void AttachDownstreamBoundary(Connection connection, DownstreamBoundaryStageLogic logic)
        {
            logic.PortToConn[logic.In.Id] = connection;
            logic.Interpreter = this;
            connection.InHandler = (IInHandler) logic.Handlers[0];
        }

        public void AttachDownstreamBoundary(int connection, DownstreamBoundaryStageLogic logic)
            => AttachDownstreamBoundary(Connections[connection], logic);

        /// <summary>
        /// Dynamic handler changes are communicated from a GraphStageLogic by this method.
        /// </summary>
        public void SetHandler(Connection connection, IInHandler handler)
        {
            if (IsDebug) Console.WriteLine($"{Name} SETHANDLER {OutOwnerName(connection)} (in) {handler}");
            connection.InHandler = handler;
        }

        /// <summary>
        /// Dynamic handler changes are communicated from a GraphStageLogic by this method.
        /// </summary>
        public void SetHandler(Connection connection, IOutHandler handler)
        {
            if (IsDebug) Console.WriteLine($"{Name} SETHANDLER {OutOwnerName(connection)} (out) {handler}");
            connection.OutHandler = handler;
        }

        /// <summary>
        /// Returns true if there are pending unprocessed events in the event queue.
        /// </summary>
        public bool IsSuspended => _queueHead != _queueTail;

        /// <summary>
        /// Returns true if there are no more running stages and pending events.
        /// </summary>
        public bool IsCompleted => RunningStagesCount == 0 && !IsSuspended;

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
            for (var i = 0; i < Logics.Length; i++)
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
                if (!IsStageCompleted(logic)) FinalizeStage(logic);
        }

        // Debug name for a connections input part
        private string InOwnerName(Connection connection)
        {
            var owner = Assembly.InletOwners[connection.Id];
            return owner == Boundary ? "DownstreamBoundary" : Assembly.Stages[owner].ToString();
        }

        // Debug name for a connections output part
        private string OutOwnerName(Connection connection)
        {
            var owner = Assembly.OutletOwners[connection.Id];
            return owner == Boundary ? "UpstreamBoundary" : Assembly.Stages[owner].ToString();
        }

        // Debug name for a connections input part
        private string InLogicName(Connection connection)
        {
            var owner = Assembly.InletOwners[connection.Id];
            return owner == Boundary ? "DownstreamBoundary" : Logics[owner].ToString();
        }

        // Debug name for a connections output part
        private string OutLogicName(Connection connection)
        {
            var owner = Assembly.OutletOwners[connection.Id];
            return owner == Boundary ? "UpstreamBoundary" : Logics[owner].ToString();
        }

        private string ShutdownCounters() => string.Join(",",
            _shutdownCounter.Select(x => x >= KeepGoingFlag ? $"{x & KeepGoingMask}(KeepGoing)" : x.ToString()));

        /// <summary>
        /// Executes pending events until the given limit is met. If there were remaining events, <see cref="IsSuspended"/> will return true.
        /// </summary>
        public int Execute(int eventLimit)
        {
            if (IsDebug)
                Console.WriteLine(
                    $"{Name} ---------------- EXECUTE {QueueStatus()} (running={RunningStagesCount}, shutdown={ShutdownCounters()})");
            var currentInterpreterHolder = CurrentInterpreter.Value;
            var previousInterpreter = currentInterpreterHolder[0];
            currentInterpreterHolder[0] = this;
            var eventsRemaining = eventLimit;
            try
            {
                while (eventsRemaining > 0 && _queueTail != _queueHead)
                {
                    var connection = Dequeue();
                    eventsRemaining--;
                    _chaseCounter = Math.Min(ChaseLimit, eventsRemaining);

                    // This is the "normal" event processing code which dequeues directly from the internal event queue. Since
                    // most execution paths tend to produce either a Push that will be propagated along a longer chain we take
                    // extra steps below to make this more efficient.
                    try
                    {
                        ProcessEvent(connection);
                    }
                    catch (Exception ex)
                    {
                        ReportStageError(ex);
                    }

                    AfterStageHasRun(ActiveStage);

                    /*
                      * "Event chasing" optimization follows from here. This optimization works under the assumption that a Push or
                      * Pull is very likely immediately followed by another Push/Pull. The difference from the "normal" event
                      * dispatch is that chased events are never touching the event queue, they use a "streamlined" execution path
                      * instead. Looking at the scenario of a Push, the following events will happen.
                      *  - "normal" dispatch executes an onPush event
                      *  - stage eventually calls push()
                      *  - code inside the push() method checks the validity of the call, and also if it can be safely ignored
                      *    (because the target stage already completed we just have not been notified yet)
                      *  - if the upper limit of ChaseLimit has not been reached, then the Connection is put into the chasedPush
                      *    variable
                      *  - the loop below immediately captures this push and dispatches it
                      *
                      * What is saved by this optimization is three steps:
                      *  - no need to enqueue the Connection in the queue (array), it ends up in a simple variable, reducing
                      *    pressure on array load-store
                      *  - no need to dequeue the Connection from the queue, similar to above
                      *  - no need to decode the event, we know it is a Push already
                      *  - no need to check for validity of the event because we already checked at the push() call, and there
                      *    can be no concurrent events interleaved unlike with the normal dispatch (think about a cancel() that is
                      *    called in the target stage just before the onPush() arrives). This avoids unnecessary branching.
                    */

                    // Chasing PUSH events
                    while (_chasedPush != NoEvent)
                    {
                        var con = _chasedPush;
                        _chasedPush = NoEvent;

                        try
                        {
                            ProcessPush(con);
                        }
                        catch (Exception ex)
                        {
                            ReportStageError(ex);
                        }

                        AfterStageHasRun(ActiveStage);
                    }

                    // Chasing PULL events
                    while (_chasedPull != NoEvent)
                    {
                        var con = _chasedPull;
                        _chasedPull = NoEvent;

                        try
                        {
                            ProcessPull(con);
                        }
                        catch (Exception ex)
                        {
                            ReportStageError(ex);
                        }

                        AfterStageHasRun(ActiveStage);
                    }

                    if (_chasedPush != NoEvent)
                    {
                        Enqueue(_chasedPush);
                        _chasedPush = NoEvent;
                    }
                }

                // Event *must* be enqueued while not in the execute loop (events enqueued from external, possibly async events)
                _chaseCounter = 0;
            }
            finally
            {
                currentInterpreterHolder[0] = previousInterpreter;
            }
            if (IsDebug) Console.WriteLine($"{Name} ---------------- {QueueStatus()} (running={RunningStagesCount}, shutdown={ShutdownCounters()})");
            // TODO: deadlock detection
            return eventsRemaining;
        }

        private void ReportStageError(Exception e)
        {
            if (ActiveStage == null)
                throw e;

            var stage = Assembly.Stages[ActiveStage.StageId];
            if (Log.IsErrorEnabled)
                Log.Error(e, $"Error in stage [{stage}]: {e.Message}");

            ActiveStage.FailStage(e);

            // Abort chasing
            _chaseCounter = 0;
            if (_chasedPush != NoEvent)
            {
                Enqueue(_chasedPush);
                _chasedPush = NoEvent;
            }
            
            if (_chasedPull != NoEvent)
            {
                Enqueue(_chasedPull);
                _chasedPull = NoEvent;
            }

        }
        

        public void RunAsyncInput(GraphStageLogic logic, object evt, Action<object> handler)
        {
            if (!IsStageCompleted(logic))
            {
                if (IsDebug) Console.WriteLine($"{Name} ASYNC {evt} ({handler}) [{logic}]");
                var currentInterpreterHolder = CurrentInterpreter.Value;
                var previousInterpreter = currentInterpreterHolder[0];
                currentInterpreterHolder[0] = this;
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
                    currentInterpreterHolder[0] = previousInterpreter;
                }
            }
        }

        /// <summary>
        /// Decodes and processes a single event for the given connection
        /// </summary>
        private void ProcessEvent(Connection connection)
        {
            // this must be the state after returning without delivering any signals, to avoid double-finalization of some unlucky stage
            // (this can happen if a stage completes voluntarily while connection close events are still queued)
            ActiveStage = null;
            var code = connection.PortState;

            // Manual fast decoding, fast paths are PUSH and PULL
            if ((code & (Pushing | InClosed | OutClosed)) == Pushing)
            {
                // PUSH
                ProcessPush(connection);
            }
            else if ((code & (Pulling | OutClosed | InClosed)) == Pulling)
            {
                // PULL
                ProcessPull(connection);
            }
            else if ((code & (OutClosed | InClosed)) == InClosed)
            {
                // CANCEL
                ActiveStage = connection.OutOwner;
                if (IsDebug) Console.WriteLine($"{Name} CANCEL {InOwnerName(connection)} -> {OutOwnerName(connection)} ({connection.OutHandler}) [{OutLogicName(connection)}]");
                connection.PortState |= OutClosed;
                CompleteConnection(connection.OutOwnerId);
                connection.OutHandler.OnDownstreamFinish();
            }
            else if ((code & (OutClosed | InClosed)) == OutClosed)
            {
                // COMPLETIONS
                if ((code & Pushing) == 0)
                {
                    // Normal completion (no push pending)
                    if (IsDebug) Console.WriteLine($"{Name} COMPLETE {OutOwnerName(connection)} -> {InOwnerName(connection)} ({connection.InHandler}) [{InLogicName(connection)}]");
                    connection.PortState |= InClosed;
                    ActiveStage = connection.InOwner;
                    CompleteConnection(connection.InOwnerId);

                    if ((connection.PortState & InFailed) == 0)
                        connection.InHandler.OnUpstreamFinish();
                    else
                        connection.InHandler.OnUpstreamFailure(((Failed)connection.Slot).Reason);
                }
                else
                {
                    // Push is pending, first process push, then re-enqueue closing event
                    ProcessPush(connection);
                    Enqueue(connection);
                }
            }
        }
        
        private void ProcessPush(Connection connection)
        {
            if (IsDebug) Console.WriteLine($"{Name} PUSH {OutOwnerName(connection)} -> {InOwnerName(connection)},  {connection.Slot} ({connection.InHandler}) [{InLogicName(connection)}]");
            ActiveStage = connection.InOwner;
            connection.PortState ^= PushEndFlip;
            connection.InHandler.OnPush();
        }

        private void ProcessPull(Connection connection)
        {
            if (IsDebug) Console.WriteLine($"{Name} PULL {InOwnerName(connection)} -> {OutOwnerName(connection)}, ({connection.OutHandler}) [{OutLogicName(connection)}]");
            ActiveStage = connection.OutOwner;
            connection.PortState ^= PullEndFlip;
            connection.OutHandler.OnPull();
        }

        private Connection Dequeue()
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

        public void Enqueue(Connection connection)
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
        /// Returns true if the given stage is already completed
        /// </summary>
        internal bool IsStageCompleted(GraphStageLogic stage) => stage != null && _shutdownCounter[stage.StageId] == 0;

        /// <summary>
        ///  Register that a connection in which the given stage participated has been completed and therefore the stage itself might stop, too.
        /// </summary>
        private void CompleteConnection(int stageId)
        {
            if (stageId != Boundary)
            {
                var activeConnections = _shutdownCounter[stageId];
                if (activeConnections > 0)
                    _shutdownCounter[stageId] = activeConnections - 1;
            }
        }

        internal void SetKeepGoing(GraphStageLogic logic, bool enabled)
        {
            if (enabled)
                _shutdownCounter[logic.StageId] |= KeepGoingFlag;
            else
                _shutdownCounter[logic.StageId] &= KeepGoingMask;
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

        internal void ChasePush(Connection connection)
        {
            if (_chaseCounter > 0 && _chasedPush == NoEvent)
            {
                _chaseCounter--;
                _chasedPush = connection;
            }
            else
                Enqueue(connection);
        }

        internal void ChasePull(Connection connection)
        {
            if (_chaseCounter > 0 && _chasedPull == NoEvent)
            {
                _chaseCounter--;
                _chasedPull = connection;
            }
            else
                Enqueue(connection);
        }

        internal void Complete(Connection connection)
        {
            var currentState = connection.PortState;
            if (IsDebug) Console.WriteLine($"{Name}   Complete({connection}) [{currentState}]");
            connection.PortState = currentState | OutClosed;

            // Push-Close needs special treatment, cannot be chased, convert back to ordinary event
            if (_chasedPush == connection)
            {
                _chasedPush = NoEvent;
                Enqueue(connection);
            }
            else if ((currentState & (InClosed |Pushing |Pulling|OutClosed)) == 0)
                Enqueue(connection);

            if((currentState & OutClosed) == 0)
                CompleteConnection(connection.OutOwnerId);
        }

        internal void Fail(Connection connection, Exception reason)
        {
            var currentState = connection.PortState;
            if (IsDebug) Console.WriteLine($"{Name}   Fail({connection}, {reason}) [{currentState}]");
            connection.PortState = currentState | OutClosed;
            if ((currentState & (InClosed | OutClosed)) == 0)
            {
                connection.PortState = currentState | (OutClosed | InFailed);
                connection.Slot = new Failed(reason, connection.Slot);
                if ((currentState & (Pulling | Pushing)) == 0)
                    Enqueue(connection);
            }

            if ((currentState & OutClosed) == 0)
                CompleteConnection(connection.OutOwnerId);
        }

        internal void Cancel(Connection connection)
        {
            var currentState = connection.PortState;
            if (IsDebug) Console.WriteLine($"{Name}   Cancel({connection}) [{currentState}]");
            connection.PortState = currentState | InClosed;
            if ((currentState & OutClosed) == 0)
            {
                connection.Slot = Empty.Instance;
                if ((currentState & (Pulling | Pushing | InClosed)) == 0)
                    Enqueue(connection);
            }

            if ((currentState & InClosed) == 0)
                CompleteConnection(connection.InOwnerId);
        }

        /// <summary>
        /// Debug utility to dump the "waits-on" relationships in DOT format to the console for analysis of deadlocks.
        /// 
        /// Only invoke this after the interpreter completely settled, otherwise the results might be off. This is a very
        /// simplistic tool, make sure you are understanding what you are doing and then it will serve you well.
        /// </summary>
        public void DumpWaits() => Console.WriteLine(this);

        public override string ToString()
        {
            var builder = new StringBuilder("digraph waits {\n");

            for (var i = 0; i < Assembly.Stages.Length; i++)
                builder.AppendLine($"N{i} [label={Assembly.Stages[i]}]");

            for (var i = 0; i < Connections.Length; i++)
            {
                var state = Connections[i].PortState;
                if (state == InReady)
                    builder.Append($"  {NameIn(i)} -> {NameOut(i)} [label=shouldPull; color=blue];");
                else if (state == OutReady)
                    builder.Append($"  {NameOut(i)} -> {NameIn(i)} [label=shouldPush; color=red];");
                else if( (state | InClosed | OutClosed) == (InClosed | OutClosed))
                    builder.Append($"  {NameIn(i)} -> {NameOut(i)} [style=dotted; label=closed dir=both];");
            }

            builder.AppendLine();
            builder.AppendLine("}");
            builder.Append($"// {QueueStatus()} (running={RunningStagesCount}, shutdown={ShutdownCounters()}");
            return builder.ToString();
        }

        private string NameIn(int port) => Assembly.InletOwners[port] == Boundary ? "Out" + port : "N" + port;

        private string NameOut(int port) => Assembly.OutletOwners[port] == Boundary ? "Out" + port : "N" + port;}
}
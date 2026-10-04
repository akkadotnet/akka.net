//-----------------------------------------------------------------------
// <copyright file="GraphInterpreter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Streams.Implementation;
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
    [InternalApi]
    public sealed class GraphInterpreter
    {
        #region internal classes

        /// <summary>
        /// Marker object that indicates that a port holds no element since it was already grabbed. 
        /// The port is still pullable, but there is no more element to grab.
        /// </summary>
        public sealed class Empty
        {
            /// <summary>
            /// Marker for a connection slot with no available element.
            /// </summary>
            public static readonly Empty Instance = new();

            private Empty()
            {
            }

            /// <summary>
            /// Returns the marker name.
            /// </summary>
            /// <returns>The string "Empty".</returns>
            public override string ToString() => "Empty";
        }


        /// <summary>
        /// Marker class that indicates that a port was failed with a given cause and a potential outstanding element
        /// </summary>
        public sealed class Failed
        {
            /// <summary>
            /// The failure cause stored in this connection slot.
            /// </summary>
            public readonly Exception Reason;
            /// <summary>
            /// The element that was in the slot when the failure occurred, if any.
            /// </summary>
            public readonly object PreviousElement;

            /// <summary>
            /// Stores a failure cause and any element that was already in flight.
            /// </summary>
            /// <param name="reason">The failure cause stored in the slot.</param>
            /// <param name="previousElement">The element that was in the slot when the failure occurred, if any.</param>
            public Failed(Exception reason, object previousElement)
            {
                Reason = reason;
                PreviousElement = previousElement;
            }
        }
        
        /// <summary>
        /// Marker class that indicates that a port was cancelled with a given cause
        /// </summary>
        public sealed class Cancelled
        {
            public readonly Exception Cause;

            public Cancelled(Exception cause)
            {
                Cause = cause;
            }
        }

        /// <summary>
        /// Base logic for an upstream graph boundary that injects signals into an interpreter connection.
        /// </summary>
        public abstract class UpstreamBoundaryStageLogic : GraphStageLogic
        {
            /// <summary>
            /// The outlet connected to the graph interpreter.
            /// </summary>
            public abstract Outlet Out { get; }

            /// <summary>
            /// Initializes boundary logic with no inlets and one outlet.
            /// </summary>
            protected UpstreamBoundaryStageLogic() : base(inCount: 0, outCount: 1)
            {
            }
        }

        /// <summary>
        /// Base logic for a downstream graph boundary that receives signals from an interpreter connection.
        /// </summary>
        public abstract class DownstreamBoundaryStageLogic : GraphStageLogic
        {
            /// <summary>
            /// The inlet connected to the graph interpreter.
            /// </summary>
            public abstract Inlet In { get; }

            /// <summary>
            /// Initializes boundary logic with one inlet and no outlets.
            /// </summary>
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
        [InternalApi]
        public sealed class Connection
        {
            /// <summary>
            /// Creates a connection between the supplied input and output stage owners and handlers.
            /// </summary>
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

            /// <summary>
            /// The connection index in the graph assembly.
            /// </summary>
            public int Id { get; }

            /// <summary>
            /// The graph assembly index of the stage that owns the input side, or <see cref="Boundary"/> for a boundary.
            /// </summary>
            public int InOwnerId { get; }

            /// <summary>
            /// The logic that owns the input side of this connection, or null when that side is a graph boundary.
            /// </summary>
            public GraphStageLogic InOwner { get; }

            /// <summary>
            /// The graph assembly index of the stage that owns the output side, or <see cref="Boundary"/> for a boundary.
            /// </summary>
            public int OutOwnerId { get; }

            /// <summary>
            /// The logic that owns the output side of this connection, or null when that side is a graph boundary.
            /// </summary>
            public GraphStageLogic OutOwner { get; }

            /// <summary>
            /// The handler invoked for events delivered to the input side.
            /// </summary>
            public IInHandler InHandler { get; set; }

            /// <summary>
            /// The handler invoked for events delivered to the output side.
            /// </summary>
            public IOutHandler OutHandler { get; set; }

            /// <summary>
            /// See <see cref="GraphInterpreter"/> about possible states
            /// </summary>
            public int PortState { get; set; } = InReady;

            /// <summary>
            /// Can either be:
            /// * An in-flight element
            /// * A failure (with an optional in-flight element), if elem is an instance of <see cref="Failed"/>
            /// * A cancellation cause, is elem is an instance of <see cref="Cancelled"/>
            /// </summary>
            public object Slot { get; set; } = Empty.Instance;

            /// <summary>
            /// The OpenTelemetry trace context associated with the element currently in <see cref="Slot"/>,
            /// if any. Captured by <c>GraphStageLogic.Push</c> from <c>Activity.Current</c> at push time,
            /// and cleared by <c>GraphStageLogic.Grab</c> alongside the element. Used by
            /// <c>GraphInterpreter.ProcessPush</c> to start a stage-scoped <c>Activity</c> with this
            /// context as parent before invoking the downstream stage's handler, so that trace context
            /// flows end-to-end through a stream even across dispatcher boundaries where
            /// <c>AsyncLocal</c> would be lost.
            /// </summary>
            internal ActivityContext? SlotContext { get; set; }

            /// <summary>
            /// Additional trace contexts (beyond <see cref="SlotContext"/>) that should be attached
            /// as <see cref="ActivityLink"/>s to the downstream stage span when this slot is processed.
            /// Set by fan-in stages such as <c>BatchWeighted</c>, <c>GroupedWithin</c>, and <c>Merge</c>
            /// when a single output element represents the merged work of N upstream input elements.
            /// The first input's context becomes <see cref="SlotContext"/> (primary parent); the
            /// remaining N-1 contexts go here as forward references.
            /// </summary>
            internal ActivityContext[] SlotLinks { get; set; }

            /// <summary>
            /// Override for the primary trace context that the next <c>Push</c> on this connection's
            /// outlet should attach to <see cref="SlotContext"/>. Set by fan-in stages just before
            /// they call <c>Push</c> so that the merged output element carries the FIRST input's
            /// trace context as its primary parent — instead of <c>Activity.Current</c>, which would
            /// otherwise be the stage span of whichever input triggered the flush. Consumed and
            /// cleared by <c>GraphStageLogic.Push</c>.
            /// </summary>
            internal ActivityContext? PendingPushPrimaryContext { get; set; }

            /// <summary>
            /// Companion to <see cref="PendingPushPrimaryContext"/>. Holds the additional trace
            /// contexts (links) that the next <c>Push</c> should attach to <see cref="SlotLinks"/>.
            /// Consumed and cleared by <c>GraphStageLogic.Push</c>.
            /// </summary>
            internal ActivityContext[] PendingPushLinks { get; set; }

            /// <summary>
            /// Returns a diagnostic representation of the connection and its handlers.
            /// </summary>
            /// <returns>A string containing the connection index, port state, slot, and handlers.</returns>
            public override string ToString() => $"Connection({Id}, {PortState}, {Slot}, {InHandler}, {OutHandler})";
        }

        #endregion

        /// <summary>
        /// Enables verbose interpreter diagnostics when set to true.
        /// </summary>
        public const bool IsDebug = false;

        /// <summary>
        /// Sentinel used when no connection event is queued or being chased.
        /// </summary>
        public const Connection NoEvent = null;
        /// <summary>
        /// Owner index used for a connection side attached to an external graph boundary.
        /// </summary>
        public const int Boundary = -1;

        /// <summary>
        /// Port-state bit indicating the input side is ready for a pull.
        /// </summary>
        public const int InReady = 1;
        /// <summary>
        /// Port-state bit indicating that a pull is in flight.
        /// </summary>
        public const int Pulling = 1 << 1;
        /// <summary>
        /// Port-state bit indicating that a push is in flight.
        /// </summary>
        public const int Pushing = 1 << 2;
        /// <summary>
        /// Port-state bit indicating the output side is ready to send an element.
        /// </summary>
        public const int OutReady = 1 << 3;

        /// <summary>
        /// Port-state bit indicating that the input side is closed.
        /// </summary>
        public const int InClosed = 1 << 4;
        /// <summary>
        /// Port-state bit indicating that the output side is closed.
        /// </summary>
        public const int OutClosed = 1 << 5;
        /// <summary>
        /// Port-state bit indicating that an input close carries a failure.
        /// </summary>
        public const int InFailed = 1 << 6;

        /// <summary>
        /// State bits toggled when a pull begins.
        /// </summary>
        public const int PullStartFlip = InReady | Pulling;
        /// <summary>
        /// State bits toggled when a pull reaches the output handler.
        /// </summary>
        public const int PullEndFlip = Pulling | OutReady;
        /// <summary>
        /// State bits toggled when a push begins.
        /// </summary>
        public const int PushStartFlip = Pushing | OutReady;
        /// <summary>
        /// State bits toggled when a push reaches the input handler.
        /// </summary>
        public const int PushEndFlip = InReady | Pushing;

        /// <summary>
        /// Shutdown-counter flag indicating that a stage should remain active after its connections close.
        /// </summary>
        public const int KeepGoingFlag = 0x4000000;
        /// <summary>
        /// Mask used to clear the keep-going flag from a shutdown counter.
        /// </summary>
        public const int KeepGoingMask = 0x3ffffff;

        // Using an Object-array avoids holding on to the GraphInterpreter class
        // when this accidentally leaks onto threads that are not stopped when this
        // class should be unloaded.
        private static readonly ThreadLocal<object[]> CurrentInterpreter = new(() => new object[1]);

        /// <summary>
        /// The interpreter currently executing on this thread.
        /// </summary>
        /// <exception cref="InvalidOperationException">No interpreter is registered for the current thread.</exception>
        public static GraphInterpreter Current
        {
            get
            {
                if (CurrentInterpreter.Value[0] == null)
                    throw new InvalidOperationException("Something went terribly wrong!");
                return (GraphInterpreter) CurrentInterpreter.Value[0];
            }
        }

        /// <summary>
        /// The interpreter currently executing on this thread, or null when none is registered.
        /// </summary>
        public static GraphInterpreter CurrentInterpreterOrNull => (GraphInterpreter) CurrentInterpreter.Value[0];

        /// <summary>
        /// A one-element attribute array containing <see cref="Attributes.None"/>.
        /// </summary>
        public static readonly Attributes[] SingleNoAttribute = {Attributes.None};

        /// <summary>
        /// The stage logic instances executed by this interpreter.
        /// </summary>
        public readonly GraphStageLogic[] Logics;
        /// <summary>
        /// The graph assembly interpreted by this instance.
        /// </summary>
        public readonly GraphAssembly Assembly;
        /// <summary>
        /// The materializer used by stages when no sub-fusing materializer is supplied.
        /// </summary>
        public readonly IMaterializer Materializer;
        /// <summary>
        /// The logger used for interpreter and stage lifecycle errors.
        /// </summary>
        public readonly ILoggingAdapter Log;
        /// <summary>
        /// The connections indexed by the graph assembly.
        /// </summary>
        public readonly Connection[] Connections;
        /// <summary>
        /// Callback used to deliver asynchronous stage input from stage logics.
        /// </summary>
        public readonly Action<GraphStageLogic, object, TaskCompletionSource<Done>, Action<object>> OnAsyncInput;
        /// <summary>
        /// Whether event processing uses randomized connection order for fuzzing.
        /// </summary>
        public readonly bool FuzzingMode;

        /// <summary>
        /// The actor reference associated with this interpreter.
        /// </summary>
        public IActorRef Context { get; }

        // The number of currently running stages. Once this counter reaches zero, the interpreter is considered to be completed.
        /// <summary>
        /// The number of stage logics that have not completed.
        /// </summary>
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

        /// <summary>
        /// Creates an interpreter for the supplied assembly, stage logics, and connections.
        /// </summary>
        /// <param name="assembly">The graph assembly whose stages and connections are interpreted.</param>
        /// <param name="materializer">The materializer used by the stage logics.</param>
        /// <param name="log">The logger used for interpreter errors.</param>
        /// <param name="logics">The stage logic instances, indexed by stage id.</param>
        /// <param name="connections">The connections corresponding to the assembly connection indexes.</param>
        /// <param name="onAsyncInput">Callback that forwards asynchronous input events from stage logics.</param>
        /// <param name="fuzzingMode">Whether to randomize event order for fuzzing.</param>
        /// <param name="context">The actor reference associated with this interpreter.</param>
        public GraphInterpreter(
                    GraphAssembly assembly,
                    IMaterializer materializer,
                    ILoggingAdapter log,
                    GraphStageLogic[] logics,
                    Connection[] connections,
                    Action<GraphStageLogic, object, TaskCompletionSource<Done>, Action<object>> onAsyncInput,
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

        /// <summary>
        /// The stage logic whose callback is currently executing.
        /// </summary>
        internal GraphStageLogic ActiveStage { get; private set; }

        /// <summary>
        /// The materializer used by stage logic to materialize sub-flows.
        /// </summary>
        internal IMaterializer SubFusingMaterializer { get; private set; }

        private string QueueStatus()
        {
            var contents = Enumerable.Range(_queueHead, _queueTail - _queueHead).Select(i => _eventQueue[i & _mask]);
            return $"({_eventQueue.Length}, {_queueHead}, {_queueTail})({string.Join(", ", contents)})";
        }

        private string _name;
        /// <summary>
        /// A lazily created hexadecimal identifier used in interpreter diagnostics.
        /// </summary>
        internal string Name => _name ??= GetHashCode().ToString("x");

        /// <summary>
        /// Assigns upstream boundary logic to a connection, providing an interface for external publishers to inject events.
        /// </summary>
        /// <param name="connection">The connection representing the graph boundary.</param>
        /// <param name="logic">The upstream boundary logic attached to the connection.</param>
        public void AttachUpstreamBoundary(Connection connection, UpstreamBoundaryStageLogic logic)
        {
            logic.PortToConn[logic.Out.Id + logic.InCount] = connection;
            logic.Interpreter = this;
            connection.OutHandler = (IOutHandler) logic.Handlers[0];
        }

        /// <summary>
        /// Connects upstream boundary logic to the connection at the supplied index.
        /// </summary>
        /// <param name="connection">The connection index in <see cref="Connections"/>.</param>
        /// <param name="logic">The upstream boundary logic attached to the connection.</param>
        public void AttachUpstreamBoundary(int connection, UpstreamBoundaryStageLogic logic)
            => AttachUpstreamBoundary(Connections[connection], logic);

        /// <summary>
        /// Assigns downstream boundary logic to a connection, providing an interface for external subscribers to receive events.
        /// </summary>
        /// <param name="connection">The connection representing the graph boundary.</param>
        /// <param name="logic">The downstream boundary logic attached to the connection.</param>
        public void AttachDownstreamBoundary(Connection connection, DownstreamBoundaryStageLogic logic)
        {
            logic.PortToConn[logic.In.Id] = connection;
            logic.Interpreter = this;
            connection.InHandler = (IInHandler) logic.Handlers[0];
        }

        /// <summary>
        /// Connects downstream boundary logic to the connection at the supplied index.
        /// </summary>
        /// <param name="connection">The connection index in <see cref="Connections"/>.</param>
        /// <param name="logic">The downstream boundary logic attached to the connection.</param>
        public void AttachDownstreamBoundary(int connection, DownstreamBoundaryStageLogic logic)
            => AttachDownstreamBoundary(Connections[connection], logic);

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        /// <summary>
        /// Sets the handler used for input events. Dynamic handler changes from a <see cref="GraphStageLogic"/> are communicated through this method.
        /// </summary>
        /// <param name="connection">The connection whose input handler is updated.</param>
        /// <param name="handler">The handler to invoke for input-side events.</param>
        public void SetHandler(Connection connection, IInHandler handler)
        {
            if (IsDebug) Console.WriteLine($"{Name} SETHANDLER {OutOwnerName(connection)} (in) {handler}");
            connection.InHandler = handler;
        }

        /// <summary>
        /// Sets the handler used for output events. Dynamic handler changes from a <see cref="GraphStageLogic"/> are communicated through this method.
        /// </summary>
        /// <param name="connection">The connection whose output handler is updated.</param>
        /// <param name="handler">The handler to invoke for output-side events.</param>
        public void SetHandler(Connection connection, IOutHandler handler)
        {
            if (IsDebug) Console.WriteLine($"{Name} SETHANDLER {OutOwnerName(connection)} (out) {handler}");
            connection.OutHandler = handler;
        }
#pragma warning restore CS0162

        /// <summary>
        /// Returns true if there are pending unprocessed events in the event queue.
        /// </summary>
        public bool IsSuspended => _queueHead != _queueTail;

        /// <summary>
        /// Returns true if there are no more running stages and pending events.
        /// </summary>
        public bool IsCompleted => RunningStagesCount == 0 && !IsSuspended;

        /// <summary>
        /// Initializes stage logic and calls <see cref="GraphStageLogic.PreStart"/>. A supplied <see cref="SubFusingMaterializer"/> can avoid creating actors when stages materialize sub-flows; if none is available, the interpreter uses its regular materializer because fusing is an optimization.
        /// </summary>
        /// <param name="subMaterializer">The materializer used for sub-flows, or null to use <see cref="Materializer"/>.</param>
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

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        /// <summary>
        /// Dequeues and processes pending connection events up to the supplied limit. If queued events remain, <see cref="IsSuspended"/> returns true.
        /// </summary>
        /// <param name="eventLimit">The maximum number of connection events to process.</param>
        /// <returns>The unused dequeue budget after processing queued events.</returns>
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
#pragma warning restore CS0162

        private void ReportStageError(Exception e)
        {
            if (ActiveStage == null)
                ExceptionDispatchInfo.Capture(e).Throw();

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


#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        public void RunAsyncInput(GraphStageLogic logic, object evt, TaskCompletionSource<Done> promise, Action<object> handler)
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
                        if (!ReferenceEquals(promise, GraphStageLogic.NoPromise))
                        {
                            promise.TrySetResult(Done.Instance);
                            logic.OnFeedbackDispatched();
                        }
                    }
                    catch (Exception e)
                    {
                        if (!ReferenceEquals(promise, GraphStageLogic.NoPromise))
                        {
                            promise.TrySetException(e);
                            logic.OnFeedbackDispatched();
                        }
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
                var cause = ((Cancelled)connection.Slot).Cause;
                connection.Slot = Empty.Instance;
                connection.OutHandler.OnDownstreamFinish(cause);
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
#pragma warning restore CS0162

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessPush(Connection connection)
        {
            //if (IsDebug) Console.WriteLine($"{Name} PUSH {OutOwnerName(connection)} -> {InOwnerName(connection)},  {connection.Slot} ({connection.InHandler}) [{InLogicName(connection)}]");
            ActiveStage = connection.InOwner;
            connection.PortState ^= PushEndFlip;

            // Trace-context carry: if the upstream Push captured an ActivityContext, start a
            // stage-scoped Activity with it as parent so the downstream stage's handler (and any
            // child spans it creates, e.g. OpenTelemetry.Instrumentation.SqlClient) correctly
            // parents back to the producer's trace. If the source has no listeners or the element
            // has no captured context, StartActivity returns null and this path allocates nothing.
            //
            // Fan-in stages may attach additional ActivityContexts as SlotLinks — those become
            // ActivityLinks on the downstream stage span, preserving trace continuity for every
            // input element that contributed to a single merged output. See SetFanInTraceContext.
            //
            // InOwner is null for boundary connections (actor/async boundary plumbing, not a user
            // stage), so skip them — otherwise GetStageOperationName(null) throws an NRE (issue #8241).
            if (connection.InOwner != null && connection.SlotContext.HasValue && StreamsDiagnostics.ActivitySource.HasListeners())
            {
                var slotContext = connection.SlotContext.Value;
                var slotLinks = connection.SlotLinks;
                var stage = connection.InOwner;
                ActivityLink[] activityLinks = null;
                if (slotLinks != null && slotLinks.Length > 0)
                {
                    activityLinks = new ActivityLink[slotLinks.Length];
                    for (int i = 0; i < slotLinks.Length; i++)
                        activityLinks[i] = new ActivityLink(slotLinks[i]);
                }
                var stageActivity = StreamsDiagnostics.ActivitySource.StartActivity(
                    StreamsDiagnostics.GetStageOperationName(stage),
                    ActivityKind.Internal,
                    slotContext,
                    tags: null,
                    links: activityLinks);
                if (stageActivity != null)
                {
                    stageActivity.SetTag(StreamsDiagnostics.TagStageType, StreamsDiagnostics.GetStageName(stage));
                    if (activityLinks != null)
                        stageActivity.SetTag(StreamsDiagnostics.TagFanInLinks, activityLinks.Length);
                    try
                    {
                        connection.InHandler.OnPush();
                    }
                    finally
                    {
                        stageActivity.Dispose();
                    }
                    return;
                }
            }

            connection.InHandler.OnPush();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessPull(Connection connection)
        {
            //if (IsDebug) Console.WriteLine($"{Name} PULL {InOwnerName(connection)} -> {OutOwnerName(connection)}, ({connection.OutHandler}) [{OutLogicName(connection)}]");
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
                (_eventQueue[swapWith], _eventQueue[idx]) = (_eventQueue[idx], _eventQueue[swapWith]);
            }
            var element = _eventQueue[idx];
            _eventQueue[idx] = NoEvent;
            _queueHead++;
            return element;
        }

        /// <summary>
        /// Adds a connection with a pending event to the interpreter queue.
        /// </summary>
        /// <param name="connection">The connection to enqueue.</param>
        /// <exception cref="Exception">The internal event queue exceeds its expected capacity while debug checks are enabled.</exception>
        public void Enqueue(Connection connection)
        {
#pragma warning disable CS0162 // Unreachable code can be reached if IsDebug is set to true.
            if (IsDebug && _queueTail - _queueHead > _mask) throw new Exception($"{Name} internal queue full ({QueueStatus()}) + {connection}");
#pragma warning restore CS0162
            _eventQueue[_queueTail & _mask] = connection;
            _queueTail++;
        }

        /// <summary>
        /// Finalizes a stage after a callback if its connections have completed and it is not retained by the keep-going flag.
        /// </summary>
        /// <param name="logic">The stage logic whose callback has just run.</param>
        internal void AfterStageHasRun(GraphStageLogic logic)
        {
            if (IsStageCompleted(logic))
            {
                RunningStagesCount--;
                FinalizeStage(logic);
            }
        }

        /// <summary>
        /// Returns whether the stage shutdown counter is zero, which requires all connections to close and the keep-going flag to be clear.
        /// </summary>
        /// <param name="stage">The stage logic to check.</param>
        /// <returns><c>true</c> when the stage has completed; otherwise <c>false</c>.</returns>
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

        /// <summary>
        /// Sets whether a stage remains active after its connections close.
        /// </summary>
        /// <param name="logic">The stage logic whose shutdown counter is updated.</param>
        /// <param name="enabled">Whether to retain the keep-going flag.</param>
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

        /// <summary>
        /// Records the first eligible push for immediate processing during <see cref="Execute"/>; otherwise queues the connection.
        /// </summary>
        /// <param name="connection">The connection with a pending push.</param>
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

        /// <summary>
        /// Records the first eligible pull for immediate processing during <see cref="Execute"/>; otherwise queues the connection.
        /// </summary>
        /// <param name="connection">The connection with a pending pull.</param>
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

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        /// <summary>
        /// Closes the output side of a connection and schedules completion delivery when needed.
        /// </summary>
        /// <param name="connection">The connection whose output side is completed.</param>
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

        /// <summary>
        /// Closes the output side of a connection and schedules failure delivery when needed.
        /// </summary>
        /// <param name="connection">The connection whose output side failed.</param>
        /// <param name="reason">The failure propagated to the input side.</param>
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
                else if (_chasedPush == connection)
                {
                    // Abort chasing so Failure is not lost (chasing does NOT decode the event but assumes it to be a PUSH
                    // but we just changed the event!)
                    _chasedPush = NoEvent;
                    Enqueue(connection);
                }
            }

            if ((currentState & OutClosed) == 0)
                CompleteConnection(connection.OutOwnerId);
        }

        /// <summary>
        /// Closes the input side of a connection and schedules cancellation delivery when needed.
        /// </summary>
        /// <param name="connection">The connection whose input side was canceled.</param>
        /// <param name="cause">The cancellation cause propagated to the output side.</param>
        internal void Cancel(Connection connection, Exception cause)
        {
            var currentState = connection.PortState;
            if (IsDebug) Console.WriteLine($"{Name}   Cancel({connection}) [{currentState}] [{cause.Message}]");
            connection.PortState = currentState | InClosed;
            if ((currentState & OutClosed) == 0)
            {
                connection.Slot = new Cancelled(cause);
                if ((currentState & (Pulling | Pushing | InClosed)) == 0)
                    Enqueue(connection);
                else if (_chasedPull == connection)
                {
                    // Abort chasing so Cancel is not lost (chasing does NOT decode the event but assumes it to be a PULL
                    // but we just changed the event!)
                    _chasedPull = NoEvent;
                    Enqueue(connection);
                }
            }

            if ((currentState & InClosed) == 0)
                CompleteConnection(connection.InOwnerId);
        }
#pragma warning restore CS0162

        /// <summary>
        /// Debug utility to dump the "waits-on" relationships in DOT format to the console for analysis of deadlocks.
        /// 
        /// Only invoke this after the interpreter completely settled, otherwise the results might be off. This is a very
        /// simplistic tool, make sure you are understanding what you are doing and then it will serve you well.
        /// </summary>
        public void DumpWaits() => Console.WriteLine(this);

        /// <summary>
        /// Returns a DOT representation of connection waits and interpreter state for diagnostics.
        /// </summary>
        /// <returns>A DOT graph followed by queue, running-stage, and shutdown-counter details.</returns>
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

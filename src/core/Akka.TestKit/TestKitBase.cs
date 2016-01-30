//-----------------------------------------------------------------------
// <copyright file="TestKitBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit.Internal;
using Akka.Util.Internal;

namespace Akka.TestKit
{
    /// <summary>
    /// <remarks>Unless you're creating a TestKit for a specific test framework, you should probably not inherit directly from this class.</remarks>
    /// </summary>
    public abstract partial class TestKitBase : IActorRefFactory
    {
        private class TestState
        {
            public TestState()
            {
                LastMessage = NullMessageEnvelope.Instance;
            }

            public ActorSystem System { get; set; }
            public TestKitSettings TestKitSettings { get; set; }
            public BlockingQueue<MessageEnvelope> Queue { get; set; }
            public MessageEnvelope LastMessage  { get; set; }
            public IActorRef TestActor { get; set; }
            public TimeSpan? End { get; set; }
            public bool LastWasNoMsg { get; set; } //if last assertion was expectNoMsg, disable timing failure upon within() block end.
            public ILoggingAdapter Log { get; set; }
            public EventFilterFactory EventFilterFactory { get; set; }
        }

        private static readonly Config _defaultConfig = ConfigurationFactory.FromResource<TestKitBase>("Akka.TestKit.Internal.Reference.conf");
        private static readonly Config _fullDebugConfig = ConfigurationFactory.ParseString(@"
                akka.log-dead-letters-during-shutdown = true
                akka.actor.debug.receive = true
                akka.actor.debug.autoreceive = true
                akka.actor.debug.lifecycle = true
                akka.actor.debug.event-stream = true
                akka.actor.debug.unhandled = true
                akka.actor.debug.fsm = true
                akka.actor.debug.router-misconfiguration = true
                akka.log-dead-letters = true
                akka.loglevel = DEBUG
                akka.stdout-loglevel = DEBUG");
        private static readonly AtomicCounter _testActorId = new AtomicCounter(0);

        private readonly ITestKitAssertions _assertions;
        private TestState _testState;

        /// <summary>
        /// Create a new instance of the <see cref="TestKitBase"/> class.
        /// If no <paramref name="system"/> is passed in, a new system 
        /// with <see cref="DefaultConfig"/> will be created.
        /// </summary>
        /// <param name="assertions"></param>
        /// <param name="system">Optional: The actor system.</param>
        /// <param name="testActorName">Optional: The name of the TestActor.</param>
        protected TestKitBase(ITestKitAssertions assertions, ActorSystem system = null, string testActorName=null)
            : this(assertions, system, _defaultConfig, null, testActorName)
        {
        }

        /// <summary>
        /// Create a new instance of the <see cref="TestKitBase"/> class.
        /// A new system with the specified configuration will be created.
        /// </summary>
        /// <param name="config">The configuration to use for the system.</param>
        /// <param name="testActorName">Optional: The name of the TestActor.</param>
        /// <param name="assertions"></param>
        /// <param name="actorSystemName"></param>
        protected TestKitBase(ITestKitAssertions assertions, Config config, string actorSystemName = null, string testActorName = null)
            : this(assertions, null, config ?? ConfigurationFactory.Empty, actorSystemName, testActorName)
        {
        }

        private TestKitBase(ITestKitAssertions assertions, ActorSystem system, Config config, string actorSystemName, string testActorName)
        {
            if(assertions == null) throw new ArgumentNullException("assertions");

            _assertions = assertions;
            
            InitializeTest(system, config, actorSystemName, testActorName);
        }

        protected void InitializeTest(ActorSystem system, Config config, string actorSystemName, string testActorName)
        {
            _testState = new TestState();

            if (system == null)
            {
                var configWithDefaultFallback = config.SafeWithFallback(_defaultConfig);
                system = ActorSystem.Create(actorSystemName ?? "test", configWithDefaultFallback);
            }

            _testState.System = system;

            system.RegisterExtension(new TestKitExtension());
            system.RegisterExtension(new TestKitAssertionsExtension(_assertions));

            _testState.TestKitSettings = TestKitExtension.For(_testState.System);
            _testState.Queue = new BlockingQueue<MessageEnvelope>();
            _testState.Log = Logging.GetLogger(system, GetType());
            _testState.EventFilterFactory = new EventFilterFactory(this);

            //register the CallingThreadDispatcherConfigurator
            _testState.System.Dispatchers.RegisterConfigurator(CallingThreadDispatcher.Id,
                new CallingThreadDispatcherConfigurator(_testState.System.Settings.Config, _testState.System.Dispatchers.Prerequisites));

            if (string.IsNullOrEmpty(testActorName))
                testActorName = "testActor" + _testActorId.IncrementAndGet();

            var testActor = CreateTestActor(system, testActorName);
            //Wait for the testactor to start
            AwaitCondition(() =>
            {
                var repRef = testActor as IRepointableRef;
                return repRef == null || repRef.IsStarted;
            }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));

            if (!(this is INoImplicitSender))
            {
                InternalCurrentActorCellKeeper.Current = (ActorCell)((ActorRefWithCell)testActor).Underlying;
            }
            else if (!(this is TestProbe))
            //HACK: we need to clear the current context when running a No Implicit Sender test as sender from an async test may leak
            //but we should not clear the current context when creating a testprobe from a test
            {
                InternalCurrentActorCellKeeper.Current = null;
            }
            SynchronizationContext.SetSynchronizationContext(
                new ActorCellKeepingSynchronizationContext(InternalCurrentActorCellKeeper.Current));

            _testState.TestActor = testActor;
        }

        private TimeSpan SingleExpectDefaultTimeout { get { return _testState.TestKitSettings.SingleExpectDefault; } }

        public ActorSystem Sys { get { return _testState.System; } }
        public TestKitSettings TestKitSettings { get { return _testState.TestKitSettings; } }
        public IActorRef LastSender { get { return _testState.LastMessage.Sender; } }
        public static Config DefaultConfig { get { return _defaultConfig; } }
        public static Config FullDebugConfig { get { return _fullDebugConfig; } }
        public static TimeSpan Now { get { return TimeSpan.FromTicks(DateTime.UtcNow.Ticks); } }
        public ILoggingAdapter Log { get { return _testState.Log; } }
        public object LastMessage { get { return _testState.LastMessage.Message; } }

        /// <summary>
        /// The default TestActor. The actor can be controlled by sending it 
        /// special control messages, see <see cref="TestKit.TestActor.SetIgnore"/>, 
        /// <see cref="TestKit.TestActor.Watch"/>, <see cref="TestKit.TestActor.Unwatch"/>.
        /// You can also install an <see cref="AutoPilot" /> to drive the actor, see
        /// <see cref="SetAutoPilot"/>. All other messages are forwarded to the queue
        /// and can be retrieved with Receive and the ExpectMsg overloads.
        /// </summary>
        public IActorRef TestActor { get { return _testState.TestActor; } }

        /// <summary>
        /// Filter <see cref="LogEvent"/> sent to the system's <see cref="EventStream"/>.
        /// In order to be able to filter the log the special logger
        /// <see cref="TestEventListener"/> must be installed using the config
        /// <code>akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]</code>
        /// It is installed by default in testkit.
        /// </summary>
        public EventFilterFactory EventFilter { get { return _testState.EventFilterFactory; } }


        /// <summary>
        /// Returns <c>true</c> if messages are available.
        /// </summary>
        /// <value>
        /// <c>true</c> if messages are available; otherwise, <c>false</c>.
        /// </value>
        public bool HasMessages
        {
            get { return _testState.Queue.Count > 0; }
        }

        /// <summary>
        /// Ignore all messages in the test actor for which the given function 
        /// returns <c>true</c>.
        /// </summary>
        /// <param name="shouldIgnoreMessage">Given a message, if the function returns 
        /// <c>true</c> the message will be ignored by <see cref="TestActor"/>.</param>
        public void IgnoreMessages(Func<object, bool> shouldIgnoreMessage)
        {
            _testState.TestActor.Tell(new TestActor.SetIgnore(m => shouldIgnoreMessage(m)));
        }

        /// <summary>Stop ignoring messages in the test actor.</summary>
        public void IgnoreNoMessages()
        {
            _testState.TestActor.Tell(new TestActor.SetIgnore(null));
        }

        /// <summary>
        /// Have the <see cref="TestActor"/> watch an actor and receive 
        /// <see cref="Terminated"/> messages when the actor terminates.
        /// </summary>
        /// <param name="actorToWatch">The actor to watch.</param>
        /// <returns>The actor to watch, i.e. the parameter <paramref name="actorToWatch"/></returns>
        public IActorRef Watch(IActorRef actorToWatch)
        {
            _testState.TestActor.Tell(new TestActor.Watch(actorToWatch));
            return actorToWatch;
        }

        /// <summary>
        /// Have the <see cref="TestActor"/> stop watching an actor.
        /// </summary>
        /// <param name="actorToUnwatch">The actor to unwatch.</param>
        /// <returns>The actor to unwatch, i.e. the parameter <paramref name="actorToUnwatch"/></returns>
        public IActorRef Unwatch(IActorRef actorToUnwatch)
        {
            _testState.TestActor.Tell(new TestActor.Unwatch(actorToUnwatch));
            return actorToUnwatch;
        }

        /// <summary>
        /// Install an <see cref="AutoPilot" /> to drive the <see cref="TestActor" />.
        /// The <see cref="AutoPilot" /> will be run for each received message and can
        /// be used to send or forward messages, etc.
        /// Each invocation must return the AutoPilot for the next round. To reuse the
        /// same <see cref="AutoPilot" /> return <see cref="AutoPilot.KeepRunning" />
        /// </summary>
        /// <param name="pilot">The pilot to install.</param>
        public void SetAutoPilot(AutoPilot pilot)
        {
            _testState.TestActor.Tell(new TestActor.SetAutoPilot(pilot));
        }


        /// <summary>Obtain time remaining for execution of the innermost enclosing `within`
        /// block or missing that it returns the properly dilated default for this
        /// case from settings (key "akka.test.single-expect-default"). <remarks>The returned value is always finite.</remarks>
        /// </summary>
        public TimeSpan RemainingOrDefault
        {
            get { return RemainingOr(Dilated(SingleExpectDefaultTimeout)); }
        }


        /// <summary>
        /// Obtain time remaining for execution of the innermost enclosing <see cref="Within(System.TimeSpan,System.Action)">Within</see>
        /// block or throw an <see cref="InvalidOperationException" /> if no `within` block surrounds this
        /// call. <remarks>The returned value is always finite.</remarks>
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if this was called outside of within</exception>
        public TimeSpan Remaining
        {
            get
            {
                // ReSharper disable once PossibleInvalidOperationException
                if (_testState.End.IsPositiveFinite()) return _testState.End.Value - Now;
                throw new InvalidOperationException(@"Remaining may not be called outside of ""within""");
            }
        }

        ///<summary>
        /// If inside a `within` block obtain time remaining for execution of the innermost enclosing `within`
        /// block; otherwise returns the given duration.
        /// </summary>
        protected TimeSpan RemainingOr(TimeSpan duration)
        {
            if (!_testState.End.HasValue) return duration;
            if (_testState.End.IsInfinite())
                throw new ArgumentException("end cannot be infinite");
            return _testState.End.Value - Now;

        }

        /// <summary>
        /// If <paramref name="duration"/> is finite it is returned after it has been scaled using <see cref="Dilated(TimeSpan)"/>.
        /// If <paramref name="duration"/> is undefined, it returns the remaining time (if within a `within` block) or the properly dilated 
        /// default from settings (key "akka.test.single-expect-default").
        /// If <paramref name="duration"/> is infinite, an <see cref="ArgumentException"/> is thrown.
        /// <remarks>The returned value is always finite.</remarks>
        /// </summary>
        /// <param name="duration">The maximum.</param>
        /// <returns>A finite <see cref="TimeSpan"/> properly dilated</returns>
        /// <exception cref="System.ArgumentException">Thrown if <paramref name="duration"/> is infinite</exception>
        public TimeSpan RemainingOrDilated(TimeSpan? duration)
        {
            if(!duration.HasValue) return RemainingOrDefault;
            if(duration.IsInfinite()) throw new ArgumentException("max duration cannot be infinite");
            return Dilated(duration.Value);
        }


        /// <summary>
        /// Multiplies the duration with the <see cref="Akka.TestKit.TestKitSettings.TestTimeFactor"/>,
        /// i.e. the config value "akka.test.timefactor"
        /// </summary>
        public TimeSpan Dilated(TimeSpan duration)
        {
            if(duration.IsPositiveFinite())
                return new TimeSpan((long)(duration.Ticks * _testState.TestKitSettings.TestTimeFactor));
            //Else: 0 or infinite (negative)
            return duration;
        }


        /// <summary>
        /// If <paramref name="timeout"/> is defined it is returned; otherwise
        /// the config value "akka.test.single-expect-default" is returned.
        /// </summary>
        public TimeSpan GetTimeoutOrDefault(TimeSpan? timeout)
        {
            return timeout.GetValueOrDefault(SingleExpectDefaultTimeout);
        }

        /// <summary>
        /// Shuts down this system.
        /// On failure debug output will be logged about the remaining actors in the system.
        /// If verifySystemShutdown is true, then an exception will be thrown on failure.
        /// </summary>
        /// <param name="duration">Optional. The duration to wait for shutdown. Default is 5 seconds multiplied with the config value "akka.test.timefactor".</param>
        /// <param name="verifySystemShutdown">if set to <c>true</c> an exception will be thrown on failure.</param>
        public virtual void Shutdown(TimeSpan? duration = null, bool verifySystemShutdown = false)
        {
            Shutdown(_testState.System, duration, verifySystemShutdown);
        }

        /// <summary>
        /// Shuts down the specified system.
        /// On failure debug output will be logged about the remaining actors in the system.
        /// If verifySystemShutdown is true, then an exception will be thrown on failure.
        /// </summary>
        /// <param name="system">The system to shutdown.</param>
        /// <param name="duration">The duration to wait for shutdown. Default is 5 seconds multiplied with the config value "akka.test.timefactor"</param>
        /// <param name="verifySystemShutdown">if set to <c>true</c> an exception will be thrown on failure.</param>
        protected virtual void Shutdown(ActorSystem system, TimeSpan? duration = null, bool verifySystemShutdown = false)
        {
            if (system == null) system = _testState.System;

            var durationValue = duration.GetValueOrDefault(Dilated(TimeSpan.FromSeconds(5)).Min(TimeSpan.FromSeconds(10)));

            var wasShutdownDuringWait = system.Terminate().Wait(durationValue);
            if(!wasShutdownDuringWait)
            {
                const string msg = "Failed to stop [{0}] within [{1}] \n{2}";
                if(verifySystemShutdown)
                    throw new TimeoutException(string.Format(msg, system.Name, durationValue, ""));
                //TODO: replace "" with system.PrintTree()
                system.Log.Warning(msg, system.Name, durationValue, ""); //TODO: replace "" with system.PrintTree()
            }
        }

        /// <summary>
        /// Creates a test actor with the specified name. The actor can be controlled by sending it 
        /// special control messages, see <see cref="TestKit.TestActor.SetIgnore"/>, 
        /// <see cref="TestKit.TestActor.Watch"/>, <see cref="TestKit.TestActor.Unwatch"/>,
        /// <see cref="TestKit.TestActor.SetAutoPilot"/>. All other messages are forwarded to the queue
        /// and can be retrieved with Receive and the ExpectMsg overloads.
        /// <para>The default test actor can be retrieved from the <see cref="TestActor"/> property</para>
        /// </summary>
        /// <param name="name">The name of the new actor.</param>
        /// <returns></returns>
        public IActorRef CreateTestActor(string name)
        {
            return CreateTestActor(_testState.System, name);
        }

        private IActorRef CreateTestActor(ActorSystem system, string name)
        {
            var testActorProps = Props.Create(() => new InternalTestActor(new BlockingCollectionTestActorQueue<MessageEnvelope>(_testState.Queue)))
                .WithDispatcher("akka.test.test-actor.dispatcher");
            var testActor = system.ActorOf(testActorProps, name);
            return testActor;
        }


        /// <summary>
        /// Creates a new <see cref="TestProbe" />.
        /// </summary>
        /// <param name="name">Optional: The name of the probe.</param>
        /// <returns></returns>
        public virtual TestProbe CreateTestProbe(string name=null)
        {
            return CreateTestProbe(Sys, name);
        }

        /// <summary>
        /// Creates a new <see cref="TestProbe" />.
        /// </summary>
        /// <param name="system">For multi-actor system tests, you can specify which system the node is for.</param>
        /// <param name="name">Optional: The name of the probe.</param>
        /// <returns></returns>
        public virtual TestProbe CreateTestProbe(ActorSystem system, string name = null)
        {
            return new TestProbe(system, _assertions, name);
        }

        /// <summary>
        /// Creates a Countdown latch wrapper for use in testing.
        /// 
        /// It uses a timeout when waiting and timeouts are specified as durations.
        /// There's a default timeout of 5 seconds and the default count is 1.
        /// Timeouts will always throw an exception.
        /// </summary>
        /// <param name="count">Optional. The count. Default: 1</param>
        /// <returns>A new <see cref="TestLatch"/></returns>
        public virtual TestLatch CreateTestLatch(int count=1)
        {
            return new TestLatch(Dilated, count, _testState.TestKitSettings.DefaultTimeout);
        }

        /// <summary>
        /// Wraps a <see cref="Barrier"/> for use in testing.
        /// It always uses a timeout when waiting.
        /// Timeouts will always throw an exception. The default timeout is 5 seconds.
        /// </summary>
        public TestBarrier CreateTestBarrier(int count)
        {
            return new TestBarrier(this, count, _testState.TestKitSettings.DefaultTimeout);
        }

    }

}


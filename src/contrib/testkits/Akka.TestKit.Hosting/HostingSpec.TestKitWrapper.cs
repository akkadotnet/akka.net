// -----------------------------------------------------------------------
//  <copyright file="HostingSpec.TestKitWrapper.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Xunit.Abstractions;

namespace Akka.TestKit.Hosting
{
    public abstract partial class HostingSpec
    {
        private ActorSystem _sys;
        /// <summary>
        /// The <see cref="ActorSystem"/> that is recreated and used for each test.
        /// </summary>
        protected ActorSystem Sys
        {
            get
            {
                AssertNotNull(_sys);
                return _sys;
            }
        }

        /// <summary>
        /// The settings for the testkit.
        /// </summary>
        public TestKitSettings TestKitSettings => TestKit.TestKitSettings;

        /// <summary>
        /// The last <see cref="IActorRef"/> to send a message to the <see cref="TestActor"/>.
        /// </summary>
        public IActorRef LastSender => TestKit.LastSender;

        /// <summary>
        /// The default TestKit configuration.
        /// </summary>
        public static Config DefaultConfig => TestKitBase.DefaultConfig;

        /// <summary>
        /// A full debugging configuration with all log settings enabled.
        /// </summary>
        public static Config FullDebugConfig => TestKitBase.FullDebugConfig;

        /// <summary>
        /// The current time.
        /// </summary>
        public static TimeSpan Now => TimeSpan.FromTicks(DateTime.UtcNow.Ticks);

        /// <summary>
        /// The built-in <see cref="ILoggingAdapter"/> used by <see cref="Sys"/>.
        /// </summary>
        public ILoggingAdapter Log => TestKit.Log;

        /// <summary>
        /// The last message received by the <see cref="TestActor"/>.
        /// </summary>
        public object LastMessage => TestKit.LastMessage;

        /// <summary>
        /// The default TestActor. The actor can be controlled by sending it 
        /// special control messages, see <see cref="Akka.TestKit.TestActor.SetIgnore"/>, 
        /// <see cref="Akka.TestKit.TestActor.Watch"/>, <see cref="Akka.TestKit.TestActor.Unwatch"/>.
        /// You can also install an <see cref="AutoPilot" /> to drive the actor, see
        /// <see cref="SetAutoPilot"/>. All other messages are forwarded to the queue
        /// and can be retrieved with Receive and the ExpectMsg overloads.
        /// </summary>
        public IActorRef TestActor => TestKit.TestActor;

        /// <summary>
        /// Filter <see cref="LogEvent"/> sent to the system's <see cref="EventStream"/>.
        /// In order to be able to filter the log the special logger
        /// <see cref="TestEventListener"/> must be installed using the config
        /// <code>akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]</code>
        /// It is installed by default in testkit.
        /// </summary>
        public EventFilterFactory EventFilter => TestKit.EventFilter;

        /// <summary>
        /// Creates a new event filter for the specified actor system.
        /// </summary>
        /// <param name="system">Actor system.</param>
        /// <returns>A new instance of <see cref="EventFilterFactory"/>.</returns>
        public EventFilterFactory CreateEventFilter(ActorSystem system)
            => new EventFilterFactory(TestKit, system);

        /// <summary>
        /// Returns <c>true</c> if messages are available.
        /// </summary>
        /// <value>
        /// <c>true</c> if messages are available; otherwise, <c>false</c>.
        /// </value>
        public bool HasMessages => TestKit.HasMessages;

        /// <summary>
        /// Ignore all messages in the test actor for which the given function 
        /// returns <c>true</c>.
        /// </summary>
        /// <param name="shouldIgnoreMessage">Given a message, if the function returns 
        /// <c>true</c> the message will be ignored by <see cref="TestActor"/>.</param>
        public void IgnoreMessages(Func<object, bool> shouldIgnoreMessage)
            => TestKit.IgnoreMessages(shouldIgnoreMessage);

        /// <summary>
        /// Ignore all messages in the test actor of the given TMsg type for which the given function 
        /// returns <c>true</c>.
        /// </summary>
        /// <typeparam name="TMsg">The type of the message to ignore.</typeparam>
        /// <param name="shouldIgnoreMessage">Given a message, if the function returns 
        /// <c>true</c> the message will be ignored by <see cref="TestActor"/>.</param>
        public void IgnoreMessages<TMsg>(Func<TMsg, bool> shouldIgnoreMessage) 
            => TestKit.IgnoreMessages(shouldIgnoreMessage);

        /// <summary>
        /// Ignore all messages in the test actor of the given TMsg type.
        /// </summary>
        /// <typeparam name="TMsg">The type of the message to ignore.</typeparam>
        public void IgnoreMessages<TMsg>() => TestKit.IgnoreMessages<TMsg>();

        /// <summary>Stop ignoring messages in the test actor.</summary>
        public void IgnoreNoMessages() => TestKit.IgnoreNoMessages();

        /// <summary>
        /// Have the <see cref="TestActor"/> watch an actor and receive 
        /// <see cref="Terminated"/> messages when the actor terminates.
        /// </summary>
        /// <param name="actorToWatch">The actor to watch.</param>
        /// <returns>The actor to watch, i.e. the parameter <paramref name="actorToWatch"/></returns>
        public IActorRef Watch(IActorRef actorToWatch) => TestKit.Watch(actorToWatch);

        /// <summary>
        /// Have the <see cref="TestActor"/> stop watching an actor.
        /// </summary>
        /// <param name="actorToUnwatch">The actor to unwatch.</param>
        /// <returns>The actor to unwatch, i.e. the parameter <paramref name="actorToUnwatch"/></returns>
        public IActorRef Unwatch(IActorRef actorToUnwatch) => TestKit.Unwatch(actorToUnwatch);

        /// <summary>
        /// <para>
        /// Install an <see cref="AutoPilot" /> to drive the <see cref="TestActor" />.
        /// The <see cref="AutoPilot" /> will be run for each received message and can
        /// be used to send or forward messages, etc.
        /// </para>
        /// <para>
        /// Each invocation must return the AutoPilot for the next round. To reuse the
        /// same <see cref="AutoPilot" /> return <see cref="AutoPilot.KeepRunning" />.
        /// </para>
        /// </summary>
        /// <param name="pilot">The pilot to install.</param>
        public void SetAutoPilot(AutoPilot pilot) => TestKit.SetAutoPilot(pilot);

        /// <summary>
        /// <para>
        /// Retrieves the time remaining for execution of the innermost enclosing
        /// <see cref="Within(TimeSpan, Action, TimeSpan?, CancellationToken)">Within</see> block.
        /// If missing that, then it returns the properly dilated default for this
        /// case from settings (key: "akka.test.single-expect-default").
        /// </para>
        /// <remarks>The returned value is always finite.</remarks>
        /// </summary>
        public TimeSpan RemainingOrDefault => TestKit.RemainingOrDefault;

        /// <summary>
        /// <para>
        /// Retrieves the time remaining for execution of the innermost enclosing
        /// <see cref="Within(TimeSpan, Action, TimeSpan?, CancellationToken)">Within</see> block.
        /// </para>
        /// <remarks>The returned value is always finite.</remarks>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown when called from outside of `within`.
        /// </exception>
        public TimeSpan Remaining => TestKit.Remaining;

        ///<summary>
        /// If inside a `within` block obtain time remaining for execution of the innermost enclosing `within`
        /// block; otherwise returns the given duration.
        /// </summary>
        /// <param name="duration">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        protected TimeSpan RemainingOr(TimeSpan duration)
        {
            AssertNotNull(_testKit);
            return _testKit.RemainingOr(duration);
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
        /// <exception cref="ArgumentException">Thrown if <paramref name="duration"/> is infinite</exception>
        public TimeSpan RemainingOrDilated(TimeSpan? duration) => TestKit.RemainingOrDilated(duration);


        /// <summary>
        /// Multiplies the duration with the <see cref="Akka.TestKit.TestKitSettings.TestTimeFactor"/>,
        /// i.e. the config value "akka.test.timefactor"
        /// </summary>
        /// <param name="duration">TBD</param>
        /// <returns>TBD</returns>
        public TimeSpan Dilated(TimeSpan duration) => TestKit.Dilated(duration);


        /// <summary>
        /// If <paramref name="timeout"/> is defined it is returned; otherwise
        /// the config value "akka.test.single-expect-default" is returned.
        /// </summary>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public TimeSpan GetTimeoutOrDefault(TimeSpan? timeout) => TestKit.GetTimeoutOrDefault(timeout);

        /// <summary>
        /// Shuts down this system.
        /// On failure debug output will be logged about the remaining actors in the system.
        /// If verifySystemShutdown is true, then an exception will be thrown on failure.
        /// </summary>
        /// <param name="duration">Optional. The duration to wait for shutdown. Default is 5 seconds multiplied with the config value "akka.test.timefactor".</param>
        /// <param name="verifySystemShutdown">if set to <c>true</c> an exception will be thrown on failure.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <exception cref="TimeoutException">TBD</exception>
        public virtual void Shutdown(
            TimeSpan? duration = null,
            bool verifySystemShutdown = false,
            CancellationToken cancellationToken = default)
            => TestKit.Shutdown(duration, verifySystemShutdown, cancellationToken);

        /// <summary>
        /// Shuts down this system.
        /// On failure debug output will be logged about the remaining actors in the system.
        /// If verifySystemShutdown is true, then an exception will be thrown on failure.
        /// </summary>
        /// <param name="duration">Optional. The duration to wait for shutdown. Default is 5 seconds multiplied with the config value "akka.test.timefactor".</param>
        /// <param name="verifySystemShutdown">if set to <c>true</c> an exception will be thrown on failure.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <exception cref="TimeoutException">TBD</exception>
        public virtual Task ShutdownAsync(
            TimeSpan? duration = null,
            bool verifySystemShutdown = false,
            CancellationToken cancellationToken = default)
            => TestKit.ShutdownAsync(duration, verifySystemShutdown, cancellationToken);

        /// <summary>
        /// Shuts down the specified system.
        /// On failure debug output will be logged about the remaining actors in the system.
        /// If verifySystemShutdown is true, then an exception will be thrown on failure.
        /// </summary>
        /// <param name="system">The system to shutdown.</param>
        /// <param name="duration">The duration to wait for shutdown. Default is 5 seconds multiplied with the config value "akka.test.timefactor"</param>
        /// <param name="verifySystemShutdown">if set to <c>true</c> an exception will be thrown on failure.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <exception cref="TimeoutException">TBD</exception>
        protected virtual void Shutdown(
            ActorSystem system,
            TimeSpan? duration = null,
            bool verifySystemShutdown = false,
            CancellationToken cancellationToken = default)
        {
            AssertNotNull(_testKit);
            _testKit.Shutdown(system, duration, verifySystemShutdown, cancellationToken);
        }

        /// <summary>
        /// Shuts down the specified system.
        /// On failure debug output will be logged about the remaining actors in the system.
        /// If verifySystemShutdown is true, then an exception will be thrown on failure.
        /// </summary>
        /// <param name="system">The system to shutdown.</param>
        /// <param name="duration">The duration to wait for shutdown. Default is 5 seconds multiplied with the config value "akka.test.timefactor"</param>
        /// <param name="verifySystemShutdown">if set to <c>true</c> an exception will be thrown on failure.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <exception cref="TimeoutException">TBD</exception>
        protected virtual Task ShutdownAsync(
            ActorSystem system,
            TimeSpan? duration = null,
            bool verifySystemShutdown = false,
            CancellationToken cancellationToken = default) 
        {
            AssertNotNull(_testKit);
            return _testKit.ShutdownAsync(system, duration, verifySystemShutdown, cancellationToken);
        }

        /// <summary>
        /// Spawns an actor as a child of this test actor, and returns the child's IActorRef
        /// </summary>
        /// <param name="props">Child actor props</param>
        /// <param name="name">Child actor name</param>
        /// <param name="supervisorStrategy">Supervisor strategy for the child actor</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <returns></returns>
        public IActorRef ChildActorOf(
            Props props,
            string name,
            SupervisorStrategy supervisorStrategy,
            CancellationToken cancellationToken = default)
            => TestKit.ChildActorOf(props, name, supervisorStrategy, cancellationToken);

        /// <summary>
        /// Spawns an actor as a child of this test actor, and returns the child's IActorRef
        /// </summary>
        /// <param name="props">Child actor props</param>
        /// <param name="name">Child actor name</param>
        /// <param name="supervisorStrategy">Supervisor strategy for the child actor</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <returns></returns>
        public Task<IActorRef> ChildActorOfAsync(
            Props props, 
            string name,
            SupervisorStrategy supervisorStrategy,
            CancellationToken cancellationToken = default)
            => TestKit.ChildActorOfAsync(props, name, supervisorStrategy, cancellationToken);

        /// <summary>
        /// Spawns an actor as a child of this test actor with an auto-generated name, and returns the child's ActorRef.
        /// </summary>
        /// <param name="props">Child actor props</param>
        /// <param name="supervisorStrategy">Supervisor strategy for the child actor</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <returns></returns>
        public IActorRef ChildActorOf(
            Props props, SupervisorStrategy supervisorStrategy, CancellationToken cancellationToken = default)
            => TestKit.ChildActorOf(props, supervisorStrategy, cancellationToken);

        /// <summary>
        /// Spawns an actor as a child of this test actor with an auto-generated name, and returns the child's ActorRef.
        /// </summary>
        /// <param name="props">Child actor props</param>
        /// <param name="supervisorStrategy">Supervisor strategy for the child actor</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <returns></returns>
        public Task<IActorRef> ChildActorOfAsync(
            Props props, SupervisorStrategy supervisorStrategy, CancellationToken cancellationToken = default)
            => TestKit.ChildActorOfAsync(props, supervisorStrategy, cancellationToken);

        /// <summary>
        /// Spawns an actor as a child of this test actor with a stopping supervisor strategy, and returns the child's ActorRef.
        /// </summary>
        /// <param name="props">Child actor props</param>
        /// <param name="name">Child actor name</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <returns></returns>
        public IActorRef ChildActorOf(Props props, string name, CancellationToken cancellationToken = default)
            => TestKit.ChildActorOf(props, name, cancellationToken);
        
        /// <summary>
        /// Spawns an actor as a child of this test actor with a stopping supervisor strategy, and returns the child's ActorRef.
        /// </summary>
        /// <param name="props">Child actor props</param>
        /// <param name="name">Child actor name</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <returns></returns>
        public Task<IActorRef> ChildActorOfAsync(
            Props props, string name, CancellationToken cancellationToken = default)
            => TestKit.ChildActorOfAsync(props, name, cancellationToken);
        
        /// <summary>
        /// Spawns an actor as a child of this test actor with an auto-generated name and stopping supervisor strategy, returning the child's ActorRef.
        /// </summary>
        /// <param name="props">Child actor props</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <returns></returns>
        public IActorRef ChildActorOf(Props props, CancellationToken cancellationToken = default)
            => TestKit.ChildActorOf(props, cancellationToken);

        /// <summary>
        /// Spawns an actor as a child of this test actor with an auto-generated name and stopping supervisor strategy, returning the child's ActorRef.
        /// </summary>
        /// <param name="props">Child actor props</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to cancel the operation</param>
        /// <returns></returns>
        public Task<IActorRef> ChildActorOfAsync(Props props, CancellationToken cancellationToken = default)
            => TestKit.ChildActorOfAsync(props, cancellationToken);

        /// <summary>
        /// Creates a test actor with the specified name. The actor can be controlled by sending it 
        /// special control messages, see <see cref="Akka.TestKit.TestActor.SetIgnore"/>, 
        /// <see cref="Akka.TestKit.TestActor.Watch"/>, <see cref="Akka.TestKit.TestActor.Unwatch"/>,
        /// <see cref="Akka.TestKit.TestActor.SetAutoPilot"/>. All other messages are forwarded to the queue
        /// and can be retrieved with Receive and the ExpectMsg overloads.
        /// <para>The default test actor can be retrieved from the <see cref="TestActor"/> property</para>
        /// </summary>
        /// <param name="name">The name of the new actor.</param>
        /// <returns>TBD</returns>
        public IActorRef CreateTestActor(string name)
            => TestKit.CreateTestActor(name);

        /// <summary>
        /// Creates a new <see cref="TestProbe" />.
        /// </summary>
        /// <param name="name">Optional: The name of the probe.</param>
        /// <returns>A new <see cref="TestProbe"/> instance.</returns>
        public virtual TestProbe CreateTestProbe(string name = null)
            => TestKit.CreateTestProbe(name);
        
        /// <summary>
        /// Creates a new <see cref="TestProbe" />.
        /// </summary>
        /// <param name="system">For multi-actor system tests, you can specify which system the node is for.</param>
        /// <param name="name">Optional: The name of the probe.</param>
        /// <returns>TBD</returns>
        public virtual TestProbe CreateTestProbe(ActorSystem system, string name = null)
            => TestKit.CreateTestProbe(system, name);

        /// <summary>
        /// Creates a Countdown latch wrapper for use in testing.
        /// 
        /// It uses a timeout when waiting and timeouts are specified as durations.
        /// There's a default timeout of 5 seconds and the default count is 1.
        /// Timeouts will always throw an exception.
        /// </summary>
        /// <param name="count">Optional. The count. Default: 1</param>
        /// <returns>A new <see cref="TestLatch"/></returns>
        public virtual TestLatch CreateTestLatch(int count = 1)
            => TestKit.CreateTestLatch(count);

        /// <summary>
        /// Wraps a <see cref="Barrier"/> for use in testing.
        /// It always uses a timeout when waiting.
        /// Timeouts will always throw an exception. The default timeout is 5 seconds.
        /// </summary>
        /// <param name="count">TBD</param>
        /// <returns>TBD</returns>
        public TestBarrier CreateTestBarrier(int count)
            => TestKit.CreateTestBarrier(count);        
    }

    internal class TestKitBaseUnWrapper : Xunit2.TestKit
    {
        public TestKitBaseUnWrapper(ActorSystem system = null, ITestOutputHelper output = null) : base(system, output)
        {
        }

        public TestKitBaseUnWrapper(ActorSystemSetup config, string actorSystemName = null, ITestOutputHelper output = null) : base(config, actorSystemName, output)
        {
        }

        public TestKitBaseUnWrapper(Config config, string actorSystemName = null, ITestOutputHelper output = null) : base(config, actorSystemName, output)
        {
        }

        public TestKitBaseUnWrapper(string config, ITestOutputHelper output = null) : base(config, output)
        {
        }

        public new void Shutdown(
            ActorSystem system,
            TimeSpan? duration = null,
            bool verifySystemShutdown = false,
            CancellationToken cancellationToken = default)
            => base.Shutdown(system, duration, verifySystemShutdown, cancellationToken);

        public new Task ShutdownAsync(
            ActorSystem system,
            TimeSpan? duration = null,
            bool verifySystemShutdown = false,
            CancellationToken cancellationToken = default)
            => base.ShutdownAsync(system, duration, verifySystemShutdown, cancellationToken);

        public new TimeSpan RemainingOr(TimeSpan duration)
            => base.RemainingOr(duration);
    }
}
//-----------------------------------------------------------------------
// <copyright file="DiResolverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.DI.Core;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.TestKit.Xunit2;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;

namespace Akka.DI.TestKit
{
    public abstract class DiResolverSpec : TestKitBase, IDisposable
    {
        #region DI classes

        class GetCallCount { }

        class DiPerRequestActor : ReceiveActor
        {
            private readonly IDiTest _di;

            public DiPerRequestActor(IDiTest di)
            {
                _di = di;

                Receive<GetCallCount>(count => Sender.Tell(_di.CallCount));
                ReceiveAny(o => _di.Call());
            }
        }

        class DiSingletonActor : ReceiveActor
        {
            private readonly IDiSingleton _di;

            public DiSingletonActor(IDiSingleton di)
            {
                _di = di;

                Receive<GetCallCount>(count => Sender.Tell(_di.CallCount));
                ReceiveAny(o => _di.Call());
            }
        }

        class DiParentActor : ReceiveActor
        {
            private IActorRef _child;

            public class GetChild { }

            public DiParentActor()
            {
                Receive<GetChild>(c => Sender.Tell(_child));
            }

            protected override void PreStart()
            {
                var childProps = Context.DI().Props<DiPerRequestActor>();
                _child = Context.ActorOf(childProps);
            }
        }

        class DisposableActor : ReceiveActor
        {
            public class Restart { }

            public class GetHashCode { }

            private readonly IDiDisposable _di;

            public DisposableActor(IDiDisposable di)
            {
                _di = di;

                Receive<GetHashCode>(g => Sender.Tell(_di.GetHashCode()));
                Receive<Restart>(r => ForceRestart());
            }

            private void ForceRestart()
            {
                throw new Exception("RESTART ME!");
            }
        }

        interface IDiTest
        {
            int CallCount { get; }

            void Call();
        }

        class ConcreteDiTest : IDiTest
        {
            public int CallCount { get; private set; }

            public void Call()
            {
                CallCount = CallCount + 1;
            }
        }

        interface IDiSingleton
        {
            int CallCount { get; }

            void Call();
        }

        class ConcreteDiSingleton : IDiSingleton
        {
            AtomicCounter _counter = new AtomicCounter(0);
            public int CallCount { get { return _counter.Current; }}

            public void Call()
            {
                _counter.GetAndIncrement();
            }
        }

        interface IDiDisposable : IDisposable
        {
            bool WasDisposed { get; }
        }

        class ConcreteDiDisposable : IDiDisposable
        {
            private readonly AtomicCounter _disposeCounter;

            public ConcreteDiDisposable(AtomicCounter disposeCounter)
            {
                _disposeCounter = disposeCounter;
            }

            public void Dispose()
            {
                GC.SuppressFinalize(this);
                Dispose(true);
            }

            private void Dispose(bool isDisposing)
            {
                if (!WasDisposed)
                {
                    WasDisposed = true;
                    _disposeCounter.Next();
                }
            }


            public bool WasDisposed { get; private set; }
        }

        private static readonly IDiSingleton Single = new ConcreteDiSingleton();

        class UnboundedStashActor : BlackHoleActor, IWithUnboundedStash
        {
            private readonly IDiTest _di;

            public UnboundedStashActor(IDiTest di)
            {
                _di = di;
            }

            public IStash Stash { get; set; }
        }

        class BoundedStashActor : BlackHoleActor, IWithBoundedStash
        {
            private readonly IDiTest _di;

            public BoundedStashActor(IDiTest di)
            {
                _di = di;
            }

            public IStash Stash { get; set; }
        }

        #endregion

        private static readonly AtomicCounter Counter = new AtomicCounter(0);
        private string _pid;

        protected int ActorInstanceId = 1;

        public string Pid { get { return _pid; } }

        private AtomicCounter _disposeCounter = new AtomicCounter(0);

        protected DiResolverSpec(Config config = null, string actorSystemName = null, string testActorName = null)
            : base(new XunitAssertions(), config, actorSystemName, testActorName)
        {
            _pid = "p-" + Counter.IncrementAndGet();
// ReSharper disable once DoNotCallOverridableMethodsInConstructor
            var resolver = ConfigureDependencyResolver(Sys);
        }

        /// <summary>
        /// Creates and configures a brand new <see cref="IDependencyResolver"/>.
        /// </summary>
        /// <returns>A new <see cref="IDependencyResolver"/> configured using the provided DI generator.</returns>
        protected IDependencyResolver ConfigureDependencyResolver(ActorSystem system)
        {
            var container = NewDiContainer();
            Bind<IDiTest>(container, () => new ConcreteDiTest());
            Bind<IDiSingleton>(container, () => Single);
            Bind<IDiDisposable>(container, () => new ConcreteDiDisposable(_disposeCounter));
            Bind<DisposableActor>(container);
            Bind<DiPerRequestActor>(container);
            Bind<DiSingletonActor>(container);
            Bind<BoundedStashActor>(container);
            Bind<UnboundedStashActor>(container);
            return NewDependencyResolver(container, system);
        }

        #region Abstract methods

        /// <summary>
        /// Create a new instance of the Dependency Injection container that we're creating.
        /// </summary>
        /// <returns>A new DI container instance.</returns>
        protected abstract object NewDiContainer();

        /// <summary>
        /// Create a new <see cref="IDependencyResolver"/> instance that we're going to use
        /// in the context of all of our tests.
        /// </summary>
        /// <returns>An <see cref="IDependencyResolver"/> instance.</returns>
        protected abstract IDependencyResolver NewDependencyResolver(object diContainer, ActorSystem system);

        /// <summary>
        /// Create a binding for type <typeparamref name="T"/> on the provided DI container.
        /// </summary>
        /// <typeparam name="T">The type we're binding onto the DI container.</typeparam>
        /// <param name="diContainer">The DI container.</param>
        /// <param name="generator">A generator function that yields new objects of type <typeparamref name="T"/>.</param>
        protected abstract void Bind<T>(object diContainer, Func<T> generator);

        /// <summary>
        /// Create a binding for type <typeparamref name="T"/> on the provided DI container.
        /// 
        /// Used for DI frameworks that require the DI target to be registered as well
        /// as the injected components.
        /// </summary>
        /// <typeparam name="T">The type we're binding onto the DI container.</typeparam>
        /// <param name="diContainer">The DI container.</param>
        protected abstract void Bind<T>(object diContainer);

        #endregion

        #region Tests

        [Fact]
        public void DependencyResolver_should_inject_new_instances_into_DiPerRequestActor()
        {
            var diActorProps = Sys.DI().Props<DiPerRequestActor>();
            var diActor1 = Sys.ActorOf(diActorProps);
            var diActor2 = Sys.ActorOf(diActorProps);

            diActor1.Tell("increment 1");
            diActor1.Tell("increment 2");
            diActor2.Tell("increment 1");

            diActor1.Tell(new GetCallCount());
            Assert.Equal(2, ExpectMsg<int>());

            diActor2.Tell(new GetCallCount());
            Assert.Equal(1, ExpectMsg<int>());
        }

        [Fact]
        public void DependencyResolver_should_inject_new_instances_on_Restart()
        {
            var disposableActorProps = Sys.DI().Props<DisposableActor>();
            var disposableActor = Sys.ActorOf(disposableActorProps);

            disposableActor.Tell(new DisposableActor.GetHashCode());
            var originalHashCode = ExpectMsg<int>();
            disposableActor.Tell(new DisposableActor.Restart());
            disposableActor.Tell(new DisposableActor.GetHashCode());
            var nextHashCode = ExpectMsg<int>();
            Assert.NotEqual(originalHashCode, nextHashCode);
        }

        [Fact]
        public void DependencyResolver_should_inject_same_instance_into_DiSingletonActor()
        {
            var diActorProps = Sys.DI().Props<DiSingletonActor>();
            var diActor1 = Sys.ActorOf(diActorProps);
            var diActor2 = Sys.ActorOf(diActorProps);

            diActor1.Tell("increment 1");
            diActor1.Tell("increment 2");
            diActor2.Tell("increment 1");

            diActor1.Tell(new Identify(null));
            diActor2.Tell(new Identify(null));
            ExpectMsg<ActorIdentity>();
            ExpectMsg<ActorIdentity>();

            diActor1.Tell(new GetCallCount());
            Assert.Equal(3, ExpectMsg<int>());

            diActor2.Tell(new GetCallCount());
            Assert.Equal(3, ExpectMsg<int>());
        }

        [Fact]
        public void DependencyResolver_should_inject_instances_into_DiChildActor()
        {
            var diParent = Sys.ActorOf(Props.Create<DiParentActor>());
            diParent.Tell(new DiParentActor.GetChild());
            var child = ExpectMsg<IActorRef>();

            child.Tell("increment 1");
            child.Tell("increment 2");

            child.Tell(new GetCallCount());
            Assert.Equal(2, ExpectMsg<int>());
        }

        [Fact]
        public void DependencyResolver_should_inject_into_normal_mailbox_Actor()
        {
            var stashActorProps = Sys.DI().Props<DiPerRequestActor>();
            var stashActor = Sys.ActorOf(stashActorProps);

            var internalRef = (RepointableActorRef)stashActor;
            AwaitCondition(() => internalRef.IsStarted);

            Assert.IsType<UnboundedMessageQueue>(internalRef.Underlying.AsInstanceOf<ActorCell>().Mailbox.MessageQueue);
        }

        [Fact]
        public void DependencyResolver_should_inject_into_UnboundedStash_Actor()
        {
            var stashActorProps = Sys.DI().Props<UnboundedStashActor>();
            var stashActor = Sys.ActorOf(stashActorProps);

            var internalRef = (RepointableActorRef)stashActor;
            AwaitCondition(() => internalRef.IsStarted);

            Assert.IsType<UnboundedDequeMessageQueue>(internalRef.Underlying.AsInstanceOf<ActorCell>().Mailbox.MessageQueue);
        }

        [Fact]
        public void DependencyResolver_should_inject_into_BoundedStash_Actor()
        {
            var stashActorProps = Sys.DI().Props<BoundedStashActor>();
            var stashActor = Sys.ActorOf(stashActorProps);

            var internalRef = (RepointableActorRef)stashActor;
            AwaitCondition(() => internalRef.IsStarted);

            Assert.IsType<BoundedDequeMessageQueue>(internalRef.Underlying.AsInstanceOf<ActorCell>().Mailbox.MessageQueue);
        }

        [Fact]
        public void DependencyResolver_should_dispose_IDisposable_instances_on_Actor_Termination()
        {
            var disposableActorProps = Sys.DI().Props<DisposableActor>();
            var disposableActor = Sys.ActorOf(disposableActorProps);

            var currentDisposeCounter = _disposeCounter.Current;
            Assert.True(disposableActor.GracefulStop(TimeSpan.FromSeconds(1)).Result);
            AwaitAssert(() => Assert.True(currentDisposeCounter + 1 == _disposeCounter.Current), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));
        }

        [Fact]
        public void DependencyResolver_should_dispose_IDisposable_instances_on_Actor_Restart()
        {
            var disposableActorProps = Sys.DI().Props<DisposableActor>();
            var disposableActor = Sys.ActorOf(disposableActorProps);

            var currentDisposeCounter = _disposeCounter.Current;
            disposableActor.Tell(new DisposableActor.Restart());
            AwaitAssert(() => Assert.True(currentDisposeCounter + 1 == _disposeCounter.Current), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));
        }

        #endregion

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        /// <param name="disposing">if set to <c>true</c> the method has been called directly or indirectly by a 
        /// user's code. Managed and unmanaged resources will be disposed.<br />
        /// if set to <c>false</c> the method has been called by the runtime from inside the finalizer and only 
        /// unmanaged resources can be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                Shutdown();
        }
    }
}

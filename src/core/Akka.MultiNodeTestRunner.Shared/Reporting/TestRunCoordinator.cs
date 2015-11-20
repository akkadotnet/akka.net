//-----------------------------------------------------------------------
// <copyright file="TestRunCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Sinks;

namespace Akka.MultiNodeTestRunner.Shared.Reporting
{
    /// <summary>
    /// Actor responsible for organizing all of the data for each test run
    /// </summary>
    public class TestRunCoordinator : ReceiveActor, IWithUnboundedStash
    {
        #region Internal message classes

        /// <summary>
        /// Message used to request the current <see cref="TestRunData"/> value.
        /// </summary>
        public class RequestTestRunState { }

        /// <summary>
        /// Signals that we need to publish all <see cref="FactData"/> messages to the <see cref="Subscriber"/>
        /// </summary>
        public class SubscribeFactCompletionMessages
        {
            public SubscribeFactCompletionMessages(IActorRef subscriber)
            {
                Subscriber = subscriber;
            }

            public IActorRef Subscriber { get; private set; }
        }

        /// <summary>
        /// Signals that <see cref="Subscriber"/> no longer wants to receive <see cref="FactData"/> messages
        /// </summary>
        public class UnsubscribeFactCompletionMessages
        {
            public UnsubscribeFactCompletionMessages(IActorRef subscriber)
            {
                Subscriber = subscriber;
            }


            public IActorRef Subscriber { get; private set; }
        }

        #endregion

        /// <summary>
        /// Default constructor which uses <see cref="DateTime.UtcNow"/> as the time for <see cref="TestRunStarted"/>.
        /// </summary>
        public TestRunCoordinator() : this(DateTime.UtcNow) { }

        public TestRunCoordinator(DateTime testRunStarted)
        {
            TestRunStarted = testRunStarted;
            TestRunData = new TestRunTree(testRunStarted.Ticks);
            Subscribers = new List<IActorRef>();
            this.RunningTests();
        }

        #region Internal fields and Properties

        protected readonly DateTime TestRunStarted;

        protected IActorRef _currentSpecRunActor;

        /// <summary>
        /// Automatically set when <see cref="EndTestRun"/> is sent to this actor.
        /// </summary>
        protected DateTime? TestRunCompleted { get; private set; }

        /// <summary>
        /// The amount of time elapsed for this test run
        /// </summary>
        protected TimeSpan TestRunElapsed
        {
            get
            {
                return TestRunStarted - (TestRunCompleted.HasValue ? TestRunCompleted.Value : DateTime.UtcNow);
            }
        }

        /// <summary>
        /// Contains the entire tree of information needed to process results of a full test run.
        /// </summary>
        protected TestRunTree TestRunData;

        /// <summary>
        /// All of the subscribers who wish to receive <see cref="FactData"/> notifications
        /// </summary>
        protected List<IActorRef> Subscribers;

        #endregion

        #region Message-handling

        private void AwaitingShutdown()
        {
            this.Receive<FactData>(
                data =>
                    {
                        this.ReceiveFactData(data);
                        this.Shutdown();
                    });
        }

        private void RunningTests()
        {
            this.Receive<MultiNodeMessage>(
                message =>
                    {
                        if (this._currentSpecRunActor == null)
                        {
                            return;
                        }
                        this._currentSpecRunActor.Forward(message);
                    });
            this.Receive<BeginNewSpec>(spec => this.ReceiveBeginSpecRun(spec));
            this.Receive<EndSpec>(
                spec =>
                    {
                        this.Become(EndingSpec);
                        this.ReceiveEndSpecRun(spec);
                    });
            this.Receive<RequestTestRunState>(
                state => this.Sender.Tell(this.TestRunData.Copy(TestRunPassed(this.TestRunData))));
            this.Receive<SubscribeFactCompletionMessages>(messages => this.AddSubscriber(messages));
            this.Receive<UnsubscribeFactCompletionMessages>(messages => this.RemoveSubscriber(messages));
            this.Receive<EndTestRun>(
                run =>
                    {
                        //clean up the current spec, if it hasn't been done already
                        if (this._currentSpecRunActor != null)
                        {
                            this.Become(this.AwaitingShutdown);

                            this.ReceiveEndSpecRun(new EndSpec());

                            return;
                        }

                        this.Shutdown();
                    });
            this.Receive<FactData>(data => this.ReceiveFactData(data));
        }

        private void EndingSpec()
        {
            this.Receive<MultiNodeMessage>(_ => Stash.Stash());
            this.Receive<BeginNewSpec>(_ => Stash.Stash());
            this.Receive<EndSpec>(_ => Stash.Stash());
            this.Receive<RequestTestRunState>(_ => Stash.Stash());
            this.Receive<SubscribeFactCompletionMessages>(_ => Stash.Stash());
            this.Receive<UnsubscribeFactCompletionMessages>(_ => Stash.Stash());
            this.Receive<EndTestRun>(_ => Stash.Stash());

            this.Receive<FactData>(
                data =>
                    {
                        this.ReceiveFactData(data);
                        this.Become(this.RunningTests);
                        Stash.UnstashAll();
                    });
        }

        private void Shutdown()
        {
            //Mark the test run as finished
            TestRunData.Complete();

            //Deliver the final copy of the TestRunData
            Sender.Tell(TestRunData.Copy());

            //shutdown
            Context.Stop(Self);
        }
        private void ReceiveFactData(FactData data)
        {
                    this.TestRunData.AddSpec(data);
                    foreach (var subscriber in this.Subscribers)
                    {
                        subscriber.Tell(data);
                    }
                    this._currentSpecRunActor = null;
        }

        private void RemoveSubscriber(UnsubscribeFactCompletionMessages unsubscribe)
        {
            Subscribers.Remove(unsubscribe.Subscriber);
        }

        private void AddSubscriber(SubscribeFactCompletionMessages subscription)
        {
            Subscribers.Add(subscription.Subscriber);
        }

        private void ReceiveBeginSpecRun(BeginNewSpec spec)
        {
            if (_currentSpecRunActor != null) throw new InvalidOperationException("EndSpec has not been called for previous run yet. Cannot begin next run.");

            //Create the new spec run actor
            _currentSpecRunActor =
                Context.ActorOf(
                    Props.Create(() => new SpecRunCoordinator(spec.ClassName, spec.MethodName, spec.Nodes)));
        }

        private void ReceiveEndSpecRun(EndSpec spec)
        {
           _currentSpecRunActor.Ask<FactData>(spec, TimeSpan.FromSeconds(2))
                .PipeTo(Self, Sender);
        }

        private static bool TestRunPassed(TestRunTree tree)
        {
            return tree.Specs.All(x => x.Passed.HasValue && x.Passed.Value);
        }

        #endregion

        public IStash Stash { get; set; }
    }
}


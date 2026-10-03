//-----------------------------------------------------------------------
// <copyright file="TestRunCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Internal.Sinks;

namespace Akka.MultiNode.TestAdapter.Internal.Reporting
{
    /// <summary>
    /// Actor responsible for organizing all of the data for each test run
    /// </summary>
    internal class TestRunCoordinator : ReceiveActor, IWithUnboundedStash
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
            Ready();
        }

        #region Internal fields and Properties

        protected readonly DateTime TestRunStarted;

        protected IActorRef _currentSpecRunActor;

        /// <summary>
        /// Non-null while an <see cref="EndTestRun"/> is waiting for the in-flight spec to finish
        /// before the whole run is completed and the reply returned.
        /// </summary>
        private IActorRef _testRunRequester;

        public IStash Stash { get; set; }

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

        /// <summary>
        /// Default ("ready") behavior: route node messages, begin/end specs, and answer state requests.
        /// </summary>
        private void Ready()
        {
            Receive<MultiNodeMessage>(message =>
            {
                if (_currentSpecRunActor == null) return;
                _currentSpecRunActor.Forward(message);
            });
            Receive<BeginNewSpec>(ReceiveBeginSpecRun);
            Receive<EndSpec>(spec =>
            {
                //nothing to end; ignore (matches the previous no-op guard)
                if (_currentSpecRunActor == null) return;
                BeginEndSpecRun();
            });
            Receive<EndTestRun>(run =>
            {
                //clean up the current spec, if it hasn't been done already, then complete the run
                if (_currentSpecRunActor != null)
                {
                    _testRunRequester = Sender;
                    BeginEndSpecRun();
                }
                else
                {
                    CompleteTestRun(Sender);
                }
            });
            Receive<RequestTestRunState>(state => Sender.Tell(TestRunData.Copy(TestRunPassed(TestRunData))));
            Receive<SubscribeFactCompletionMessages>(AddSubscriber);
            Receive<UnsubscribeFactCompletionMessages>(RemoveSubscriber);
            // A watched SpecRunCoordinator that stopped after already reporting its FactData: harmless here.
            Receive<Terminated>(_ => { });
        }

        /// <summary>
        /// Ask the in-flight <see cref="SpecRunCoordinator"/> to finish by <see cref="ActorRefImplicitSenderExtensions.Tell"/>-ing
        /// it <see cref="EndSpec"/> and awaiting its <see cref="FactData"/> reply as a message — no Ask timeout to race
        /// under load. While waiting we stash every other message so the previous await-sequential ordering is preserved,
        /// and DeathWatch guards against the coordinator dying without replying (which would otherwise hang the run).
        /// </summary>
        private void BeginEndSpecRun()
        {
            Context.Watch(_currentSpecRunActor);
            _currentSpecRunActor.Tell(new EndSpec()); // sender = Self; SpecRunCoordinator replies to us with FactData
            Become(AwaitingSpecCompletion);
        }

        private void AwaitingSpecCompletion()
        {
            Receive<FactData>(factData => OnSpecCompleted(factData));
            Receive<Terminated>(terminated =>
            {
                //the spec coordinator stopped before replying — complete the spec with no data rather than hang the run
                if (Equals(terminated.ActorRef, _currentSpecRunActor))
                    OnSpecCompleted(null);
            });
            //defer everything else until the in-flight spec has reported
            ReceiveAny(_ => Stash.Stash());
        }

        private void OnSpecCompleted(FactData factData)
        {
            if (factData != null)
            {
                TestRunData.AddSpec(factData);

                //Publish the FactData back to any subscribers who wanted it
                foreach (var subscriber in Subscribers)
                    subscriber.Tell(factData);
            }

            //Ready to begin the next spec
            _currentSpecRunActor = null;
            Become(Ready);

            if (_testRunRequester != null)
            {
                var requester = _testRunRequester;
                _testRunRequester = null;
                CompleteTestRun(requester);
            }
            else
            {
                Stash.UnstashAll();
            }
        }

        private void CompleteTestRun(IActorRef requester)
        {
            //Mark the test run as finished
            TestRunData.Complete();

            //Deliver the final copy of the TestRunData
            requester.Tell(TestRunData.Copy());

            //shutdown
            Context.Stop(Self);
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

        private static bool TestRunPassed(TestRunTree tree)
        {
            return tree.Specs.All(x => x.Passed.HasValue && x.Passed.Value);
        }

        #endregion
    }
}

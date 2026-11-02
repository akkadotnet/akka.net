//-----------------------------------------------------------------------
// <copyright file="MessageSinkActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.MultiNode.TestAdapter.Internal.Reporting;

namespace Akka.MultiNode.TestAdapter.Internal.Sinks
{
    /// <summary>
    /// Actor responsible for directing the flow of all messages for each test run.
    /// </summary>
    internal abstract class MessageSinkActor : ReceiveActor
    {
        #region Message classes

        /// <summary>
        /// Used to signal that the underlying  <see cref="MessageSinkActor"/> 
        /// must collect and report its final test run results.
        /// 
        /// Shut down process is ready to begin.
        /// </summary>
        public class BeginSinkTerminate
        {
            public BeginSinkTerminate(TestRunTree testRun, IActorRef subscriber)
            {
                Subscriber = subscriber;
                TestRun = testRun;
            }

            public TestRunTree TestRun { get; private set; }
            public IActorRef Subscriber { get; private set; }
        }

        /// <summary>
        /// Signals to <see cref="MessageSink"/> that the <see cref="MessageSinkActor"/> is ready to be
        /// shut down.
        /// </summary>
        public class SinkCanBeTerminated { }

        #endregion

        protected MessageSinkActor()
        {
            SetReceive();
        }

        /// <summary>
        /// Use the template method pattern here to force child actors to fill in
        /// all handlers for these classes
        /// </summary>
        private void SetReceive()
        {
            Receive<SinkCoordinator.Ready>(HandleReady);
            Receive<BeginNewSpec>(HandleNewSpec);
            Receive<EndSpec>(HandleEndSpec);
            Receive<LogMessageFragmentForNode>(HandleNodeMessageFragment);
            Receive<LogMessageForTestRunner>(HandleRunnerMessage);
            Receive<NodeCompletedSpecWithSuccess>(HandleNodeSpecPass);
            Receive<NodeCompletedSpecWithFail>(HandleNodeSpecFail);
            Receive<EndTestRun>(HandleTestRunEnd);
            Receive<TestRunTree>(HandleTestRunTree);
            Receive<BeginSinkTerminate>(HandleSinkTerminate);
            AdditionalReceives();
        }

        #region Abstract message-handling methods

        /// <summary>
        /// Used to hook additional <see cref="Receive"/> methods into the <see cref="MessageSinkActor"/>
        /// </summary>
        protected abstract void AdditionalReceives();

        private void HandleReady(SinkCoordinator.Ready ready) => Sender.Tell(ready);
        
        protected abstract void HandleNewSpec(BeginNewSpec newSpec);

        protected abstract void HandleEndSpec(EndSpec endSpec);

        /// <summary>
        /// Used for truncated messages (happens when there's a line break during standard I/O redirection from child nodes)
        /// </summary>
        protected abstract void HandleNodeMessageFragment(LogMessageFragmentForNode logMessageFragment);

        protected abstract void HandleRunnerMessage(LogMessageForTestRunner node);

        protected abstract void HandleNodeSpecPass(NodeCompletedSpecWithSuccess nodeSuccess);

        protected abstract void HandleNodeSpecFail(NodeCompletedSpecWithFail nodeFail);

        protected virtual void HandleTestRunEnd(EndTestRun endTestRun)
        {
            Self.Tell(new BeginSinkTerminate(null, Sender));
        }

        protected virtual void HandleSinkTerminate(BeginSinkTerminate terminate)
        {
            terminate.Subscriber.Tell(new SinkCanBeTerminated());
        }

        protected abstract void HandleTestRunTree(TestRunTree tree);

        #endregion
    }
}


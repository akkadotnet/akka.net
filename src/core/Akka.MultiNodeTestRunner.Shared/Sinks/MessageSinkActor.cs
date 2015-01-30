using Akka.Actor;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    /// <summary>
    /// Actor responsible for directing the flow of all messages for each test run.
    /// </summary>
    public abstract class MessageSinkActor : ReceiveActor
    {
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
            Receive<BeginNewSpec>(spec => HandleNewSpec(spec));
            Receive<EndSpec>(endspec => HandleEndSpec(endspec));
            Receive<LogMessageForNode>(node => HandleNodeMessage(node));
            Receive<LogMessageFragmentForNode>(node => HandleNodeMessageFragment(node));
            Receive<LogMessageForTestRunner>(node => HandleRunnerMessage(node));
            Receive<NodeCompletedSpecWithSuccess>(success => HandleNodeSpecPass(success));
            Receive<NodeCompletedSpecWithFail>(fail => HandleNodeSpecFail(fail));
            Receive<EndTestRun>(end => HandleTestRunEnd(end));
            AdditionalReceives();
        }

        #region Abstract message-handling methods

        /// <summary>
        /// Used to hook additional <see cref="Receive"/> methods into the <see cref="MessageSinkActor"/>
        /// </summary>
        protected abstract void AdditionalReceives();

        protected abstract void HandleNewSpec(BeginNewSpec newSpec);

        protected abstract void HandleEndSpec(EndSpec endSpec);

        protected abstract void HandleNodeMessage(LogMessageForNode logMessage);

        /// <summary>
        /// Used for truncated messages (happens when there's a line break during standard I/O redirection from child nodes)
        /// </summary>
        protected abstract void HandleNodeMessageFragment(LogMessageFragmentForNode logMessageFragment);

        protected abstract void HandleRunnerMessage(LogMessageForTestRunner node);

        protected abstract void HandleNodeSpecPass(NodeCompletedSpecWithSuccess nodeSuccess);

        protected abstract void HandleNodeSpecFail(NodeCompletedSpecWithFail nodeFail);

        protected abstract void HandleTestRunEnd(EndTestRun endTestRun);

        #endregion
    }
}
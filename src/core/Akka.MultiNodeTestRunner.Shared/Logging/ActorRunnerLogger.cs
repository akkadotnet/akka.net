using System.Globalization;
using Akka.Actor;

namespace Akka.MultiNodeTestRunner.Shared.Logging
{
    /// <summary>
    /// Actor-based implementation of <see cref="ITestRunnerLogger"/>.
    /// </summary>
    public class ActorRunnerLogger : ITestRunnerLogger
    {
        private readonly IActorRef _actor;
        private readonly int _nodeIndex;

        public ActorRunnerLogger(IActorRef actor, int nodeIndex)
        {
            _actor = actor;
            _nodeIndex = nodeIndex;
        }

        public void Write(object obj)
        {
           WriteString(obj.ToString());
        }

        public void WriteLine(string formatStr, params object[] args)
        {
            WriteString(string.Format(formatStr, args));
        }

        protected void WriteString(string message)
        {
            if (!message.StartsWith("[NODE", true, CultureInfo.InvariantCulture))
            {
                message = "[NODE" + _nodeIndex + "]" + message;
            }
            _actor.Tell(message);
        }
    }
}
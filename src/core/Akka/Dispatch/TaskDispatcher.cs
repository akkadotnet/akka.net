using System;
using System.Threading.Tasks;

namespace Akka.Dispatch
{
    /// <summary>
    /// Task based dispatcher
    /// </summary>
    public class TaskDispatcher : MessageDispatcher
    {
        /// <summary>
        /// Takes a <see cref="MessageDispatcherConfigurator"/>
        /// </summary>
        public TaskDispatcher(MessageDispatcherConfigurator configurator) : base(configurator)
        {
        }

        public override void Schedule(Action run)
        {
            Task.Run(run);
        }
    }    
}
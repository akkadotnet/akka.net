using System;
using System.Threading.Tasks;

namespace Akka.Dispatch
{
    /// <summary>
    /// Task based dispatcher
    /// </summary>
    public class TaskDispatcher : MessageDispatcher
    {
        public override void Schedule(Action run)
        {
            Task.Run(run);
        }
    }    
}
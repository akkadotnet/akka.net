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

        public override string ToString()
        {
            return string.Format("(Sender: {0}, Self: {1}, Message: {2})", Sender, Self, Message);
        }
}
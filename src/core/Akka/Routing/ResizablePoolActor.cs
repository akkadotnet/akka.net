using Akka.Actor;

namespace Akka.Routing
{
    public class ResizablePoolActor : RouterActor
    {
   //     private SupervisorStrategy supervisorStrategy;

        public ResizablePoolActor(SupervisorStrategy supervisorStrategy)
        {
            this.supervisorStrategy = supervisorStrategy;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return supervisorStrategy;
        }

        protected ResizablePoolCell ResizerCell
        {
            get { return Context.AsInstanceOf<ResizablePoolCell>(); }
        }

        protected override void OnReceive(object message)
        {
            if (message is Resize)
            {
                if (ResizerCell != null)
                {
                    ResizerCell.Resize(false);
                }
            }
            else
            {
                base.OnReceive(message);
            }
        }
    }
    public class Resize : RouterManagementMesssage
    { }

}
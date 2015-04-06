using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Routing
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// Defines <see cref="Pool"/> routers who can resize the number of routees
    /// they use based on a defined <see cref="Resizer"/>
    /// </summary>
    internal class ResizablePoolActor : RouterPoolActor
    {
        //     private SupervisorStrategy supervisorStrategy;

        public ResizablePoolActor(SupervisorStrategy supervisorStrategy) : base(supervisorStrategy)
        {
        }

        protected ResizablePoolCell ResizerCell
        {
            get { return Context.AsInstanceOf<ResizablePoolCell>(); }
        }

        protected override void OnReceive(object message)
        {
            if (message is Resize && ResizerCell != null)
            {

                ResizerCell.Resize(false);
            }
            else
            {
                base.OnReceive(message);
            }
        }

        protected override void StopIfAllRouteesRemoved()
        {
            //we don't care if routees are removed
        }
    }

    /// <summary>
    /// Command used to resize a <see cref="ResizablePoolActor"/>
    /// </summary>
    public class Resize : RouterManagementMessage
    { }

}
using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{   
    public class DaemonMsgCreate
    {
        public DaemonMsgCreate(Props props,Deploy deploy,string path,ActorRef supervisor)
        {
            this.Props = props;
            this.Deploy = deploy;
            this.Path = path;
            this.Supervisor = supervisor;
        }

        public Props Props { get;private set; }

        public Deploy Deploy { get; private set; }

        public string Path { get; private set; }

        public ActorRef Supervisor { get; private set; }
    }

    public class RemoteDaemon : VirtualPathContainer
    {
        public RemoteDaemon(ActorRefProvider provider,ActorPath path,InternalActorRef parent) : base(provider,path,parent)
        {

        }
        protected void OnReceive(object message)
        {
            if (message is DaemonMsgCreate)
            {
                HandleDaemonMsgCreate((DaemonMsgCreate)message);
            }
            else
            {
              //  Unhandled(message);
            }            
        }

        private void HandleDaemonMsgCreate(DaemonMsgCreate message)
        {
          //  ActorPath path = ActorPath.Parse(message.Path, Context.System);

          //  Context.ActorOf(message.Props);
        }
    }
}

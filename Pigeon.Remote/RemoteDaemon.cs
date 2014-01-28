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

    //TODO: create a specific actortype which forces DaemonMailbox for unbounded parallellism
    public class RemoteDaemon : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Pattern.Match(message)
                .With<DaemonMsgCreate>(HandleDaemonMsgCreate)
                .Default(Unhandled);
        }

        private void HandleDaemonMsgCreate(DaemonMsgCreate message)
        {
            ActorPath path = ActorPath.Parse(message.Path, Context.System);

        }
    }
}

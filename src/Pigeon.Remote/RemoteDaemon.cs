using Pigeon.Actor;
using Pigeon.Dispatch.SysMsg;
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
        public ActorSystem System { get;private set; }
        public RemoteDaemon(ActorSystem system,ActorPath path,InternalActorRef parent) : base(system.Provider,path,parent)
        {
            this.System = system;
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

        protected override void TellInternal(object message, ActorRef sender)
        {
            OnReceive(message);
        }

        private void HandleDaemonMsgCreate(DaemonMsgCreate message)
        {
            var supervisor = (InternalActorRef)message.Supervisor;
            var props = message.Props;
            ActorPath path = ActorPath.Parse(message.Path, System);
            var subPath = path.Skip(1);
            var p = this.Path / path.Skip(1);            
            var actor = System.Provider.ActorOf(System, props, supervisor, p, 0);
            var name = string.Join("/", subPath);
            this.AddChild(name, actor);
            actor.Tell(new Watch(actor, this));
        }

        public override ActorRef GetChild(IEnumerable<string> name)
        {
            //TODO: I have no clue what the scala version does
            if (!name.Any())
                return this;

            var n = name.First();
            if (string.IsNullOrEmpty(n))
                return this;
            else
            {
                var parts = name.ToArray();
                for(int i=parts.Length;i>=0;i--)
                {
                    var joined = string.Join("/", parts, 0, i);
                    InternalActorRef child;
                    if (children.TryGetValue(joined, out child))
                    {
                        //longest match found
                        var rest = parts.Skip(i);
                        return child.GetChild(rest);
                    }
                }
                return Nobody;
                    
            }
        }
    }
}

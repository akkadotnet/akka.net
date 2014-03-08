using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Remote
{
    public class DaemonMsgCreate
    {
        public DaemonMsgCreate(Props props, Deploy deploy, string path, ActorRef supervisor)
        {
            Props = props;
            Deploy = deploy;
            Path = path;
            Supervisor = supervisor;
        }

        public Props Props { get; private set; }

        public Deploy Deploy { get; private set; }

        public string Path { get; private set; }

        public ActorRef Supervisor { get; private set; }
    }

    public class RemoteDaemon : VirtualPathContainer
    {
        public RemoteDaemon(ActorSystem system, ActorPath path, InternalActorRef parent)
            : base(system.Provider, path, parent)
        {
            System = system;
        }

        public ActorSystem System { get; private set; }

        protected void OnReceive(object message)
        {
            if (message is DaemonMsgCreate)
            {
                HandleDaemonMsgCreate((DaemonMsgCreate) message);
            }
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            OnReceive(message);
        }

        private void HandleDaemonMsgCreate(DaemonMsgCreate message)
        {
            //TODO: find out what format "Path" should have
            var supervisor = (InternalActorRef) message.Supervisor;
            Props props = message.Props;
            ActorPath path = Path/message.Path.Split('/');
            InternalActorRef actor = System.Provider.ActorOf(System, props, supervisor, path);
            string name = message.Path;
            AddChild(name, actor);
            actor.Tell(new Watch(actor, this));
        }

        public override ActorRef GetChild(IEnumerable<string> name)
        {
            //TODO: I have no clue what the scala version does
            if (!name.Any())
                return this;

            string n = name.First();
            if (string.IsNullOrEmpty(n))
                return this;
            string[] parts = name.ToArray();
            for (int i = parts.Length; i >= 0; i--)
            {
                string joined = string.Join("/", parts, 0, i);
                InternalActorRef child;
                if (children.TryGetValue(joined, out child))
                {
                    //longest match found
                    IEnumerable<string> rest = parts.Skip(i);
                    return child.GetChild(rest);
                }
            }
            return Nobody;
        }
    }
}
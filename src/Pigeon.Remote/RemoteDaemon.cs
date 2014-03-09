using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Remote
{
    /// <summary>
    ///     Class DaemonMsgCreate.
    /// </summary>
    public class DaemonMsgCreate
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DaemonMsgCreate" /> class.
        /// </summary>
        /// <param name="props">The props.</param>
        /// <param name="deploy">The deploy.</param>
        /// <param name="path">The path.</param>
        /// <param name="supervisor">The supervisor.</param>
        public DaemonMsgCreate(Props props, Deploy deploy, string path, ActorRef supervisor)
        {
            Props = props;
            Deploy = deploy;
            Path = path;
            Supervisor = supervisor;
        }

        /// <summary>
        ///     Gets the props.
        /// </summary>
        /// <value>The props.</value>
        public Props Props { get; private set; }

        /// <summary>
        ///     Gets the deploy.
        /// </summary>
        /// <value>The deploy.</value>
        public Deploy Deploy { get; private set; }

        /// <summary>
        ///     Gets the path.
        /// </summary>
        /// <value>The path.</value>
        public string Path { get; private set; }

        /// <summary>
        ///     Gets the supervisor.
        /// </summary>
        /// <value>The supervisor.</value>
        public ActorRef Supervisor { get; private set; }
    }

    /// <summary>
    ///     Class RemoteDaemon.
    /// </summary>
    public class RemoteDaemon : VirtualPathContainer
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RemoteDaemon" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="path">The path.</param>
        /// <param name="parent">The parent.</param>
        public RemoteDaemon(ActorSystem system, ActorPath path, InternalActorRef parent)
            : base(system.Provider, path, parent)
        {
            System = system;
        }

        /// <summary>
        ///     Gets the system.
        /// </summary>
        /// <value>The system.</value>
        public ActorSystem System { get; private set; }

        /// <summary>
        ///     Called when [receive].
        /// </summary>
        /// <param name="message">The message.</param>
        protected void OnReceive(object message)
        {
            if (message is DaemonMsgCreate)
            {
                HandleDaemonMsgCreate((DaemonMsgCreate) message);
            }
        }

        /// <summary>
        ///     Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        protected override void TellInternal(object message, ActorRef sender)
        {
            OnReceive(message);
        }

        /// <summary>
        ///     Handles the daemon MSG create.
        /// </summary>
        /// <param name="message">The message.</param>
        private void HandleDaemonMsgCreate(DaemonMsgCreate message)
        {
            //TODO: find out what format "Path" should have
            var supervisor = (InternalActorRef) message.Supervisor;
            Props props = message.Props;
            ActorPath childPath = ActorPath.Parse(message.Path);
            IEnumerable<string> subPath = childPath.Elements.Drop(1);
            ActorPath path = Path/subPath;
            InternalActorRef actor = System.Provider.ActorOf(System, props, supervisor, path);
            string childName = subPath.Join("/");
            AddChild(childName, actor);
            actor.Tell(new Watch(actor, this));
        }

        /// <summary>
        ///     Gets the child.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorRef.</returns>
        public override ActorRef GetChild(IEnumerable<string> name)
        {
            string[] parts = name.ToArray();
            //TODO: I have no clue what the scala version does
            if (!parts.Any())
                return this;

            string n = parts.First();
            if (string.IsNullOrEmpty(n))
                return this;

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
using System;
using System.Linq;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Routing;

namespace Akka.Actor
{
    /// <summary>
    ///     Class ActorRefProvider.
    /// </summary>
    public abstract class ActorRefProvider
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ActorRefProvider" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        protected ActorRefProvider(ActorSystem system)
        {
            System = system;
        }

        /// <summary>
        ///     Gets the root path.
        /// </summary>
        /// <value>The root path.</value>
        public ActorPath RootPath { get; private set; }

        /// <summary>
        ///     Gets the temporary node.
        /// </summary>
        /// <value>The temporary node.</value>
        public ActorPath TempNode { get; private set; }


        /// <summary>
        ///     Gets the temporary container.
        /// </summary>
        /// <value>The temporary container.</value>
        public VirtualPathContainer TempContainer { get; private set; }

        /// <summary>
        ///     Gets or sets the system.
        /// </summary>
        /// <value>The system.</value>
        public ActorSystem System { get; protected set; }

        /// <summary>
        ///     Gets or sets the root cell.
        /// </summary>
        /// <value>The root cell.</value>
        public ActorCell RootCell { get; protected set; }

        /// <summary>
        ///     Gets or sets the dead letters.
        /// </summary>
        /// <value>The dead letters.</value>
        public ActorRef DeadLetters { get; protected set; }

        /// <summary>
        ///     Gets or sets the guardian.
        /// </summary>
        /// <value>The guardian.</value>
        public LocalActorRef Guardian { get; protected set; }

        /// <summary>
        ///     Gets or sets the system guardian.
        /// </summary>
        /// <value>The system guardian.</value>
        public LocalActorRef SystemGuardian { get; protected set; }

        /// <summary>
        ///     Gets or sets the address.
        /// </summary>
        /// <value>The address.</value>
        public virtual Address Address { get; set; }

        /// <summary>
        ///     Initializes this instance.
        /// </summary>
        public virtual void Init()
        {
            RootPath = new RootActorPath(Address);
            TempNode = RootPath/"temp";

            RootCell = new ActorCell(System, "", new ConcurrentQueueMailbox());
            DeadLetters = new DeadLetterActorRef(this, RootPath/"deadLetters", System.EventStream);
            Guardian = (LocalActorRef) RootCell.ActorOf<GuardianActor>("user");
            SystemGuardian = (LocalActorRef) RootCell.ActorOf<GuardianActor>("system");
            TempContainer = new VirtualPathContainer(this, TempNode, null);
        }

        /// <summary>
        ///     Registers the temporary actor.
        /// </summary>
        /// <param name="actorRef">The actor reference.</param>
        /// <param name="path">The path.</param>
        public void RegisterTempActor(InternalActorRef actorRef, ActorPath path)
        {
            TempContainer.AddChild(path.Name, actorRef);
        }

        /// <summary>
        ///     Unregisters the temporary actor.
        /// </summary>
        /// <param name="path">The path.</param>
        public void UnregisterTempActor(ActorPath path)
        {
            TempContainer.RemoveChild(path.Name);
        }

        /// <summary>
        ///     Temporaries the path.
        /// </summary>
        /// <returns>ActorPath.</returns>
        public ActorPath TempPath()
        {
            return TempNode/Guid.NewGuid().ToString();
        }

        /// <summary>
        ///     Roots the guardian at.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns>ActorRef.</returns>
        public virtual ActorRef RootGuardianAt(Address address)
        {
            return RootCell.Self;
        }

        /// <summary>
        ///     Actors the of.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="props">The props.</param>
        /// <param name="supervisor">The supervisor.</param>
        /// <param name="path">The path.</param>
        /// <returns>InternalActorRef.</returns>
        public abstract InternalActorRef ActorOf(ActorSystem system, Props props, InternalActorRef supervisor,
            ActorPath path);

        /// <summary>
        ///     Resolves the actor reference.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>ActorRef.</returns>
        public ActorRef ResolveActorRef(string path)
        {
            if (path == "")
                return ActorRef.NoSender;

            ActorPath actorPath = ActorPath.Parse(path);
            return ResolveActorRef(actorPath);
        }

        /// <summary>
        ///     Resolves the actor reference.
        /// </summary>
        /// <param name="actorPath">The actor path.</param>
        /// <returns>ActorRef.</returns>
        public abstract ActorRef ResolveActorRef(ActorPath actorPath);

        /// <summary>
        ///     Afters the send system message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void AfterSendSystemMessage(SystemMessage message)
        {
            message.Match()
                .With<Watch>(m => { })
                .With<Unwatch>(m => { });

            //    message match {
            //  // Sending to local remoteWatcher relies strong delivery guarantees of local send, i.e.
            //  // default dispatcher must not be changed to an implementation that defeats that
            //  case rew: RemoteWatcher.Rewatch ⇒
            //    remoteWatcher ! RemoteWatcher.RewatchRemote(rew.watchee, rew.watcher)
            //  case Watch(watchee, watcher)   ⇒ remoteWatcher ! RemoteWatcher.WatchRemote(watchee, watcher)
            //  case Unwatch(watchee, watcher) ⇒ remoteWatcher ! RemoteWatcher.UnwatchRemote(watchee, watcher)
            //  case _                         ⇒
            //}
        }

        public Deployer Deployer { get;protected set; }
    }

    /// <summary>
    ///     Class LocalActorRefProvider. This class cannot be inherited.
    /// </summary>
    public sealed class LocalActorRefProvider : ActorRefProvider
    {
        public override void Init()
        {
            Deployer = new Deployer(System.Settings);
            base.Init();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LocalActorRefProvider" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public LocalActorRefProvider(ActorSystem system) : base(system)
        {
            Address = new Address("akka", System.Name); //TODO: this should not work this way...
        }

        /// <summary>
        ///     Actors the of.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="props">The props.</param>
        /// <param name="supervisor">The supervisor.</param>
        /// <param name="path">The path.</param>
        /// <returns>InternalActorRef.</returns>
        public override InternalActorRef ActorOf(ActorSystem system, Props props, InternalActorRef supervisor,
            ActorPath path)
        {
            Mailbox mailbox = System.Mailboxes.FromConfig(props.Mailbox);

            ActorCell cell;
            if (props.RouterConfig is NoRouter)
            {
                cell = new ActorCell(system, supervisor, props, path, mailbox);
            }
            else
            {
                cell = new RoutedActorCell(system, supervisor, props,props, path, mailbox);
            }
            cell.NewActor();

            //  parentContext.Watch(cell.Self);
            return cell.Self;
        }

        /// <summary>
        ///     Resolves the actor reference.
        /// </summary>
        /// <param name="actorPath">The actor path.</param>
        /// <returns>ActorRef.</returns>
        /// <exception cref="System.NotSupportedException">The provided actor path is not valid in the LocalActorRefProvider</exception>
        public override ActorRef ResolveActorRef(ActorPath actorPath)
        {
            if (Address.Equals(actorPath.Address))
            {
                if (actorPath.Elements.Head() == "temp")
                {
                    //skip ""/"temp", 
                    string[] parts = actorPath.Elements.Drop(1).ToArray();
                    return TempContainer.GetChild(parts);
                }
                //standard
                ActorCell currentContext = RootCell;
                foreach (string part in actorPath.Elements)
                {
                    currentContext = ((LocalActorRef) currentContext.Child(part)).Cell;
                }
                return currentContext.Self;
            }
            throw new NotSupportedException("The provided actor path is not valid in the LocalActorRefProvider");
        }
    }
}
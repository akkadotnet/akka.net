using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Remote
{
    public class RemoteActorRef : InternalActorRef
    {
        private readonly Deploy deploy;
        private readonly InternalActorRef parent;
        private readonly Props props;

        public RemoteActorRef(RemoteTransport remote, Address localAddressToUse, ActorPath path, InternalActorRef parent,
            Props props, Deploy deploy)
        {
            Remote = remote;
            LocalAddressToUse = localAddressToUse;
            Path = path;
            this.parent = parent;
            this.props = props;
            this.deploy = deploy;
        }

        public Address LocalAddressToUse { get; private set; }
        public RemoteTransport Remote { get; private set; }

        public override InternalActorRef Parent
        {
            get { return parent; }
        }

        public override ActorRefProvider Provider
        {
            get { return Remote.Provider; }
        }

        public override ActorRef GetChild(IEnumerable<string> name)
        {
            throw new NotImplementedException();
        }

        public override void Resume(Exception causedByFailure = null)
        {
            SendSystemMessage(new Resume(causedByFailure));
        }

        public override void Stop()
        {
            SendSystemMessage(new Terminate());
        }

        public override void Restart(Exception cause)
        {
            SendSystemMessage(new Recreate(cause));
        }

        public override void Suspend()
        {
            SendSystemMessage(new Suspend());
        }

        private void SendSystemMessage(SystemMessage message)
        {
            try
            {
                Remote.Send(message, null, this);
                Provider.AfterSendSystemMessage(message);
            }
            catch
            {
                throw;
            }
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            try
            {
                Remote.Send(message, sender, this);
            }
            catch
            {
                throw;
            }
        }

        public void Start()
        {
            if (props != null && deploy != null)
                Remote.Provider.UseActorOnNode(this, props, deploy, parent);
        }
    }
}
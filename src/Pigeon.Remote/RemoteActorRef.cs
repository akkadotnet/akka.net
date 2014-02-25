using Pigeon.Actor;
using Pigeon.Dispatch.SysMsg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public class RemoteActorRef : InternalActorRef
    {
        private InternalActorRef parent;
        public Address LocalAddressToUse { get; private set; }
        public RemoteTransport Remote { get; private set; }
        private Deploy deploy;
        private Props props;
        public RemoteActorRef(RemoteTransport remote, Address localAddressToUse, ActorPath path, InternalActorRef parent, Props props, Deploy deploy)
        {
            this.Remote = remote;
            this.LocalAddressToUse = localAddressToUse;
            this.Path = path;
            this.parent = parent;
            this.props = props;
            this.deploy = deploy;


        }

        public override InternalActorRef Parent
        {
            get { return parent; }
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

        public override ActorRefProvider Provider
        {
            get { return this.Remote.Provider; }
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
                ((RemoteActorRefProvider)Remote.Provider).UseActorOnNode(this, props, deploy, parent);
        }
    }
}

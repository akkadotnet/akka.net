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
        private Address localAddressToUse;
        private RemoteTransport remote;
        private Deploy deploy;
        private Props props;
        public RemoteActorRef(RemoteTransport remote, Address localAddressToUse, ActorPath path, InternalActorRef parent, Props props, Deploy deploy)
        {
            this.remote = remote;
            this.localAddressToUse = localAddressToUse;
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
                remote.Send(message, null, this);
                Provider.AfterSendSystemMessage(message);
            }
            catch
            {
                throw;
            }
        }

        public override ActorRefProvider Provider
        {
            get { return this.remote.Provider; }
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            try
            {
                remote.Send(message, sender, this);
            }
            catch
            {
                throw;
            }
        }

        public void Start()
        {
            if (props != null && deploy != null)
                ((RemoteActorRefProvider)remote.Provider).UseActorOnNode(this, props, deploy, parent);
        }
    }
}

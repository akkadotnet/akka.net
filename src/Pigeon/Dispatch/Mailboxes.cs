using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Dispatch
{
    public class Mailboxes
    {
        private ActorSystem system;
        public Mailboxes(ActorSystem system)
        {
            this.system = system;
        }

        public Mailbox FromConfig(string path)
        {
            //TODO: this should not exist, its a temp hack because we are not serializing mailbox info when doing remote deply..
            if (path == null)
            {
                return new ConcurrentQueueMailbox();
            }

            var config = system.Settings.Config.GetConfig(path);
            var type = config.GetString("mailbox-type");

            Type mailboxType = Type.GetType(type);
            var mailbox = (Mailbox)Activator.CreateInstance(mailboxType);
            return mailbox;

            /*
mailbox-capacity = 1000
mailbox-push-timeout-time = 10s
stash-capacity = -1
            */
        }
    }
}

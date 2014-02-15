using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Dispatch
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

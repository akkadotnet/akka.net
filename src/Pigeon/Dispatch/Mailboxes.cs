using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Dispatch
{
    public class Mailboxes
    {
        private readonly ActorSystem system;

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

            Config config = system.Settings.Config.GetConfig(path);
            string type = config.GetString("mailbox-type");

            Type mailboxType = Type.GetType(type);
            var mailbox = (Mailbox) Activator.CreateInstance(mailboxType);
            return mailbox;

            /*
mailbox-capacity = 1000
mailbox-push-timeout-time = 10s
stash-capacity = -1
            */
        }
    }
}
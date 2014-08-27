using System;
using System.Diagnostics;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Dispatch
{
    /// <summary>
    ///     Class Mailboxes.
    /// </summary>
    public class Mailboxes
    {
        /// <summary>
        ///     The system
        /// </summary>
        private readonly ActorSystem system;

        private readonly DeadLetterMailbox _deadLetterMailbox;
        public static readonly string DefaultMailboxId = "akka.actor.default-mailbox";

        /// <summary>
        ///     Initializes a new instance of the <see cref="Mailboxes" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public Mailboxes(ActorSystem system)
        {
            this.system = system;
            _deadLetterMailbox = new DeadLetterMailbox(system.DeadLetters);
        }

        public DeadLetterMailbox DeadLetterMailbox { get { return _deadLetterMailbox; } }

        /// <summary>
        ///     Creates a mailbox from a configuration path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Mailbox.</returns>
        public Mailbox FromConfig(string path)
        {
            //TODO: this should not exist, its a temp hack because we are not serializing mailbox info when doing remote deply..
            if (string.IsNullOrEmpty(path))
            {
                return new DefaultMailbox();
            }

            Config config = system.Settings.Config.GetConfig(path);
            string type = config.GetString("mailbox-type");

            Type mailboxType = Type.GetType(type);
            Debug.Assert(mailboxType != null, "mailboxType != null");
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
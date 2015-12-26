//-----------------------------------------------------------------------
// <copyright file="Mailboxes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly ActorSystem _system;

        private readonly DeadLetterMailbox _deadLetterMailbox;
        public static readonly string DefaultMailboxId = "akka.actor.default-mailbox";
        private readonly Dictionary<Type, Config> _requirementsMapping;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Mailboxes" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public Mailboxes(ActorSystem system)
        {
            _system = system;
            _deadLetterMailbox = new DeadLetterMailbox(system.DeadLetters);
            var mailboxConfig = system.Settings.Config.GetConfig("akka.actor.mailbox");
            var requirements = mailboxConfig.GetConfig("requirements").AsEnumerable().ToList();
            _requirementsMapping = new Dictionary<Type, Config>();
            foreach (var kvp in requirements)
            {
                var type = Type.GetType(kvp.Key);
                if (type == null)
                {
                    //TODO: can't log here, logger not created yet
                  //  system.Log.Warn("Mailbox Requirement mapping '{0}' is not an actual type",kvp.Key);
                    continue;
                }
                var config = system.Settings.Config.GetConfig(kvp.Value.GetString());
                _requirementsMapping.Add(type, config);
            }
        }

        public Type LookupByQueueType(Type queueType)
        {
            var config = _requirementsMapping[queueType];
            var mailbox = config.GetString("mailbox-type");
            var mailboxType = Type.GetType(mailbox);
            return mailboxType;
        }

        public DeadLetterMailbox DeadLetterMailbox { get { return _deadLetterMailbox; } }

        public Mailbox CreateMailbox(Props props, Config dispatcherConfig)
        {
            var type = GetMailboxType(props, dispatcherConfig);
            var instance = (Mailbox)Activator.CreateInstance(type);
            return instance;
        }

        public Type GetMailboxType(Props props, Config dispatcherConfig)
        {
            if (!string.IsNullOrEmpty(props.Mailbox))
            {
                return FromConfig(props.Mailbox);
            }

            var actortype = props.Type;
            var interfaces = actortype.GetInterfaces()
                .Where(i => i.IsGenericType)
                .Where(i => i.GetGenericTypeDefinition() == typeof (IRequiresMessageQueue<>))
                .Select(i => i.GetGenericArguments().First())
                .ToList();

            if (interfaces.Count > 0)
            {
                var config = _requirementsMapping[interfaces.First()];
                var mailbox = config.GetString("mailbox-type");

                try
                {
                    var mailboxType = Type.GetType(mailbox, true);
                    return mailboxType;
                }
                catch (TypeLoadException ex)
                {
                    var msgException = string.Format("Could not find type '{0}'", mailbox);
                    throw new TypeLoadException(msgException, ex);
                }
            }


            return FromConfig(DefaultMailboxId);
        }

        /// <summary>
        /// Creates a mailbox from a configuration path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Mailbox.</returns>
        public Type FromConfig(string path)
        {
            //TODO: this should not exist, its a temp hack because we are not serializing mailbox info when doing remote deploy..
            if (string.IsNullOrEmpty(path))
            {
                return typeof (UnboundedMailbox);
            }

            var config = _system.Settings.Config.GetConfig(path);
            var type = config.GetString("mailbox-type");

            try
            {
                var mailboxType = Type.GetType(type, true);
                return mailboxType;
            }
            catch (TypeLoadException ex)
            {
                var msgException = string.Format("Could not find type '{0}'", type);
                throw new TypeLoadException(msgException, ex);
            }
            /*
mailbox-capacity = 1000
mailbox-push-timeout-time = 10s
stash-capacity = -1
            */
        }
    }
}


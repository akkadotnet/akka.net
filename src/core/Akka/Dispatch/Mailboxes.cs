//-----------------------------------------------------------------------
// <copyright file="Mailboxes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.MessageQueues;

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
        public static readonly string NoMailboxRequirement = "";
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

        private Type GetRequiredType(Type actorType)
        {
            return actorType.GetInterfaces()
                .Where(i => i.IsGenericType)
                .Where(i => i.GetGenericTypeDefinition() == typeof (IRequiresMessageQueue<>))
                .Select(i => i.GetGenericArguments().First())
                .FirstOrDefault();
        }

        private Type GetMailboxRequirement(Config config)
        {
            var mailboxRequirement = config.GetString("mailbox-requirement");
            return mailboxRequirement == null || mailboxRequirement.Equals(NoMailboxRequirement) ? typeof (IMessageQueue) : Type.GetType(mailboxRequirement, true);
        }

        public Type GetMailboxType(Props props, Config dispatcherConfig)
        {
            if (!string.IsNullOrEmpty(props.Mailbox))
            {
                return FromConfig(props.Mailbox);
            }

            if (dispatcherConfig == null)
                dispatcherConfig = ConfigurationFactory.Empty;
            var id = dispatcherConfig.GetString("id");
            var deploy = props.Deploy;
            var actorType = props.Type;
            var actorRequirement = new Lazy<Type>(() => GetRequiredType(actorType));

            var mailboxRequirement = GetMailboxRequirement(dispatcherConfig);
            var hasMailboxRequirement = mailboxRequirement != typeof (IMessageQueue);

            var hasMailboxType = dispatcherConfig.HasPath("mailbox-type") &&
                                 dispatcherConfig.GetString("mailbox-type") != Deploy.NoMailboxGiven;

            Func<Type, Type> verifyRequirements = mailboxType =>
            {
                // TODO Akka code after introducing IProducesMessageQueue
                // if (hasMailboxRequirement && !mailboxRequirement.isAssignableFrom(mqType))
                //   throw new IllegalArgumentException(
                //     s"produced message queue type [$mqType] does not fulfill requirement for dispatcher [$id]. " +
                //       s"Must be a subclass of [$mailboxRequirement].")
                // if (hasRequiredType(actorClass) && !actorRequirement.isAssignableFrom(mqType))
                //   throw new IllegalArgumentException(
                //     s"produced message queue type [$mqType] does not fulfill requirement for actor class [$actorClass]. " +
                //       s"Must be a subclass of [$actorRequirement].")
                return mailboxType;
            };

            if (!deploy.Mailbox.Equals(Deploy.NoMailboxGiven))
                return verifyRequirements(FromConfig(deploy.Mailbox));
            if (!deploy.Dispatcher.Equals(Deploy.NoDispatcherGiven) && hasMailboxType)
                return verifyRequirements(FromConfig(id));
            if (actorRequirement.Value != null)
            {
                try
                {
                    return verifyRequirements(LookupByQueueType(actorRequirement.Value));
                }
                catch (Exception e)
                {
                    if (hasMailboxRequirement)
                        return verifyRequirements(LookupByQueueType(mailboxRequirement));
                    throw;
                }
            }
            if (hasMailboxRequirement)
                return verifyRequirements(LookupByQueueType(mailboxRequirement));
            return verifyRequirements(FromConfig(DefaultMailboxId));
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

            var mailboxType = Type.GetType(type);
            return mailboxType;
            /*
mailbox-capacity = 1000
mailbox-push-timeout-time = 10s
stash-capacity = -1
            */
        }
    }
}


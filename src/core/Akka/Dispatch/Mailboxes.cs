//-----------------------------------------------------------------------
// <copyright file="Mailboxes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Util.Reflection;
using Akka.Dispatch.MessageQueues;
using Akka.Event;
using ConfigurationException = Akka.Configuration.ConfigurationException;

namespace Akka.Dispatch
{
    /// <summary>
    /// Contains the directory of all <see cref="MailboxType"/>s registered and configured with a given <see cref="ActorSystem"/>.
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
        private readonly Dictionary<Type, string> _mailboxBindings;
        private readonly Config _defaultMailboxConfig;

        private readonly ConcurrentDictionary<string, MailboxType> _mailboxTypeConfigurators = new ConcurrentDictionary<string, MailboxType>();

        private Settings Settings => _system.Settings;

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
            _mailboxBindings = new Dictionary<Type, string>();
            foreach (var kvp in requirements)
            {
                var type = Type.GetType(kvp.Key);
                if (type == null)
                {
                    Warn($"Mailbox Requirement mapping [{kvp.Key}] is not an actual type");
                    continue;
                }
                _mailboxBindings.Add(type, kvp.Value.GetString());
            }

            _defaultMailboxConfig = Settings.Config.GetConfig(DefaultMailboxId);
        }

        public DeadLetterMailbox DeadLetterMailbox { get { return _deadLetterMailbox; } }

        /// <summary>
        /// Check if this actor class can have a required message queue type.
        /// </summary>
        /// <param name="actorType">The type to check.</param>
        /// <returns><c>true</c> if this actor has a message queue type requirement. <c>false</c> otherwise.</returns>
        public bool HasRequiredType(Type actorType)
        {
            return actorType.GetInterfaces()
                .Where(i => i.IsGenericType)
                .Any(i => i.GetGenericTypeDefinition() == RequiresMessageQueueGenericType);
        }

        /// <summary>
        /// Check if this <see cref="MailboxType"/> implements the <see cref="IProducesMessageQueue{TQueue}"/> interface.
        /// </summary>
        /// <param name="mailboxType">The type of the <see cref="MailboxType"/> to check.</param>
        /// <returns><c>true</c> if this mailboxtype produces queues. <c>false</c> otherwise.</returns>
        public bool ProducesMessageQueue(Type mailboxType)
        {
            return mailboxType.GetInterfaces()
                .Where(i => i.IsGenericType)
                .Any(i => i.GetGenericTypeDefinition() == ProducesMessageQueueGenericType);
        }

        private string LookupId(Type queueType)
        {
            string id;
            if (!_mailboxBindings.TryGetValue(queueType, out id))
            {
                throw new ConfigurationException($"Mailbox Mapping for [{queueType}] not configured");
            }
            return id;
        }

        /// <summary>
        /// Returns a <see cref="MailboxType"/> as specified in configuration, based on the type, or if not defined null.
        /// </summary>
        /// <param name="queueType">The mailbox we need given the queue requirements.</param>
        /// <returns>A <see cref="MailboxType"/> as specified in configuration, based on the type, or if not defined null.</returns>
        public MailboxType LookupByQueueType(Type queueType)
        {
            return Lookup(LookupId(queueType));
        }

        /// <summary>
        /// Returns a <see cref="MailboxType"/> as specified in configuration, based on the id, or if not defined null.
        /// </summary>
        /// <param name="id">The ID of the mailbox to lookup</param>
        /// <returns>The <see cref="MailboxType"/> specified in configuration or if not defined null.</returns>
        public MailboxType Lookup(string id) => LookupConfigurator(id);

        // don't care if these happen twice
        private bool _mailboxSizeWarningIssued = false;
        private bool _mailboxNonZeroPushTimeoutWarningIssued = false;

        private MailboxType LookupConfigurator(string id)
        {
            MailboxType configurator;
            if (!_mailboxTypeConfigurators.TryGetValue(id, out configurator))
            {
                // It doesn't matter if we create a mailbox type configurator that isn't used due to concurrent lookup.
                if(id.Equals("unbounded")) configurator = new UnboundedMailbox();
                else if (id.Equals("bounded")) configurator = new BoundedMailbox(Settings, Config(id));
                else
                {
                    if(!Settings.Config.HasPath(id)) throw new ConfigurationException($"Mailbox Type [{id}] not configured");
                    var conf = Config(id);

                    var mailboxTypeName = conf.GetString("mailbox-type");
                    if (string.IsNullOrEmpty(mailboxTypeName)) throw new ConfigurationException($"The setting mailbox-type defined in [{id}] is empty");
                    var type = Type.GetType(mailboxTypeName);
                    if(type == null) throw new ConfigurationException($"Found mailbox-type [{mailboxTypeName}] in configuration for [{id}], but could not find that type in any loaded assemblies.");
                    var args = new object[] {Settings, conf};
                    try
                    {
                        configurator = (MailboxType) Activator.CreateInstance(type, args);
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException($"Cannot instantiate MailboxType {type}, defined in [{id}]. Make sure it has a public " +
                                                    $"constructor with [Akka.Actor.Settings, Akka.Configuration.Config] parameters", ex);
                    }

                    // TODO: check for blocking mailbox with a non-zero pushtimeout and issue a warning
                }

                // add the new configurator to the mapping, or keep the existing if it was already added
                _mailboxTypeConfigurators.AddOrUpdate(id, configurator, (s, type) => type);
            }

            return configurator;
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <param name="id">The id of the mailbox whose config we're going to generate.</param>
        /// <returns>A <see cref="Config"/> object for the mailbox with id <see cref="id"/></returns>
        private Config Config(string id)
        {
            return ConfigurationFactory.ParseString($"id:{id}")
                .WithFallback(Settings.Config.GetConfig(id))
                .WithFallback(_defaultMailboxConfig);
        }

        private static readonly Type RequiresMessageQueueGenericType = typeof (IRequiresMessageQueue<>);
        public Type GetRequiredType(Type actorType)
        {
            return actorType.GetInterfaces()
                .Where(i => i.IsGenericType)
                .Where(i => i.GetGenericTypeDefinition() == RequiresMessageQueueGenericType)
                .Select(i => i.GetGenericArguments().First())
                .FirstOrDefault();
        }

        private static readonly Type ProducesMessageQueueGenericType = typeof (IProducesMessageQueue<>);
        private Type GetProducedMessageQueueType(MailboxType mailboxType)
        {
            var queueType = mailboxType.GetType().GetInterfaces()
                .Where(i => i.IsGenericType)
                .Where(i => i.GetGenericTypeDefinition() == ProducesMessageQueueGenericType)
                .Select(i => i.GetGenericArguments().First())
                .FirstOrDefault();

            if(queueType == null)
                throw new ArgumentException(nameof(mailboxType), $"No IProducesMessageQueue<TQueue> supplied for {mailboxType}; illegal mailbox type definition.");
            return queueType;
        }

        private Type GetMailboxRequirement(Config config)
        {
            var mailboxRequirement = config.GetString("mailbox-requirement");
            return mailboxRequirement == null || mailboxRequirement.Equals(NoMailboxRequirement) ? typeof (IMessageQueue) : Type.GetType(mailboxRequirement, true);
        }

        public MailboxType GetMailboxType(Props props, Config dispatcherConfig)
        {
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

            if (!hasMailboxType && !_mailboxSizeWarningIssued && dispatcherConfig.HasPath("mailbox-size"))
            {
                Warn($"Ignoring setting 'mailbox-size for disaptcher [{id}], you need to specify 'mailbox-type=bounded`");
                _mailboxSizeWarningIssued = true;
            }

            Func<MailboxType, MailboxType> verifyRequirements = mailboxType =>
            {
                Lazy<Type> mqType = new Lazy<Type>(() => GetProducedMessageQueueType(mailboxType));
                if(hasMailboxRequirement && !mailboxRequirement.IsAssignableFrom(mqType.Value))
                    throw new ArgumentException($"produced message queue type [{mqType.Value}] does not fulfill requirement for dispatcher [{id}]." +
                                                $"Must be a subclass of [{mailboxRequirement}]");
                if(HasRequiredType(actorType) && !actorRequirement.Value.IsAssignableFrom(mqType.Value))
                    throw new ArgumentException($"produced message queue type of [{mqType.Value}] does not fulfill requirement for actor class [{actorType}]." +
                                                $"Must be a subclass of [{mailboxRequirement}]");
                return mailboxType;
            };

            if (!deploy.Mailbox.Equals(Deploy.NoMailboxGiven))
                return verifyRequirements(Lookup(deploy.Mailbox));
            if (!deploy.Dispatcher.Equals(Deploy.NoDispatcherGiven) && hasMailboxType)
                return verifyRequirements(Lookup(dispatcherConfig.GetString("id")));
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
            return verifyRequirements(Lookup(DefaultMailboxId));
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

            var mailboxType = TypeCache.GetType(type);
            return mailboxType;
            /*
mailbox-capacity = 1000
mailbox-push-timeout-time = 10s
stash-capacity = -1
            */
        }

        //TODO: stash capacity

        private void Warn(string msg)
        {
           _system.EventStream.Publish(new Warning("mailboxes", GetType(), msg));
        }
    }
}


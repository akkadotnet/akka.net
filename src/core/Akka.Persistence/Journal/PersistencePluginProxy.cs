//-----------------------------------------------------------------------
// <copyright file="PersistencePluginProxy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Persistence.Journal
{
    public class PersistencePluginProxy : ActorBase, IWithUnboundedStash
    {
        public sealed class TargetLocation
        {
            public TargetLocation(Address address)
            {
                Address = address;
            }

            public Address Address { get; private set; }
        }

        private sealed class InitTimeout
        {
            public static readonly InitTimeout Instance = new InitTimeout();
            private InitTimeout() { }
        }

        public static void SetTargetLocation(ActorSystem system, Address address)
        {
            var persistence = Persistence.Instance.Apply(system);
            persistence.JournalFor(null).Tell(new TargetLocation(address));
            if (string.IsNullOrEmpty(system.Settings.Config.GetString("akka.persistence.snapshot-store.plugin")))
                persistence.SnapshotStoreFor(null).Tell(new TargetLocation(address));
        }

        public static void Start(ActorSystem system)
        {
            var persistence = Persistence.Instance.Apply(system);
            persistence.JournalFor(null);
            if (string.IsNullOrEmpty(system.Settings.Config.GetString("akka.persistence.snapshot-store.plugin")))
                persistence.SnapshotStoreFor(null);
        }

        private interface IPluginType
        {
             string Qualifier { get; }
        }

        private class Journal : IPluginType
        {
            public string Qualifier { get { return "journal"; } }
        }

        private class SnapshotStore : IPluginType
        {
            public string Qualifier { get { return "snapshot-store"; } }
        }

        private readonly Config _config;
        private readonly IPluginType _pluginType;
        private readonly TimeSpan _initTimeout;
        private readonly string _targetPluginId;
        private readonly bool _startTarget;
        private readonly Address _selfAddress;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public PersistencePluginProxy(Config config)
        {
            _config = config;
            var pluginId = Self.Path.Name;
            if (pluginId.Equals("akka.persistence.journal.proxy"))
                _pluginType = new Journal();
            else if (pluginId.Equals("akka.persistence.snapshot-store.proxy"))
                _pluginType = new SnapshotStore();
            else
                throw new ArgumentException(string.Format("Unknown plugin type: {0}.", pluginId));
            _initTimeout = config.GetTimeSpan("init-timeout");
            var key = "target-" + _pluginType.Qualifier + "-plugin";
            _targetPluginId = config.GetString(key);
            if (string.IsNullOrEmpty(_targetPluginId))
                throw new ArgumentException(string.Format("{0}.{1} must be defined.", pluginId, key));
            _startTarget = config.GetBoolean("start-target-" + _pluginType.Qualifier);

            _selfAddress = ((ExtendedActorSystem) Context.System).Provider.DefaultAddress;
        }

        public IStash Stash { get; set; }

        protected override void PreStart()
        {
            if (_startTarget)
            {
                IActorRef target = null;
                if (_pluginType is Journal)
                {
                    if (_log.IsInfoEnabled)
                        _log.Info("Starting target journal [{0}]", _targetPluginId);
                    target = Persistence.Instance.Apply(Context.System).JournalFor(_targetPluginId);
                }
                else if (_pluginType is SnapshotStore)
                {
                    if (_log.IsInfoEnabled)
                        _log.Info("Starting target snapshot-store [{0}]", _targetPluginId);
                    target = Persistence.Instance.Apply(Context.System).SnapshotStoreFor(_targetPluginId);
                }
                Context.Become(Active(target, true));
            }
            else
            {
                var targetAddressKey = "target-" + _pluginType.Qualifier + "-address";
                var targetAddress = _config.GetString(targetAddressKey);
                if (!string.IsNullOrEmpty(targetAddress))
                {
                    try
                    {
                        if (_log.IsInfoEnabled)
                            _log.Info("Setting target {0} address to {1}", _pluginType.Qualifier, targetAddress);
                        SetTargetLocation(Context.System, Address.Parse(targetAddress));
                    }
                    catch (UriFormatException)
                    {
                        if (_log.IsWarningEnabled)
                            _log.Warning("Invalid URL provided for target {0} address: {1}", _pluginType.Qualifier,
                                targetAddress);
                    }
                }
                Context.System.Scheduler.ScheduleTellOnce(_initTimeout, Self, InitTimeout.Instance, Self);
            }
            base.PreStart();
        }

        private TimeoutException TimeoutException()
        {
            return
                new TimeoutException(
                    string.Format(
                        "Target {0} not initialized. Use `PersistencePluginProxy.SetTargetLocation` or set `target-{0}-address`.",
                        _pluginType.Qualifier));
        }

        protected override bool Receive(object message)
        {
            return Init(message);
        }

        private bool Init(object message)
        {
            if (message is TargetLocation)
            {
                Context.SetReceiveTimeout(TimeSpan.FromSeconds(1)); // for retries
                Context.Become(Identifying(((TargetLocation)message).Address));
            }
            else if (message is InitTimeout)
            {
                if (_log.IsInfoEnabled)
                    _log.Info(
                        "Initialization timed-out (after {0}s), use `PersistencePluginProxy.SetTargetLocation` or set `target-{1}-address`",
                        _initTimeout.TotalSeconds, _pluginType.Qualifier);
                Context.Become(InitTimedOut());
                Stash.UnstashAll(); // will trigger appropriate failures
            }
            else if (message is Terminated)
            {
            }
            else
                Stash.Stash();
            return true;
        }

        private void BecomeIdentifying(Address address)
        {
            SendIdentify(address);
            Context.SetReceiveTimeout(TimeSpan.FromSeconds(1)); // for retries
            Context.Become(Identifying(address));
        }

        private void SendIdentify(Address address)
        {
            var sel = Context.ActorSelection(string.Format("{0}/system/{1}", new RootActorPath(address), _targetPluginId));
            if (_log.IsInfoEnabled)
                _log.Info("Trying to identify target + {0} at {1}", _pluginType.Qualifier, sel);
            sel.Tell(new Identify(_targetPluginId));
        }

        private Receive Identifying(Address address)
        {
            return message =>
            {
                if (message is ActorIdentity)
                {
                    var ai = (ActorIdentity) message;
                    if (_targetPluginId.Equals(ai.MessageId))
                    {
                        var target = ai.Subject;
                        if (_log.IsInfoEnabled)
                            _log.Info("Found target {0} at [{1}]", _pluginType.Qualifier, address);
                        Context.SetReceiveTimeout(null);
                        Context.Watch(target);
                        Stash.UnstashAll();
                        Context.Become(Active(target, address.Equals(_selfAddress)));
                    }
                    else
                    {
                        // will retry after ReceiveTimeout
                    }
                }
                else if (message is Terminated)
                {
                }
                else if (message is ReceiveTimeout)
                    SendIdentify(address);
                else return Init(message);
                return true;
            };
        }

        private Receive Active(IActorRef targetJournal, bool targetAtThisNode)
        {
            return message =>
            {
                if (message is TargetLocation)
                {
                    var address = ((TargetLocation) message).Address;
                    if (targetAtThisNode && !address.Equals(_selfAddress))
                        BecomeIdentifying(address);
                }
                else if (message is Terminated)
                {
                    var t = (Terminated) message;
                    if (t.ActorRef.Equals(targetJournal))
                    {
                        Context.Unwatch(targetJournal);
                        Context.Become(InitTimedOut());
                    }
                }
                else if (message is InitTimeout)
                {
                }
                else
                    targetJournal.Forward(message);
                return true;
            };
        }

        private Receive InitTimedOut()
        {
            return message =>
            {
                if (message is IJournalRequest)
                {
                     // exhaustive match
                    if (message is WriteMessages)
                    {
                        var w = (WriteMessages) message;
                        w.PersistentActor.Tell(new WriteMessagesFailed(TimeoutException()));
                        foreach (var m in w.Messages)
                        {
                            if (m is AtomicWrite)
                            {
                                foreach (var p in (IEnumerable<IPersistentRepresentation>) m.Payload)
                                {
                                    w.PersistentActor.Tell(new WriteMessageFailure(p, TimeoutException(),
                                        w.ActorInstanceId));
                                }
                            }
                            else if (m is NonPersistentMessage)
                            {
                                w.PersistentActor.Tell(new LoopMessageSuccess(m.Payload, w.ActorInstanceId));
                            }
                        }
                    }
                    else if (message is ReplayMessages)
                    {
                        var r = (ReplayMessages) message;
                        r.PersistentActor.Tell(new ReplayMessagesFailure(TimeoutException()));
                    }
                    else if (message is DeleteMessagesTo)
                    {
                        var d = (DeleteMessagesTo) message;
                        d.PersistentActor.Tell(new DeleteMessagesFailure(TimeoutException(), d.ToSequenceNr));
                    }
                }
                else if (message is ISnapshotRequest)
                {
                    // exhaustive match
                    if (message is LoadSnapshot)
                    {
                        var l = (LoadSnapshot) message;
                        Sender.Tell(new LoadSnapshotResult(null, l.ToSequenceNr));
                    }
                    else if (message is SaveSnapshot)
                    {
                        var s = (SaveSnapshot) message;
                        Sender.Tell(new SaveSnapshotFailure(s.Metadata, TimeoutException()));
                    }
                    else if (message is DeleteSnapshot)
                    {
                        var d = (DeleteSnapshot) message;
                        Sender.Tell(new DeleteSnapshotFailure(d.Metadata, TimeoutException()));
                    }
                    else if (message is DeleteSnapshots)
                    {
                        var d = (DeleteSnapshots) message;
                        Sender.Tell(new DeleteSnapshotsFailure(d.Criteria, TimeoutException()));
                    }
                }
                else if (message is TargetLocation)
                {
                    BecomeIdentifying(((TargetLocation) message).Address);
                }
                else if (message is Terminated)
                {
                }
                else
                {
                    var exception = TimeoutException();
                    if (_log.IsErrorEnabled)
                        _log.Error(exception, "Failed PersistencePluginProxyRequest: {0}", exception.Message);
                }
                return true;
            };
        }
    }

    /// <summary>
    /// <see cref="PersistencePluginProxyExtension"/> is an <see cref="IExtension"/> that enables initialization
    /// of the <see cref="PersistencePluginProxy"/> via configuration, without requiring any code changes or the
    /// creation of any actors.
    /// </summary>
    public class PersistencePluginProxyExtension : ExtensionIdProvider<PersistencePluginProxyExtension>, IExtension
    {
        public PersistencePluginProxyExtension(ActorSystem system)
        {
            PersistencePluginProxy.Start(system);
        }

        public override PersistencePluginProxyExtension CreateExtension(ExtendedActorSystem system)
        {
            return new PersistencePluginProxyExtension(system);
        }
    }
}
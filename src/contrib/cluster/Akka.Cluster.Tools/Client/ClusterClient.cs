//-----------------------------------------------------------------------
// <copyright file="ClusterClient.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Discovery;
using Akka.Event;
using Akka.Remote;
using Akka.Util.Internal;

#nullable enable
namespace Akka.Cluster.Tools.Client;

/// <summary>
/// Marker interface for all ClusterClient message types
/// </summary>
public interface IClusterClientProtocolMessage
{
}
        
/// <summary>
/// This actor is intended to be used on an external node that is not member
/// of the cluster. It acts like a gateway for sending messages to actors
/// somewhere in the cluster. From the initial contact points it will establish
/// a connection to a <see cref="ClusterReceptionist"/> somewhere in the cluster. It will
/// monitor the connection to the receptionist and establish a new connection if
/// the link goes down. When looking for a new receptionist it uses fresh contact
/// points retrieved from previous establishment, or periodically refreshed
/// contacts, i.e. not necessarily the initial contact points.
/// </summary>
public sealed class ClusterClient : ActorBase
{
    #region Messages

    /// <summary>
    /// The message will be delivered to one recipient with a matching path, if any such
    /// exists. If several entries match the path the message will be delivered
    /// to one random destination. The sender of the message can specify that local
    /// affinity is preferred, i.e. the message is sent to an actor in the same local actor
    /// system as the used receptionist actor, if any such exists, otherwise random to any other
    /// matching entry.
    /// </summary>
    [Serializable]
    public sealed class Send: IClusterClientProtocolMessage, IEquatable<Send>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public string Path { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public object Message { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public bool LocalAffinity { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="localAffinity">TBD</param>
        public Send(string path, object message, bool localAffinity = false)
        {
            Path = path;
            Message = message;
            LocalAffinity = localAffinity;
        }

        public bool Equals(Send? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Path == other.Path && Equals(Message, other.Message) && LocalAffinity == other.LocalAffinity;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is Send other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Path != null ? Path.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Message != null ? Message.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ LocalAffinity.GetHashCode();
                return hashCode;
            }
        }
    }

    /// <summary>
    /// The message will be delivered to all recipients with a matching path.
    /// </summary>
    [Serializable]
    public sealed class SendToAll: IClusterClientProtocolMessage, IEquatable<SendToAll>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public string Path { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="message">TBD</param>
        public SendToAll(string path, object message)
        {
            Path = path;
            Message = message;
        }

        public bool Equals(SendToAll? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Path == other.Path && Equals(Message, other.Message);
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is SendToAll other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Path != null ? Path.GetHashCode() : 0) * 397) ^ (Message != null ? Message.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    /// The message will be delivered to all recipients Actors that have been registered as subscribers to
    /// to the named topic.
    /// </summary>
    [Serializable]
    public sealed class Publish: IClusterClientProtocolMessage, IEquatable<Publish>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public string Topic { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="topic">TBD</param>
        /// <param name="message">TBD</param>
        public Publish(string topic, object message)
        {
            Topic = topic;
            Message = message;
        }

        public bool Equals(Publish? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Topic == other.Topic && Equals(Message, other.Message);
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is Publish other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Topic != null ? Topic.GetHashCode() : 0) * 397) ^ (Message != null ? Message.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class RefreshContactsTick: IClusterClientProtocolMessage, IEquatable<RefreshContactsTick>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static RefreshContactsTick Instance { get; } = new();
        private RefreshContactsTick() { }
            
        public bool Equals(RefreshContactsTick? other) => other != null;

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is RefreshContactsTick;
        }

        public override int GetHashCode() => 0;
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class HeartbeatTick: IClusterClientProtocolMessage, IEquatable<HeartbeatTick>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static HeartbeatTick Instance { get; } = new();
        private HeartbeatTick() { }

        public bool Equals(HeartbeatTick? other) => other != null;

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is HeartbeatTick;
        }

        public override int GetHashCode() => 0;
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    internal sealed class ReconnectTimeout: IClusterClientProtocolMessage, IEquatable<ReconnectTimeout>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static ReconnectTimeout Instance { get; } = new();
        private ReconnectTimeout() { }

        public bool Equals(ReconnectTimeout? other) => other != null;

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is ReconnectTimeout;
        }

        public override int GetHashCode() => 0;
    }

    #region Discovery messages

    internal sealed class DiscoverTick: IEquatable<DiscoverTick>
    {
        public static readonly DiscoverTick Instance = new();

        private DiscoverTick() { }
        public bool Equals(DiscoverTick? other) => other is not null;
        public override bool Equals(object? obj) => ReferenceEquals(this, obj) || obj is DiscoverTick;
        public override int GetHashCode() => 0;
    }

    internal sealed record DiscoveryFailure(Exception Cause);

    #endregion
    
    #endregion

    private sealed class Contact: IEquatable<Contact>
    {
        public ActorPath Path { get; }
        public ActorSelection Selection { get; }
        public string Id { get; }
        
        public Contact(ActorPath path, ActorSelection selection)
        {
            Path = path;
            Selection = selection;
            Id = path.Address.ToString();
        }

        public bool Equals(Contact? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Id == other.Id;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is Contact other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
    
    /// <summary>
    /// Factory method for <see cref="ClusterClient"/> <see cref="Actor.Props"/>.
    /// </summary>
    /// <param name="settings">TBD</param>
    /// <exception cref="ArgumentNullException">
    /// This exception is thrown when the specified <paramref name="settings"/> is undefined.
    /// </exception>
    /// <returns>TBD</returns>
    public static Props Props(ClusterClientSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        return Actor.Props.Create(() => new ClusterClient(settings)).WithDeploy(Deploy.Local);
    }

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly EventStream _eventStream = Context.System.EventStream;
    private readonly ClusterClientSettings _settings;
    private readonly DeadlineFailureDetector _failureDetector;
    private readonly ImmutableHashSet<Contact> _initialContactsSelections;
    private ImmutableHashSet<Contact> _contacts;
    private ImmutableHashSet<Contact> _contactsPublished;
    private ImmutableList<IActorRef> _subscribers;
    private readonly ICancelable _heartbeatTask;
    private ICancelable? _refreshContactsCancelable;
    private readonly Queue<(object, IActorRef)> _buffer;

    private readonly ClusterClientDiscoverySettings _discoverySettings;
    private readonly ServiceDiscovery? _serviceDiscovery;
    private readonly Lookup? _lookup;
    private readonly TimeSpan _discoveryTimeout = TimeSpan.Zero;
    private ICancelable? _discoveryCancelable;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterClient" /> class.
    /// </summary>
    /// <param name="settings">The settings used to configure the client.</param>
    /// <exception cref="ArgumentException">
    /// This exception is thrown when the settings contain no initial contacts and initial contact discovery was not used.
    /// </exception>
    public ClusterClient(ClusterClientSettings settings)
    {
        _settings = settings;
        _discoverySettings = settings.DiscoverySettings;

        if (_settings.UseLegacySerialization)
        {
            Context.System.Serialization.RemoveSerializationMap(typeof(IClusterClientProtocolMessage));
        }
            
        _failureDetector = new DeadlineFailureDetector(_settings.AcceptableHeartbeatPause, _settings.HeartbeatInterval);

        if (_settings.UseInitialContactDiscovery)
        {
            // UseInitialContactDiscovery was set, we will use it to discover potential contacts
            if(_settings.InitialContacts.Count > 0)
                _log.Warning("Initial contacts is being ignored because ClusterClient contacts discovery is being used");

            var discoveryMethod = _discoverySettings.DiscoveryMethod;
            if(string.IsNullOrWhiteSpace(discoveryMethod) || discoveryMethod == "<method>")
                discoveryMethod = Context.System.Settings.Config.GetString("akka.discovery.method");
            if (string.IsNullOrWhiteSpace(discoveryMethod) || discoveryMethod == "<method>")
            {
                _log.Warning(
                    "No default initial contacts discovery implementation configured in both\n" +
                    "`akka.cluster.client.discovery.method` and `akka.discovery.method`.\n" +
                    "Make sure to configure this setting to your preferred implementation such as 'config'\n" +
                    "in your application.conf (from the akka-discovery module). Falling back to default config\n" +
                    "based discovery method");
                discoveryMethod = "config";
            }

            _lookup = new Lookup(_discoverySettings.ServiceName, _discoverySettings.PortName);
            _serviceDiscovery = Discovery.Discovery.Get(Context.System)
                .LoadServiceDiscovery(discoveryMethod);
            _discoveryTimeout = _discoverySettings.DiscoveryTimeout;
            
            _initialContactsSelections = ImmutableHashSet<Contact>.Empty;
            _contacts = ImmutableHashSet<Contact>.Empty;
            _contactsPublished = ImmutableHashSet<Contact>.Empty;
            
            Self.Tell(DiscoverTick.Instance);
            Rediscover();
        }
        else
        {
            // UseInitialContactDiscovery was NOT set, use the InitialContacts to retrieve receptionist contacts
            if (_settings.InitialContacts.Count == 0)
                throw new ArgumentException("Initial contacts for cluster client cannot be empty if ClusterClient contacts discovery is not being used.");

            _initialContactsSelections = settings.InitialContacts
                .Select(ap => new Contact(ap, Context.ActorSelection(ap))).ToImmutableHashSet();
            _contacts = _initialContactsSelections;
            _contactsPublished = _contacts;

            SendGetContacts();
            Become(Establishing);
        }

        _subscribers = ImmutableList<IActorRef>.Empty;

        _heartbeatTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            settings.HeartbeatInterval,
            settings.HeartbeatInterval,
            Self,
            HeartbeatTick.Instance,
            Self);

        ScheduleRefreshContactsTick(settings.EstablishingGetContactsInterval);
        Self.Tell(RefreshContactsTick.Instance);

        _buffer = new Queue<(object, IActorRef)>();
    }

    private void ScheduleRefreshContactsTick(TimeSpan interval)
    {
        if (_refreshContactsCancelable != null)
        {
            _refreshContactsCancelable.Cancel();
            _refreshContactsCancelable = null;
        }

        _refreshContactsCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            interval,
            interval,
            Self,
            RefreshContactsTick.Instance,
            Self);
    }

    protected override void PreStart()
    {
        base.PreStart();
        _eventStream.Subscribe(Self, typeof(DeadLetter));
    }

    /// <summary>
    /// TBD
    /// </summary>
    protected override void PostStop()
    {
        base.PostStop();
        _eventStream.Unsubscribe(Self);
        _heartbeatTask.Cancel();

        _refreshContactsCancelable?.Cancel();
        _refreshContactsCancelable = null;
        
        _discoveryCancelable?.Cancel();
        _discoveryCancelable = null;
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="message">TBD</param>
    /// <returns>TBD</returns>
    protected override bool Receive(object message)
    {
        throw new Exception("Should never reach this code");
    }

    private ActorPath ResolvedTargetToReceptionistActorPath(ServiceDiscovery.ResolvedTarget target)
    {
        var actorSystemName = string.IsNullOrWhiteSpace(_discoverySettings.ActorSystemName) 
            ? Context.System.Name : _discoverySettings.ActorSystemName;
        var networkAddress = string.IsNullOrWhiteSpace(target.Host) ? target.Address.ToString() : target.Host;
        var port = target.Port.HasValue ? $":{target.Port}" : string.Empty;
        return ActorPath.Parse(
            $"akka.tcp://{ actorSystemName }@{ networkAddress }{ port }/system/{ _discoverySettings.ReceptionistName }"); 
    }
    
    private bool Discovering(object message)
    {
        switch (message)
        {
            case DiscoverTick:
                Rediscover();
                return true;
            
            case ServiceDiscovery.Resolved resolved:
                _discoveryCancelable?.Cancel();
                _discoveryCancelable = null;
                
                if (resolved.Addresses.Count == 0)
                {
                    // discovery didn't find any contacts, retry discovery
                    _discoveryCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(
                        delay: _settings.DiscoverySettings.DiscoveryRetryInterval, 
                        receiver: Self,
                        message: DiscoverTick.Instance,
                        sender: Self);
                    return true;
                }

                _contacts = resolved.Addresses.Select(address => {
                        var path = ResolvedTargetToReceptionistActorPath(address);
                        return new Contact(path, Context.ActorSelection(path));
                    }).ToImmutableHashSet();
                
                Reestablish();
                return true;
            
            case DiscoveryFailure fail:
                _log.Warning(fail.Cause, "Cluster.Client contact point service discovery phase failed, will try again.");
                _discoveryCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(
                    delay: _settings.DiscoverySettings.DiscoveryRetryInterval, 
                    receiver: Self,
                    message: DiscoverTick.Instance,
                    sender: Self);
                return true;
            
            case DeadLetter:
                // ignored, we're re-discovering
                return true;
            
            case ClusterReceptionist.Contacts:
                // ignored, we're re-discovering
                return true;
                
            case HeartbeatTick:
                // keep the failure detector happy while we're discovering
                _failureDetector.HeartBeat();
                return true;
                
            case RefreshContactsTick:
                // no-op
                return true;
                
            case Send send:
                Buffer(new PublishSubscribe.Send(send.Path, send.Message, send.LocalAffinity));
                return true;
                
            case SendToAll sendToAll:
                Buffer(new PublishSubscribe.SendToAll(sendToAll.Path, sendToAll.Message));
                return true;
                
            case Publish publish:
                Buffer(new PublishSubscribe.Publish(publish.Topic, publish.Message));
                return true;
                
            case ReconnectTimeout:
                // we're re-discovering, so this is fine.
                return true;
                
            case ClusterReceptionist.ReceptionistShutdown:
                // ok, we're re-discovering and haven't chosen a new receptionist yet
                return true;
                
            default:
                return ContactPointMessages(message);
        }
    }

    private bool Establishing(object message)
    {
        ICancelable? connectTimerCancelable = null;
        if (_settings.ReconnectTimeout.HasValue)
        {
            connectTimerCancelable = Context.System.Scheduler.ScheduleTellOnceCancelable(
                _settings.ReconnectTimeout.Value,
                Self,
                ReconnectTimeout.Instance,
                Self);
        }

        switch (message)
        {
            case ServiceDiscovery.Resolved:
                // Ignored, we're already in establishing state
                return true;
            
            case DeadLetter { Message: ClusterReceptionist.GetContacts } dl:
                PruneContacts(dl.Recipient.Path.Address.ToString());
                if(_initialContactsSelections.Count == 0 && _contacts.Count == 0)
                    Rediscover();
                return true;
            
            case ClusterReceptionist.Contacts contacts:
            {
                if (contacts.ContactPoints.Count == 0)
                    return true;

                MergeContacts(contacts);
                return true;
            }
                
            case ActorIdentity actorIdentify:
            {
                var receptionist = actorIdentify.Subject;

                if (receptionist != null)
                {
                    _log.Info("Connected to [{0}]", receptionist.Path);
                    ScheduleRefreshContactsTick(_settings.RefreshContactsInterval);
                    SendBuffered(receptionist);
                    Context.Become(Active(receptionist));
                    connectTimerCancelable?.Cancel();
                    _failureDetector.HeartBeat();
                    Self.Tell(HeartbeatTick.Instance); // will register us as active client of the selected receptionist
                }
                else
                {
                    // prune out actors that failed to be identified
                    PruneContacts((string) actorIdentify.MessageId);
                    if(_initialContactsSelections.Count == 0 && _contacts.Count == 0)
                        Rediscover();
                }

                return true;
            }
                
            case HeartbeatTick:
                _failureDetector.HeartBeat();
                return true;
                
            case RefreshContactsTick:
                SendGetContacts();
                return true;
                
            case Send send:
                Buffer(new PublishSubscribe.Send(send.Path, send.Message, send.LocalAffinity));
                return true;
                
            case SendToAll sendToAll:
                Buffer(new PublishSubscribe.SendToAll(sendToAll.Path, sendToAll.Message));
                return true;
                
            case Publish publish:
                Buffer(new PublishSubscribe.Publish(publish.Topic, publish.Message));
                return true;
                
            case ReconnectTimeout:
                _log.Warning("Receptionist reconnect not successful within {0} stopping cluster client", _settings.ReconnectTimeout);
                Context.Stop(Self);
                return true;
                
            case ClusterReceptionist.ReceptionistShutdown:
                // ok, haven't chosen a receptionist yet
                return true;
                
            default:
                return ContactPointMessages(message);
        }
    }

    private Receive Active(IActorRef receptionist)
    {
        return message =>
        {
            switch (message)
            {
                case Send send:
                    receptionist.Forward(new PublishSubscribe.Send(send.Path, send.Message, send.LocalAffinity));
                    return true;
                    
                case SendToAll sendToAll:
                    receptionist.Forward(new PublishSubscribe.SendToAll(sendToAll.Path, sendToAll.Message));
                    return true;
                    
                case Publish publish:
                    receptionist.Forward(new PublishSubscribe.Publish(publish.Topic, publish.Message));
                    return true;
                    
                case HeartbeatTick when !_failureDetector.IsAvailable:
                    _log.Info("Lost contact with [{0}], reestablishing connection", receptionist);
                    PruneContacts(receptionist.Path.Address.ToString());
                    if(_initialContactsSelections.Count == 0 && _contacts.Count == 0)
                    {
                        Rediscover();
                    }
                    else
                    {
                        Reestablish();
                    }
                    return true;
                    
                case HeartbeatTick:
                    receptionist.Tell(ClusterReceptionist.Heartbeat.Instance);
                    return true;
                    
                case ClusterReceptionist.HeartbeatRsp:
                    _failureDetector.HeartBeat();
                    return true;
                    
                case RefreshContactsTick:
                    receptionist.Tell(ClusterReceptionist.GetContacts.Instance);
                    return true;
                    
                case ClusterReceptionist.Contacts contacts:
                {
                    if (contacts.ContactPoints.Count == 0)
                        return true;
                    
                    // refresh of contacts
                    MergeContacts(contacts);
                    return true;
                }
                    
                case ActorIdentity actorIdentify:
                    // prune out actors that failed to be identified
                    if (actorIdentify.Subject is null)
                    {
                        PruneContacts((string)actorIdentify.MessageId);
                        if(_initialContactsSelections.Count == 0 && _contacts.Count == 0)
                            Rediscover();
                    }

                    return true;
                    
                case ClusterReceptionist.ReceptionistShutdown:
                {
                    if (receptionist.Equals(Sender))
                    {
                        _log.Info("Receptionist [{0}] is shutting down, reestablishing connection", receptionist);
                        PruneContacts(Sender.Path.Address.ToString());
                        if(_initialContactsSelections.Count == 0 && _contacts.Count == 0)
                        {
                            Rediscover();
                        }
                        else
                        {
                            Reestablish();
                        }
                    }

                    return true;
                }
                    
                default:
                    return ContactPointMessages(message);
            }
        };
    }

    private void MergeContacts(ClusterReceptionist.Contacts contacts)
    {
        var receivedContacts = contacts.ContactPoints.Select(cp =>
        {
            var path = ActorPath.Parse(cp);
            return new Contact(path, Context.ActorSelection(path));
        }).ToArray();
        _contacts = _contacts.Union(receivedContacts);
        
        _contacts.ForEach(c => c.Selection.Tell(new Identify(c.Id)));

        PublishContactPoints();
    }
    
    private void PruneContacts(string id)
    {
        var foundItem = _contacts.FirstOrDefault(s => s.Id == id);
        if (foundItem is not null)
            _contacts = _contacts.Remove(foundItem);
                
        PublishContactPoints();
    }
    
    private void Rediscover()
    {
        Become(Discovering);
        _serviceDiscovery!.Lookup(_lookup, _discoveryTimeout)
            .PipeTo(Self, failure: cause => new DiscoveryFailure(cause));
    }
    
    private void Reestablish()
    {
        SendGetContacts();
        ScheduleRefreshContactsTick(_settings.EstablishingGetContactsInterval);
        Context.Become(Establishing);
        _failureDetector.HeartBeat();
    }

    private bool ContactPointMessages(object message)
    {
        switch (message)
        {
            case SubscribeContactPoints:
            {
                var subscriber = Sender;
                subscriber.Tell(new ContactPoints(_contacts.Select(c => c.Path).ToImmutableHashSet()));
                _subscribers = _subscribers.Add(subscriber);
                Context.Watch(subscriber);
                return true;
            }
                
            case UnsubscribeContactPoints:
            {
                var subscriber = Sender;
                _subscribers = _subscribers.Where(c => !c.Equals(subscriber)).ToImmutableList();
                return true;
            }
                
            case Terminated terminated:
                Self.Tell(UnsubscribeContactPoints.Instance, terminated.ActorRef);
                return true;
                
            case GetContactPoints:
                Sender.Tell(new ContactPoints(_contacts.Select(c => c.Path).ToImmutableHashSet()));
                return true;
                
            default:
                return false;
        }
    }

    private void SendGetContacts()
    {
        var sendTo = _contacts.Count switch
        {
            0 => _initialContactsSelections,
            1 => _initialContactsSelections.Union(_contacts),
            _ => _contacts
        };

        if (sendTo.Count == 0)
        {
            _log.Debug("Could not send GetContacts, _initialContactsSelections and _contacts are empty.");
            return;
        }

        if (_log.IsDebugEnabled)
            _log.Debug("Sending GetContacts to [{0}]", string.Join(", ", sendTo.Select(c => c.Path).AsEnumerable()));

        sendTo.ForEach(c => c.Selection.Tell(ClusterReceptionist.GetContacts.Instance));
    }

    private void Buffer(object message)
    {
        if (_settings.BufferSize == 0)
        {
            _log.Warning("Receptionist not available and buffering is disabled, dropping message [{0}]", message.GetType().Name);
        }
        else if (_buffer.Count == _settings.BufferSize)
        {
            var m = _buffer.Dequeue();
            _log.Warning("Receptionist not available, buffer is full, dropping first message [{0}]", m.Item1.GetType().Name);
            _buffer.Enqueue((message, Sender));
        }
        else
        {
            if(_log.IsDebugEnabled) // don't invoke reflection call on message type if we don't have to
                _log.Debug("Receptionist not available, buffering message type [{0}]", message.GetType().Name);
            _buffer.Enqueue((message, Sender));
        }
    }

    private void SendBuffered(IActorRef receptionist)
    {
        _log.Debug("Sending buffered messages to receptionist");
        while (_buffer.Count != 0)
        {
            var t = _buffer.Dequeue();
            receptionist.Tell(t.Item1, t.Item2);
        }
    }

    private void PublishContactPoints()
    {
        foreach (var cp in _contacts)
        {
            if (!_contactsPublished.Contains(cp))
            {
                var contactPointAdded = new ContactPointAdded(cp.Path);
                _subscribers.ForEach(s => s.Tell(contactPointAdded));
            }
        }

        foreach (var cp in _contactsPublished)
        {
            if (!_contacts.Contains(cp))
            {
                var contactPointRemoved = new ContactPointRemoved(cp.Path);
                _subscribers.ForEach(s => s.Tell(contactPointRemoved));
            }
        }

        _contactsPublished = _contacts;
    }
}

/// <summary>
/// Declares a super type for all events emitted by the `ClusterClient`
/// in relation to contact points being added or removed.
/// </summary>
public interface IContactPointChange
{
    /// <summary>
    /// TBD
    /// </summary>
    ActorPath ContactPoint { get; }
}

/// <summary>
/// Emitted to a subscriber when contact points have been
/// received by the <see cref="ClusterClient"/> and a new one has been added.
/// </summary>
public sealed class ContactPointAdded : IContactPointChange
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="contactPoint">TBD</param>
    public ContactPointAdded(ActorPath contactPoint)
    {
        ContactPoint = contactPoint;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public ActorPath ContactPoint { get; }
}

/// <summary>
/// Emitted to a subscriber when contact points have been
/// received by the <see cref="ClusterClient"/> and a new one has been added.
/// </summary>
public sealed class ContactPointRemoved : IContactPointChange
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="contactPoint">TBD</param>
    public ContactPointRemoved(ActorPath contactPoint)
    {
        ContactPoint = contactPoint;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public ActorPath ContactPoint { get; }
}

/// <summary>
/// TBD
/// </summary>
public interface ISubscribeContactPoints
{
}

/// <summary>
/// Subscribe to a cluster client's contact point changes where
/// it is guaranteed that a sender receives the initial state
/// of contact points prior to any events in relation to them
/// changing.
/// The sender will automatically become unsubscribed when it
/// terminates.
/// </summary>
public sealed class SubscribeContactPoints : ISubscribeContactPoints
{
    /// <summary>
    /// TBD
    /// </summary>
    public static readonly SubscribeContactPoints Instance = new();
    private SubscribeContactPoints() { }
}

/// <summary>
/// TBD
/// </summary>
public interface IUnsubscribeContactPoints
{
}

/// <summary>
/// Explicitly unsubscribe from contact point change events.
/// </summary>
public sealed class UnsubscribeContactPoints : IUnsubscribeContactPoints
{
    /// <summary>
    /// TBD
    /// </summary>
    public static readonly UnsubscribeContactPoints Instance = new();
    private UnsubscribeContactPoints() { }
}

/// <summary>
/// TBD
/// </summary>
public interface IGetContactPoints
{
}

/// <summary>
/// Get the contact points known to this client. A <see cref="ContactPoints"/> message
/// will be replied.
/// </summary>
public sealed class GetContactPoints : IGetContactPoints
{
    /// <summary>
    /// The singleton instance of this message.
    /// </summary>
    public static readonly GetContactPoints Instance = new();
    private GetContactPoints() { }
}

/// <summary>
/// The reply to <see cref="GetContactPoints"/>.
/// </summary>
public sealed class ContactPoints
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <param name="contactPoints">TBD</param>
    public ContactPoints(IImmutableSet<ActorPath> contactPoints)
    {
        ContactPointsList = contactPoints;
    }

    /// <summary>
    /// The set of actor paths contacted by this <see cref="ClusterClient"/>.
    /// </summary>
    public IImmutableSet<ActorPath> ContactPointsList { get; }
}
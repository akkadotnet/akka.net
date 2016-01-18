//-----------------------------------------------------------------------
// <copyright file="ClusterClient.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Remote;

namespace Akka.Cluster.Tools.Client
{
    /// <summary>
    /// <para>
    /// This actor is intended to be used on an external node that is not member
    /// of the cluster. It acts like a gateway for sending messages to actors
    /// somewhere in the cluster. From the initial contact points it will establish
    /// a connection to a <see cref="ClusterReceptionist"/> somewhere in the cluster. It will
    /// monitor the connection to the receptionist and establish a new connection if
    /// the link goes down. When looking for a new receptionist it uses fresh contact
    /// points retrieved from previous establishment, or periodically refreshed
    /// contacts, i.e. not necessarily the initial contact points.
    /// </para>
    /// <para>
    /// You can send messages via the `ClusterClient` to any actor in the cluster
    /// that is registered in the <see cref="ClusterReceptionist"/>.
    /// Messages are wrapped in <see cref="ClusterClient.Send"/>, <see cref="ClusterClient.SendToAll"/>
    /// or <see cref="ClusterClient.Publish"/>.
    /// </para>
    /// <para>
    /// 1. <see cref="ClusterClient.Send"/> -
    /// The message will be delivered to one recipient with a matching path, if any such
    /// exists. If several entries match the path the message will be delivered
    /// to one random destination. The sender of the message can specify that local
    /// affinity is preferred, i.e. the message is sent to an actor in the same local actor
    /// system as the used receptionist actor, if any such exists, otherwise random to any other
    /// matching entry.
    /// </para>
    /// <para>
    /// 2. <see cref="ClusterClient.SendToAll"/> -
    /// The message will be delivered to all recipients with a matching path.
    /// </para>
    /// <para>
    /// 3. <see cref="ClusterClient.Publish"/> -
    /// The message will be delivered to all recipients Actors that have been registered as subscribers to
    /// to the named topic.
    /// </para>
    /// <para>
    /// Use the factory method <see cref="ClusterClient.Props"/> to create the <see cref="Akka.Actor.Props"/> for the actor.
    /// </para>
    /// </summary>
    public class ClusterClient : ActorBase
    {
        #region Messages

        [Serializable]
        public sealed class Send
        {
            public readonly string Path;
            public readonly object Message;
            public readonly bool LocalAffinity;

            public Send(string path, object message, bool localAffinity = false)
            {
                Path = path;
                Message = message;
                LocalAffinity = localAffinity;
            }
        }

        [Serializable]
        public sealed class SendToAll
        {
            public readonly string Path;
            public readonly object Message;

            public SendToAll(string path, object message)
            {
                Path = path;
                Message = message;
            }
        }

        [Serializable]
        public sealed class Publish
        {
            public readonly string Topic;
            public readonly object Message;

            public Publish(string topic, object message)
            {
                Topic = topic;
                Message = message;
            }
        }

        internal enum InternalMessage
        {
            RefreshContactsTick,
            HeartbeatTick
        }

        #endregion

        /// <summary>
        /// Factory method for <see cref="ClusterClient"/> <see cref="Actor.Props"/>.
        /// </summary>
        public static Actor.Props Props(ClusterClientSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");

            return Actor.Props.Create(() => new ClusterClient(settings)).WithDeploy(Deploy.Local);
        }

        public readonly ClusterClientSettings Settings;
        private readonly DeadlineFailureDetector _failureDetector;
        private readonly ActorSelection[] _initialContactsSelections;
        private readonly ICancelable _heartbeatCancelable;
        private readonly Queue<Tuple<object, IActorRef>> _buffer;

        private ILoggingAdapter _log;
        private ActorSelection[] _contacts;
        private ICancelable _refreshContactsCancelable = null;

        public ClusterClient(ClusterClientSettings settings)
        {
            if (!settings.InitialContacts.Any()) throw new ArgumentException("Initial contacts for cluster client cannot be empty");
            Settings = settings;
            _failureDetector = new DeadlineFailureDetector(Settings.AcceptableHeartbeatPause, () => Settings.HeartbeatInterval.Ticks);
            _initialContactsSelections = settings.InitialContacts.Select(Context.ActorSelection).ToArray();
            _contacts = _initialContactsSelections;
            _buffer = new Queue<Tuple<object, IActorRef>>();

            SendGetContacts();

            _heartbeatCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                settings.HeartbeatInterval, settings.HeartbeatInterval, Self, InternalMessage.HeartbeatTick, Self);

            ScheduleRefreshContactsTick(settings.EstablishingGetContactsInterval);
            Self.Tell(InternalMessage.RefreshContactsTick);
        }

        public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

        private void ScheduleRefreshContactsTick(TimeSpan interval)
        {
            if (_refreshContactsCancelable != null) _refreshContactsCancelable.Cancel();

            _refreshContactsCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(interval,
                    interval, Self, InternalMessage.RefreshContactsTick, Self);
        }

        protected override void PostStop()
        {
            base.PostStop();
            _heartbeatCancelable.Cancel();
            if (_refreshContactsCancelable != null)
            {
                _refreshContactsCancelable.Cancel();
                _refreshContactsCancelable = null;
            }
        }

        protected override bool Receive(object message)
        {
            return Establishing(message);
        }

        private bool Establishing(object message)
        {
            return message.Match()
                .With<ClusterReceptionist.Contacts>(msg =>
                {
                    if (msg.ContactPoints.Length != 0)
                    {
                        _contacts = msg.ContactPoints.Select(Context.ActorSelection).ToArray();
                        foreach (var contact in _contacts)
                        {
                            contact.Tell(new Identify(null));
                        }
                    }
                })
                .With<ActorIdentity>(msg =>
                {
                    if (msg.Subject != null)
                    {
                        Log.Info("Connected to [{0}]", msg.Subject.Path);
                        ScheduleRefreshContactsTick(Settings.RefreshContactsInterval);

                        var receptionist = msg.Subject;
                        SendBuffered(receptionist);
                        Context.Become(Active(receptionist));
                        _failureDetector.HeartBeat();
                    }
                })
                .With<InternalMessage>(msg =>
                {
                    switch (msg)
                    {
                        case InternalMessage.HeartbeatTick:
                            _failureDetector.HeartBeat();
                            break;
                        case InternalMessage.RefreshContactsTick:
                            SendGetContacts();
                            break;
                    }
                })
               .With<Send>(send => Buffer(new Send(send.Path, send.Message, send.LocalAffinity)))
               .With<SendToAll>(send => Buffer(new SendToAll(send.Path, send.Message)))
               .With<Publish>(publish => Buffer(new Publish(publish.Topic, publish.Message)))
               .WasHandled;
        }

        private Receive Active(IActorRef receptionist)
        {
            return message =>
                message.Match()
                .With<Send>(send =>
                    receptionist.Forward(new Send(send.Path, send.Message, send.LocalAffinity)))
                .With<SendToAll>(toAll =>
                    receptionist.Forward(new SendToAll(toAll.Path, toAll.Message)))
                .With<Publish>(publish =>
                    receptionist.Forward(new Publish(publish.Topic, publish.Message)))
                .With<InternalMessage>(m =>
                {
                    switch (m)
                    {
                        case InternalMessage.HeartbeatTick:
                            if (!_failureDetector.IsAvailable)
                            {
                                Log.Info("Lost contact with [{0}], restablishing connection", receptionist);
                                SendGetContacts();
                                ScheduleRefreshContactsTick(Settings.EstablishingGetContactsInterval);
                                Context.Become(Establishing);
                                _failureDetector.HeartBeat();
                            }
                            else
                            {
                                receptionist.Tell(ClusterReceptionist.Heartbeat.Instance);
                            }
                            break;
                        case InternalMessage.RefreshContactsTick:
                            receptionist.Tell(ClusterReceptionist.GetContacts.Instance);
                            break;
                    }
                })
                .With<ClusterReceptionist.HeartbeatRsp>(_ => _failureDetector.HeartBeat())
                .With<ClusterReceptionist.Contacts>(contacts =>
                {
                    if (contacts.ContactPoints.Length != 0)
                        _contacts = contacts.ContactPoints.Select(Context.ActorSelection).ToArray();
                })
                .With<Terminated>(terminated =>
                {
                    Log.Info("Lost contact with [{0}], restablishing connection", receptionist);
                    SendGetContacts();
                    ScheduleRefreshContactsTick(Settings.EstablishingGetContactsInterval);
                    Context.Become(Establishing);
                })
                .With<ActorIdentity>(_ => { /* ok, from previous establish, already handled */ })
                .WasHandled;
        }

        private void SendGetContacts()
        {
            ActorSelection[] toSend;
            if (_contacts.Length == 0)
                toSend = _initialContactsSelections;
            else if (_contacts.Length == 1)
                toSend = _initialContactsSelections.Union(_contacts).ToArray();
            else
                toSend = _contacts;

            if (Log.IsDebugEnabled)
                Log.Debug("Sending GetContacts to [{0}]", string.Join(", ", toSend.AsEnumerable()));

            foreach (var contact in toSend)
                contact.Tell(ClusterReceptionist.GetContacts.Instance);
        }

        private void Buffer(object message)
        {
            if (Settings.BufferSize == 0)
                Log.Debug("Receptionist not available and buffering is disabled, dropping message [{0}]", message.GetType());
            else if (_buffer.Count == Settings.BufferSize)
            {
                var m = _buffer.Dequeue();
                Log.Debug("Receptionist not available, buffer is full, dropping first message [{0}]", m.Item1.GetType());
                _buffer.Enqueue(Tuple.Create(message, Sender));
            }
            else
            {
                Log.Debug("Receptionist not available, buffering message type [{0}]", message.GetType());
                _buffer.Enqueue(Tuple.Create(message, Sender));
            }
        }

        private void SendBuffered(IActorRef receptionist)
        {
            Log.Debug("Sending buffered messages to receptionist");
            while (_buffer.Count != 0)
            {
                var t = _buffer.Dequeue();
                receptionist.Tell(t.Item1, t.Item2);
            }
        }
    }
}
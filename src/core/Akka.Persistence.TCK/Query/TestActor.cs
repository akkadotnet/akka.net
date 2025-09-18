//-----------------------------------------------------------------------
// <copyright file="TestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;

namespace Akka.Persistence.TCK.Query
{
    internal class TestActor : UntypedPersistentActor, IWithUnboundedStash
    {
        public static Props Props(string persistenceId) => Actor.Props.Create(() => new TestActor(persistenceId));

        public sealed class DeleteCommand
        {
            public DeleteCommand(long toSequenceNr)
            {
                ToSequenceNr = toSequenceNr;
            }

            public long ToSequenceNr { get; }
        }

        private readonly ILoggingAdapter _log;
        
        public TestActor(string persistenceId)
        {
            PersistenceId = persistenceId;
            _log = Context.GetLogger();
            _log.Info("TestActor constructor called");
        }

        public override string PersistenceId { get; }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case SnapshotOffer offer:
                    _log.Info("Recover from snapshot: {0}", offer.Snapshot);
                    break;
                case RecoveryCompleted:
                    _log.Info("Recovery completed");
                    break;
                default:
                    _log.Info("Recovering {0}", message);
                    break;
            }
        }

        protected override void PreStart()
        {
            base.PreStart();
            _log.Info("TestActor started");
        }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case DeleteCommand delete:
                    if (Sender.IsNobody())
                        throw new Exception("Sender is Nobody. Check implicit sender code.");
                    
                    DeleteMessages(delete.ToSequenceNr);
                    Become(WhileDeleting(Sender)); // need to wait for delete ACK to return
                    break;
                case string cmd:
                    if (Sender.IsNobody())
                        throw new Exception("Sender is Nobody. Check implicit sender code.");
                    
                    var sender = Sender;
                    _log.Info("Persisting message {0}, sender: {1}", cmd, sender);
                    Persist(cmd, e =>
                    {
                        sender.Tell($"{e}-done");
                        _log.Info("Message persisted {0}, sender: {1}", e, sender);
                    });
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        protected Receive WhileDeleting(IActorRef originalSender)
        {
            return message =>
            {
                switch (message)
                {
                    case DeleteMessagesSuccess success:
                        if (originalSender.IsNobody())
                            throw new Exception("Sender is Nobody. Check implicit sender code.");
                        
                        originalSender.Tell($"{success.ToSequenceNr}-deleted");
                        Become(OnCommand);
                        Stash.UnstashAll();
                        break;
                    case DeleteMessagesFailure failure:
                        if (originalSender.IsNobody())
                            throw new Exception("Sender is Nobody. Check implicit sender code.");
                    
                        Log.Error(failure.Cause, "Failed to delete messages to sequence number [{0}].", failure.ToSequenceNr);
                        originalSender.Tell($"{failure.ToSequenceNr}-deleted-failed");
                        Become(OnCommand);
                        Stash.UnstashAll();
                        break;
                    default:
                        Stash.Stash();
                        break;
                }

                return true;
            };
        }
    }

    public class ColorFruitTagger : IWriteEventAdapter
    {
        public static IImmutableSet<string> Colors { get; } = ImmutableHashSet.Create("green", "black", "blue");
        public static IImmutableSet<string> Fruits { get; } = ImmutableHashSet.Create("apple", "banana");

        public string Manifest(object evt) => string.Empty;

        public object ToJournal(object evt)
        {
            if (evt is string s)
            {
                var colorTags = Colors.Aggregate(ImmutableHashSet<string>.Empty, (acc, color) => s.Contains(color) ? acc.Add(color) : acc);
                var fruitTags = Fruits.Aggregate(ImmutableHashSet<string>.Empty, (acc, color) => s.Contains(color) ? acc.Add(color) : acc);
                var tags = colorTags.Union(fruitTags);
                return tags.IsEmpty
                    ? evt
                    : new Tagged(evt, tags);
            }

            return evt;
        }
    }
}

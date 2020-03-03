//-----------------------------------------------------------------------
// <copyright file="TestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
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

        public TestActor(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public override string PersistenceId { get; }

        protected override void OnRecover(object message)
        {
        }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case DeleteCommand delete:
                    DeleteMessages(delete.ToSequenceNr);
                    Become(WhileDeleting(Sender)); // need to wait for delete ACK to return
                    break;
                case string cmd:
                    var sender = Sender;
                    Persist(cmd, e => sender.Tell($"{e}-done"));
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
                        originalSender.Tell($"{success.ToSequenceNr}-deleted");
                        Become(OnCommand);
                        Stash.UnstashAll();
                        break;
                    case DeleteMessagesFailure failure:
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

//-----------------------------------------------------------------------
// <copyright file="SqlJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Persistence.Sql.Common.Queries;

namespace Akka.Persistence.Sql.Common.Journal
{
    public abstract class SqlJournal : AsyncWriteJournal
    {
        protected readonly JournalDbEngine DbEngine;

        protected SqlJournal(JournalDbEngine dbEngine)
        {
            if (dbEngine == null)
                throw new ArgumentNullException("dbEngine", "Database engine provided to sql journal cannot be null");

            DbEngine = dbEngine;
        }

        protected override void PostStop()
        {
            base.PostStop();
            DbEngine.Close();
        }

        protected override bool ReceivePluginInternal(object message)
        {
            if (message is Query)
            {
                HandleEventQuery(message as Query);

            }
            else if (message is ReplayTaggedMessages)
            {
                var replay = (ReplayTaggedMessages)message;
                var readHighestSequenceNrFrom = Math.Max(0, replay.FromSequenceNr - 1);
                ReadHighestSequenceNrAsync(DbEngine.TagAsPersistenceId(replay.Tag), readHighestSequenceNrFrom)
                    .ContinueWith(async highestSequenceNrTask =>
                    {
                        var highestSequenceNr = highestSequenceNrTask.Result;
                        var toSeqNr = Math.Min(replay.ToSequenceNr, highestSequenceNr);
                        if (highestSequenceNr == 0 || replay.FromSequenceNr > toSeqNr)
                        {
                            return highestSequenceNr;
                        }
                        else
                        {
                            await DbEngine.ReplayTaggedMessagesAsync(replay.Tag, replay.FromSequenceNr, toSeqNr, replay.Max, tagged =>
                            {
                                foreach (var adapted in AdaptFromJournal(tagged.Persistent))
                                    replay.ReplyTo.Tell(new ReplayedTaggedMessage(adapted, tagged.Tag, tagged.Offset), ActorRefs.NoSender);
                            });

                            return highestSequenceNr;
                        }
                    })
                    .Unwrap()
                    .PipeTo(replay.ReplyTo, success: h => new RecoverySuccess(h), failure: e => new ReplayMessagesFailure(e));
            }
            else if (message is SubscribePersistenceId)
            {
                DbEngine.AddPersistenceIdSubscriber(Sender, ((SubscribePersistenceId)message).PersistenceId);
                Context.Watch(Sender);
            }
            else if (message is SubscribeAllPersistenceIds)
            {
                DbEngine.AddAllPersistenceIdSubscriber(Sender);
                Context.Watch(Sender);
            }
            else if (message is SubscribeTag)
            {
                DbEngine.AddTagSubscriber(Sender, ((SubscribeTag)message).Tag);
                Context.Watch(Sender);
            }
            else if (message is Terminated)
            {
                DbEngine.RemoveSubscriber(((Terminated)message).ActorRef);
            }
            else return false;
            return true;
        }

        private void HandleEventQuery(Query query)
        {
            var queryId = query.QueryId;
            var sender = Context.Sender;
            DbEngine.ReadEvents(queryId, query.Hints, Context.Sender, reply =>
            {
                foreach (var adapted in AdaptFromJournal(reply))
                {
                    sender.Tell(new QueryResponse(queryId, adapted));
                }
            })
            .ContinueWith(task =>
                task.IsFaulted || task.IsCanceled ? (IQueryReply)new QueryFailure(queryId, task.Exception) : new QuerySuccess(queryId),
                TaskContinuationOptions.ExecuteSynchronously)
            .PipeTo(Context.Sender);
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            return DbEngine.ReplayMessagesAsync(persistenceId, fromSequenceNr, toSequenceNr, max, context.Sender, recoveryCallback);
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return DbEngine.ReadHighestSequenceNrAsync(persistenceId, fromSequenceNr);
        }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            return DbEngine.WriteMessagesAsync(messages);
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            return DbEngine.DeleteMessagesToAsync(persistenceId, toSequenceNr);
        }
    }
}
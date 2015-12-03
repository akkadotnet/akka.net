//-----------------------------------------------------------------------
// <copyright file="SqlJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        protected override bool Receive(object message)
        {
            var wasHandled = base.Receive(message);
            if (!wasHandled && message is Query)
            {
                HandleEventQuery(message as Query);
                return true;
            }

            return false;
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
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent)
            .PipeTo(Context.Sender);
        }

        public override Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback)
        {
            return DbEngine.ReplayMessagesAsync(persistenceId, fromSequenceNr, toSequenceNr, max, Context.Sender, replayCallback);
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return DbEngine.ReadHighestSequenceNrAsync(persistenceId, fromSequenceNr);
        }

        protected override Task WriteMessagesAsync(IEnumerable<IPersistentRepresentation> messages)
        {
            return DbEngine.WriteMessagesAsync(messages);
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            return DbEngine.DeleteMessagesToAsync(persistenceId, toSequenceNr, isPermanent);
        }
    }
}
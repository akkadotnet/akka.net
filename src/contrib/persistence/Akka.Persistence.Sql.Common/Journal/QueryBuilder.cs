//-----------------------------------------------------------------------
// <copyright file="QueryBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Data.Common;
using Akka.Persistence.Sql.Common.Queries;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// SQL query builder used for generating queries required to perform journal's tasks.
    /// </summary>
    public interface IJournalQueryBuilder
    {
        /// <summary>
        /// Returns query which should return events filtered accordingly to provided set of <paramref name="hints"/>.
        /// </summary>
        DbCommand SelectEvents(IEnumerable<IHint> hints);

        /// <summary>
        /// Returns query which should return a frame of messages filtered accordingly to provided parameters.
        /// </summary>
        DbCommand SelectMessages(string persistenceId, long fromSequenceNr, long toSequenceNr, long max);

        /// <summary>
        /// Returns query returning single number considered as the highest sequence number in current journal.
        /// </summary>
        DbCommand SelectHighestSequenceNr(string persistenceId);

        /// <summary>
        /// Returns a non-query command used to insert collection of <paramref name="messages"/> in journal table.
        /// </summary>
        DbCommand InsertBatchMessages(IPersistentRepresentation[] messages);

        /// <summary>
        /// Returns DELETE statement used to delete rows permanently.
        /// </summary>
        DbCommand DeleteBatchMessages(string persistenceId, long toSequenceNr);
    }
}
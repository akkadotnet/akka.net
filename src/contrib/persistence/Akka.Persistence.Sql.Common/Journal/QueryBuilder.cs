//-----------------------------------------------------------------------
// <copyright file="QueryBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Data.Common;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// SQL query builder used for generating queries required to perform journal's tasks.
    /// </summary>
    public interface IJournalQueryBuilder
    {
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
        /// Depending on <paramref name="permanent"/> flag this method may return either UPDATE or DELETE statement
        /// used to alter IsDeleted field or delete rows permanently.
        /// </summary>
        DbCommand DeleteBatchMessages(string persistenceId, long toSequenceNr, bool permanent);
    }
}
//-----------------------------------------------------------------------
// <copyright file="QueryMapper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.Common;
using Akka.Actor;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// Mapper used for generating persistent representations based on SQL query results.
    /// </summary>
    public interface IJournalQueryMapper
    {
        /// <summary>
        /// Takes a current row from the SQL data reader and produces a persistent representation object in result.
        /// It's not supposed to move reader's cursor in any way.
        /// </summary>
        IPersistentRepresentation Map(DbDataReader reader, IActorRef sender = null);
    }

    /// <summary>
    /// Default implementation of <see cref="IJournalQueryMapper"/> used for mapping data 
    /// returned from ADO.NET data readers back to <see cref="IPersistentRepresentation"/> messages.
    /// </summary>
    internal class DefaultJournalQueryMapper : IJournalQueryMapper
    {
        public const int PersistenceIdIndex = 0;
        public const int SequenceNrIndex = 1;
        public const int IsDeletedIndex = 2;
        public const int ManifestIndex = 3;
        public const int PayloadIndex = 4;

        private readonly Akka.Serialization.Serialization _serialization;

        public DefaultJournalQueryMapper(Akka.Serialization.Serialization serialization)
        {
            _serialization = serialization;
        }

        public IPersistentRepresentation Map(DbDataReader reader, IActorRef sender = null)
        {
            var persistenceId = reader.GetString(PersistenceIdIndex);
            var sequenceNr = reader.GetInt64(SequenceNrIndex);
            var isDeleted = reader.GetBoolean(IsDeletedIndex);
            var manifest = reader.GetString(ManifestIndex);

            // timestamp is SQL-journal specific field, it's not a part of casual Persistent instance  
            var payload = GetPayload(reader, manifest);

            return new Persistent(payload, sequenceNr, manifest, persistenceId, isDeleted, sender);
        }

        private object GetPayload(DbDataReader reader, string manifest)
        {
            var type = Type.GetType(manifest, true);
            var binary = (byte[]) reader[PayloadIndex];

            var serializer = _serialization.FindSerializerForType(type);
            return serializer.FromBinary(binary, type);
        }
    }
}
//-----------------------------------------------------------------------
// <copyright file="QueryMapper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        private readonly Akka.Serialization.Serialization _serialization;

        public DefaultJournalQueryMapper(Akka.Serialization.Serialization serialization)
        {
            _serialization = serialization;
        }

        public IPersistentRepresentation Map(DbDataReader reader, IActorRef sender = null)
        {
            var persistenceId = reader.GetString(0);
            var sequenceNr = reader.GetInt64(1);
            var isDeleted = reader.GetBoolean(2);
            var payload = GetPayload(reader);

            return new Persistent(payload, sequenceNr, persistenceId, isDeleted, sender);
        }

        private object GetPayload(DbDataReader reader)
        {
            var payloadType = reader.GetString(3);
            var type = Type.GetType(payloadType, true);
            var binary = (byte[]) reader[4];

            var serializer = _serialization.FindSerializerForType(type);
            return serializer.FromBinary(binary, type);
        }
    }
}
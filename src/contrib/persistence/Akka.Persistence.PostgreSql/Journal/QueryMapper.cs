using System;
using System.Data.Common;
using System.Data.SqlClient;
using Npgsql;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Actor;

namespace Akka.Persistence.PostgreSql.Journal
{
    internal class PostgreSqlJournalQueryMapper : IJournalQueryMapper
    {
        private readonly Akka.Serialization.Serialization _serialization;

        public PostgreSqlJournalQueryMapper(Akka.Serialization.Serialization serialization)
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
//-----------------------------------------------------------------------
// <copyright file="SqliteQueryMapper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.Common;
using Akka.Persistence.Sql.Common.Snapshot;

namespace Akka.Persistence.Sqlite.Snapshot
{
    internal class SqliteQueryMapper : ISnapshotQueryMapper
    {
        private readonly Akka.Serialization.Serialization _serialization;

        public SqliteQueryMapper(Akka.Serialization.Serialization serialization)
        {
            _serialization = serialization;
        }

        public SelectedSnapshot Map(DbDataReader reader)
        {
            var persistenceId = reader.GetString(0);
            var sequenceNr = reader.GetInt64(1);
            var timestamp = new DateTime(reader.GetInt64(2));

            var metadata = new SnapshotMetadata(persistenceId, sequenceNr, timestamp);
            var snapshot = GetSnapshot(reader);

            return new SelectedSnapshot(metadata, snapshot);
        }

        private object GetSnapshot(DbDataReader reader)
        {
            var type = Type.GetType(reader.GetString(3), true);
            var serializer = _serialization.FindSerializerForType(type);
            var binary = (byte[])reader[4];

            var obj = serializer.FromBinary(binary, type);

            return obj;
        }
    }
}
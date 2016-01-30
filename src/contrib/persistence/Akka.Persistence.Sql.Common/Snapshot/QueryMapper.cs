//-----------------------------------------------------------------------
// <copyright file="QueryMapper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.Common;

namespace Akka.Persistence.Sql.Common.Snapshot
{
    /// <summary>
    /// Mapper used to map results of snapshot SELECT queries into valid snapshot objects.
    /// </summary>
    public interface ISnapshotQueryMapper
    {
        /// <summary>
        /// Map data found under current cursor pointed by SQL data reader into <see cref="SelectedSnapshot"/> instance.
        /// </summary>
        SelectedSnapshot Map(DbDataReader reader);
    }

    internal class DefaultSnapshotQueryMapper : ISnapshotQueryMapper
    {
        private readonly Akka.Serialization.Serialization _serialization;

        public DefaultSnapshotQueryMapper(Akka.Serialization.Serialization serialization)
        {
            _serialization = serialization;
        }

        public SelectedSnapshot Map(DbDataReader reader)
        {
            var persistenceId = reader.GetString(0);
            var sequenceNr = reader.GetInt64(1);
            var timestamp = reader.GetDateTime(2);

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
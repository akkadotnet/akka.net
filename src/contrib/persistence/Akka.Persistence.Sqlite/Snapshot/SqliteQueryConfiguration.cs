// //-----------------------------------------------------------------------
// // <copyright file="SqliteQueryConfiguration.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Persistence.Sql.Common.Snapshot;

namespace Akka.Persistence.Sqlite.Snapshot
{
    internal class SqliteQueryConfiguration : QueryConfiguration
    {
        private bool _useDefaultName;

        /// <summary>
        /// if snapshot table was created with default name (different than snapshotTableName) then defaultSnapshotTableName will be used.
        /// </summary>
        public string DefaultSnapshotTableName { get; }

        public SqliteQueryConfiguration(
            string schemaName, 
            string snapshotTableName, 
            string persistenceIdColumnName,
            string sequenceNrColumnName, 
            string payloadColumnName, 
            string manifestColumnName,
            string timestampColumnName,
            string serializerIdColumnName, 
            TimeSpan timeout,
            string defaultSerializer,
            bool useSequentialAccess,
            string defaultSnapshotTableName) 
            : base(
                schemaName, 
                snapshotTableName,
                persistenceIdColumnName,
                sequenceNrColumnName,
                payloadColumnName,
                manifestColumnName,
                timestampColumnName,
                serializerIdColumnName,
                timeout,
                defaultSerializer,
                useSequentialAccess)
        {
            DefaultSnapshotTableName = defaultSnapshotTableName;
        }

        public override string FullSnapshotTableName => _useDefaultName ? DefaultSnapshotTableName : base.FullSnapshotTableName;

        public void UseDefaultSnapshotTableName(bool useDefaultName = true)
        {
            _useDefaultName = useDefaultName;
        }
    }
}
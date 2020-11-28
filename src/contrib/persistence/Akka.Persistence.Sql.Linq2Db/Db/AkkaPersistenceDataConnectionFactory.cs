using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Snapshot;
using Akka.Util;
using LinqToDB;
using LinqToDB.Configuration;
using LinqToDB.Data;
using LinqToDB.Data.RetryPolicy;
using LinqToDB.DataProvider.SqlServer;
using LinqToDB.Mapping;

namespace Akka.Persistence.Sql.Linq2Db.Db
{
    public class AkkaPersistenceDataConnectionFactory
    {
        private string providerName;
        private string connString;
        private MappingSchema mappingSchema;
        private LinqToDbConnectionOptions opts;
        
        
        public AkkaPersistenceDataConnectionFactory(IProviderConfig<JournalTableConfig> config)
        {
            providerName = config.ProviderName;
            connString = config.ConnectionString;
            //Build Mapping Schema to be used for all connections.
            //Make a unique mapping schema name here to avoid problems
            //with multiple configurations using different schemas.
            var configName = "akka.persistence.l2db." + HashCode.Combine(config.ConnectionString, config.ProviderName, config.TableConfig.GetHashCode());
            var fmb = new MappingSchema(configName,MappingSchema.Default)
                .GetFluentMappingBuilder();
            MapJournalRow(config, fmb);

            useCloneDataConnection = config.UseCloneConnection;
            mappingSchema = fmb.MappingSchema;
            opts = new LinqToDbConnectionOptionsBuilder()
                .UseConnectionString(providerName, connString)
                .UseMappingSchema(mappingSchema).Build();
            
            if (providerName.ToLower().StartsWith("sqlserver"))
            {
                policy = new SqlServerRetryPolicy();
            }
            _cloneConnection = new Lazy<DataConnection>(()=>new DataConnection(opts));
        }
        
        public AkkaPersistenceDataConnectionFactory(IProviderConfig<SnapshotTableConfiguration> config)
        {
            providerName = config.ProviderName;
            connString = config.ConnectionString;
            //Build Mapping Schema to be used for all connections.
            //Make a unique mapping schema name here to avoid problems
            //with multiple configurations using different schemas.
            var configName = "akka.persistence.l2db." + HashCode.Combine(config.ConnectionString, config.ProviderName, config.TableConfig.GetHashCode());
            var ms = new MappingSchema(configName, MappingSchema.Default);
            //ms.SetConvertExpression<DateTime, DateTime>(dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            var fmb = ms
                .GetFluentMappingBuilder();
            MapSnapshotRow(config, fmb);

            useCloneDataConnection = config.UseCloneConnection;
            mappingSchema = fmb.MappingSchema;
            opts = new LinqToDbConnectionOptionsBuilder()
                .UseConnectionString(providerName, connString)
                .UseMappingSchema(mappingSchema).Build();
            
            if (providerName.ToLower().StartsWith("sqlserver"))
            {
                policy = new SqlServerRetryPolicy();
            }
            _cloneConnection = new Lazy<DataConnection>(()=>new DataConnection(opts));
        }

        private static void MapSnapshotRow(
            IProviderConfig<SnapshotTableConfiguration> config,
            FluentMappingBuilder fmb)
        {
            var tableConfig = config.TableConfig;
            var builder = fmb.Entity<SnapshotRow>()
                .HasSchemaName(tableConfig.SchemaName)
                .HasTableName(tableConfig.TableName)
                .Member(r => r.Created)
                .HasColumnName(tableConfig.ColumnNames.Created)
                .Member(r => r.Manifest)
                .HasColumnName(tableConfig.ColumnNames.Manifest)
                .HasLength(500)
                .Member(r => r.Payload)
                .HasColumnName(tableConfig.ColumnNames.Snapshot)
                .Member(r => r.SequenceNumber)
                .HasColumnName(tableConfig.ColumnNames.SequenceNumber)
                .Member(r => r.SerializerId)
                .HasColumnName(tableConfig.ColumnNames.SerializerId)
                .Member(r => r.PersistenceId)
                .HasColumnName(tableConfig.ColumnNames.PersistenceId)
                .HasLength(255);
            if (config.ProviderName.ToLower().Contains("sqlite")||config.ProviderName.ToLower().Contains("postgres"))
            {
                builder.Member(r => r.Created)
                    .HasDataType(DataType.Int64)
                    .HasConversion(r => r.Ticks, r => new DateTime(r));
            }
            if (config.IDaoConfig.SqlCommonCompatibilityMode)
            {
                
                //builder.Member(r => r.Created)
                //    .HasConversion(l => DateTimeHelpers.FromUnixEpochMillis(l),
                //        dt => DateTimeHelpers.ToUnixEpochMillis(dt));
            }
        }

        private static void MapJournalRow(IProviderConfig<JournalTableConfig> config,
            FluentMappingBuilder fmb)
        {
            var tableConfig = config.TableConfig;
            var journalRowBuilder = fmb.Entity<JournalRow>()
                .HasSchemaName(tableConfig.SchemaName)
                .HasTableName(tableConfig.TableName)
                .Member(r => r.deleted).HasColumnName(tableConfig.ColumnNames.Deleted)
                .Member(r => r.manifest).HasColumnName(tableConfig.ColumnNames.Manifest)
                .HasLength(500)
                .Member(r => r.message).HasColumnName(tableConfig.ColumnNames.Message)
                .Member(r => r.ordering).HasColumnName(tableConfig.ColumnNames.Ordering)
                .Member(r => r.tags).HasLength(100)
                .HasColumnName(tableConfig.ColumnNames.Tags)
                .Member(r => r.Identifier)
                .HasColumnName(tableConfig.ColumnNames.Identitifer)
                .Member(r => r.persistenceId)
                .HasColumnName(tableConfig.ColumnNames.PersistenceId).HasLength(255).IsNullable(false)
                .Member(r => r.sequenceNumber)
                .HasColumnName(tableConfig.ColumnNames.SequenceNumber)
                .Member(r => r.Timestamp)
                .HasColumnName(tableConfig.ColumnNames.Created);

            if (config.ProviderName.ToLower().Contains("sqlite"))
            {
                journalRowBuilder.Member(r => r.ordering).IsPrimaryKey().HasDbType("INTEGER")
                    .IsIdentity();
            }
            else
            {
                journalRowBuilder.Member(r => r.ordering).IsIdentity()
                    .Member(r=>r.persistenceId).IsPrimaryKey()
                    .Member(r=>r.sequenceNumber).IsPrimaryKey();
            }

            //Probably overkill, but we only set Metadata Mapping if specified
            //That we are in delete compatibility mode.
            if (config.IDaoConfig.SqlCommonCompatibilityMode)
            {
                fmb.Entity<JournalMetaData>()
                    .HasTableName(tableConfig.MetadataTableName)
                    .HasSchemaName(tableConfig.SchemaName)
                    .Member(r => r.PersistenceId)
                    .HasColumnName(tableConfig.MetadataColumnNames.PersistenceId)
                    .HasLength(255)
                    .Member(r => r.SequenceNumber)
                    .HasColumnName(tableConfig.MetadataColumnNames.SequenceNumber)
                    ;
            }
        }

        private Lazy<DataConnection> _cloneConnection;
        private bool useCloneDataConnection;
        private IRetryPolicy policy;

        public DataConnection GetConnection()
        {
            if (useCloneDataConnection)
            {
                var conn =  (DataConnection)_cloneConnection.Value.Clone();
                conn.RetryPolicy = policy;
                return conn;
            }
            else
            {
                return new DataConnection(opts) { RetryPolicy = policy};    
            }
        }
    }
}
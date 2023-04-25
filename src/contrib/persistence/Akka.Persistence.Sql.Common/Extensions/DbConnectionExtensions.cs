// //-----------------------------------------------------------------------
// // <copyright file="DbConnectionExtensions.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Persistence.Sql.Common.Extensions
{
    public static class DbConnectionExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async Task ExecuteInTransaction(
            this DbConnection connection,
            IsolationLevel isolationLevel,
            CancellationToken token,
            Func<DbTransaction, CancellationToken, Task> task)
        {
            using var tx = connection.BeginTransaction(isolationLevel);
            try
            {
                await task(tx, token);
                tx.Commit();
            }
            catch (Exception ex1)
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception ex2)
                {
                    throw new AggregateException(ex2, ex1);
                }
                throw;
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async Task<T> ExecuteInTransaction<T>(
            this DbConnection connection,
            IsolationLevel isolationLevel,
            CancellationToken token,
            Func<DbTransaction, CancellationToken, Task<T>> task)
        {
            using var tx = connection.BeginTransaction(isolationLevel);
            try
            {
                var result = await task(tx, token);
                tx.Commit();
                return result;
            }
            catch (Exception ex1)
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception ex2)
                {
                    throw new AggregateException(ex2, ex1);
                }
                throw;
            }
        }
    }
}
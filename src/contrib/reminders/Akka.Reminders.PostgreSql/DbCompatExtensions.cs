//-----------------------------------------------------------------------
// <copyright file="DbCompatExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Reminders.PostgreSql;

/// <summary>
/// Shims for ADO.NET APIs that exist on modern .NET but not on netstandard2.0.
/// </summary>
internal static class DbCompatExtensions
{
#if !NET6_0_OR_GREATER
    /// <summary>
    /// Begins a database transaction.
    /// </summary>
    /// <remarks>
    /// <c>DbConnection.BeginTransactionAsync</c> is .NET 6+. The synchronous
    /// overload is the only option on netstandard2.0; it is wrapped in a
    /// completed task so call sites stay identical across both targets.
    /// </remarks>
    public static Task<DbTransaction> BeginTransactionAsync(
        this DbConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(connection.BeginTransaction());
    }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <remarks><c>DbTransaction.CommitAsync</c> is .NET 6+.</remarks>
    public static Task CommitAsync(this DbTransaction transaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Rolls the transaction back.
    /// </summary>
    /// <remarks><c>DbTransaction.RollbackAsync</c> is .NET 6+.</remarks>
    public static Task RollbackAsync(this DbTransaction transaction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Rollback();
        return Task.CompletedTask;
    }
#endif
}

//-----------------------------------------------------------------------
// <copyright file="ISnapshotStoreInterceptor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System.Threading.Tasks;

    /// <summary>
    ///     Interface to object which will intercept all action in <see cref="TestSnapshotStore"/>.
    /// </summary>
    public interface ISnapshotStoreInterceptor
    {
        /// <summary>
        ///     Method will be called for each load, save or delete attempt in <see cref="TestSnapshotStore"/>.
        /// </summary>
        Task InterceptAsync(string persistenceId, SnapshotSelectionCriteria criteria);
    }
}

//-----------------------------------------------------------------------
// <copyright file="ITimestampProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// Interface responsible for generation of timestamps for persisted messages in SQL-based journals.
    /// </summary>
    public interface ITimestampProvider
    {
        /// <summary>
        /// Generates timestamp for provided <see cref="IPersistentRepresentation"/> message.
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        long GenerateTimestamp(IPersistentRepresentation message);
    }
    
    /// <summary>
    /// Default implementation of timestamp provider. Returns <see cref="DateTime.UtcNow"/> for any message.
    /// </summary>
    public sealed class DefaultTimestampProvider : ITimestampProvider
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public long GenerateTimestamp(IPersistentRepresentation message) => DateTime.UtcNow.Ticks;
    }
}

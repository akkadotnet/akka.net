﻿//-----------------------------------------------------------------------
// <copyright file="ITimestampProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        long GenerateTimestamp(IPersistentRepresentation message);
    }
    
    /// <summary>
    /// Default implementation of timestamp provider. Returns <see cref="DateTime.UtcNow"/> for any message.
    /// </summary>
    public sealed class DefaultTimestampProvider : ITimestampProvider
    {
        public long GenerateTimestamp(IPersistentRepresentation message) => DateTime.UtcNow.Ticks;
    }
}
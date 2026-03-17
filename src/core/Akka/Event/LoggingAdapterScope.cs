// <copyright file="LoggingAdapterScope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// Represents an explicit logging scope for <see cref="ILoggingAdapter"/> instances.
    /// </summary>
    public interface ILoggingAdapterScope : IDisposable
    {
        /// <summary>
        /// Gets the scoped logging adapter.
        /// </summary>
        ILoggingAdapter Log { get; }
    }

    internal sealed class LoggingAdapterScope : ILoggingAdapterScope
    {
        public LoggingAdapterScope(ILoggingAdapter log)
        {
            Log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public ILoggingAdapter Log { get; }

        public void Dispose()
        {
        }
    }
}

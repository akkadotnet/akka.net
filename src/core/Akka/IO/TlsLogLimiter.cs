// -----------------------------------------------------------------------
// <copyright file="TlsLogLimiter.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.IO
{
    /// <summary>
    /// INTERNAL API. Lets a log line through at most once per period, by key. It remembers at most 100 keys, and forgets
    /// a key only once its period has passed. When it is full, a new key is not logged; the caller logs one line instead
    /// that says lines were dropped, at most once per period. Its lock is held only to read the clock and to look up
    /// and note a key.
    /// </summary>
    internal sealed class TlsLogLimiter
    {
        internal const int Capacity = 100;

        private readonly TimeSpan _period;
        private readonly Func<DateTime> _clock;
        private readonly Dictionary<string, DateTime> _loggedAt = new Dictionary<string, DateTime>();
        private DateTime? _summaryAt;

        /// <param name="period">How long a key stays quiet after it was logged.</param>
        /// <param name="clock">The UTC clock; <see cref="DateTime.UtcNow"/> when <c>null</c>.</param>
        public TlsLogLimiter(TimeSpan period, Func<DateTime>? clock = null)
        {
            _period = period;
            _clock = clock ?? (static () => DateTime.UtcNow);
        }

        public enum Verdict
        {
            /// <summary>Do not log the line.</summary>
            Drop,

            /// <summary>Log the line.</summary>
            Log,

            /// <summary>Do not log the line; log the line that says lines were dropped.</summary>
            LogSummary
        }

        // Concurrent callers may reach the limiter in a different order than they started. The clock is read only
        // after admission is serialized, so a caller that was delayed never looks like the clock went backwards.
        public Verdict Admit(string key)
        {
            lock (_loggedAt)
            {
                return AdmitUnderLock(key, _clock());
            }
        }

        private Verdict AdmitUnderLock(string key, DateTime now)
        {
            if (_loggedAt.TryGetValue(key, out var loggedAt))
            {
                if (IsRecent(loggedAt, now))
                    return Verdict.Drop;
            }
            else if (_loggedAt.Count >= Capacity)
            {
                foreach (var due in _loggedAt.Where(e => !IsRecent(e.Value, now)).Select(e => e.Key).ToList())
                    _loggedAt.Remove(due);

                if (_loggedAt.Count >= Capacity)
                {
                    if (_summaryAt is { } summaryAt && IsRecent(summaryAt, now))
                        return Verdict.Drop;
                    _summaryAt = now;
                    return Verdict.LogSummary;
                }
            }

            _loggedAt[key] = now;
            return Verdict.Log;
        }

        // A clock that went back counts as due.
        private bool IsRecent(DateTime at, DateTime now) => now >= at && now - at < _period;
    }
}

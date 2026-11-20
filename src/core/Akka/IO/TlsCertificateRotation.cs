// -----------------------------------------------------------------------
// <copyright file="TlsCertificateRotation.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Event;

namespace Akka.IO
{
    /// <summary>
    /// INTERNAL API. What a remoting transport (DotNetty or Artery) does around its <see cref="TlsCertificateSource"/>:
    /// the startup check, the once-a-minute poll, and the error it logs when a connection has no certificate to
    /// present. Each transport owns one instance, so one transport's limiter never hides another transport's errors.
    /// </summary>
    internal sealed class TlsCertificateRotation
    {
        /// <summary>
        /// A certificate source is asked when a connection is built, and by the transport's timer at this interval, so
        /// a node that builds no connection still looks for a renewed certificate and logs the change. Fixed, with no
        /// config key.
        /// </summary>
        internal static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

        // When the certificate source has nothing usable, every connection fails the same way. Say so at most once a
        // minute for each direction and cause, so one cause never hides another.
        private readonly TlsLogLimiter _connectionErrors = new TlsLogLimiter(TimeSpan.FromMinutes(1));

        /// <summary>
        /// The startup check: waits for the source's first look-up, until 30 seconds after it started, without
        /// blocking a thread. Whatever the source throws (it may be a callback that throws anything) stops the node.
        /// </summary>
        internal static async Task CheckAtStartupAsync(TlsCertificateSource source, TlsCertificateRole role,
            ILoggingAdapter log)
        {
            try
            {
                await source.GetCertificateAsync(role, log).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is ConfigurationException) && !TlsCertificateSource.IsFatal(ex))
            {
                throw new ConfigurationException(ex.Message, ex);
            }
        }

        /// <summary>
        /// What the transport's timer runs: <paramref name="poll"/> asks the source. The source logs what it expects
        /// itself, so anything it throws is unexpected, and is logged here.
        /// </summary>
        internal static void Poll(Action poll, ILoggingAdapter log)
        {
            try
            {
                poll();
            }
            catch (Exception ex) when (!TlsCertificateSource.IsFatal(ex))
            {
                log.Warning(ex, "The TLS certificate check that runs once a minute failed. The node keeps its current certificate and checks again in a minute.");
            }
        }

        /// <summary>Logs, at the limiter's pace, that this node cannot build a connection to <paramref name="remotePeer"/>.</summary>
        internal void ReportOutboundFailure(Exception exception, object remotePeer, ILoggingAdapter log)
        {
            if (ShouldLog("outbound", exception, log))
                log.Error(exception, "Cannot build a TLS connection to [{0}]: {1}", remotePeer, exception.Message);
        }

        /// <summary>Logs, at the limiter's pace, that this node cannot build a connection from <paramref name="remotePeer"/>.</summary>
        internal void ReportInboundFailure(Exception exception, object remotePeer, ILoggingAdapter log)
        {
            if (ShouldLog("inbound", exception, log))
                log.Error(exception, "Cannot build a TLS connection for the incoming connection from [{0}]: {1}",
                    remotePeer, exception.Message);
        }

        // True when this direction and cause was not logged in the last minute. Past 100 causes in a minute, a new
        // one is not logged; one line says so instead, at most once a minute. The cause is the message when the
        // certificate source wrote it (or the kind of failure, where the message holds something that differs
        // between calls), and otherwise the exception type: a callback's message may differ on every call, and would
        // fill the limiter.
        private bool ShouldLog(string direction, Exception exception, ILoggingAdapter log)
        {
            var cause = TlsCertificateSource.OwnFailureKey(exception) is { } key
                ? "message|" + key
                : "type|" + exception.GetType().FullName;
            var verdict = _connectionErrors.Admit(direction + "|" + cause);
            if (verdict == TlsLogLimiter.Verdict.LogSummary)
                log.Error("Some \"Cannot build a TLS connection\" errors were not logged: more than 100 different causes came within a minute.");
            return verdict == TlsLogLimiter.Verdict.Log;
        }
    }
}

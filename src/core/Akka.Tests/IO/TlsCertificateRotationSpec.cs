// -----------------------------------------------------------------------
// <copyright file="TlsCertificateRotationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using Akka.IO;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>
    /// Pins the exact lines that both remoting transports (DotNetty and Artery) log through
    /// <see cref="TlsCertificateRotation"/>: an operator's alerts may match on them.
    /// </summary>
    public sealed class TlsCertificateRotationSpec
    {
        [Fact(DisplayName = "Should_Log_The_Exact_Outbound_And_Inbound_Error_Once_A_Minute_For_Each_Direction_And_Cause")]
        public void Should_log_the_exact_outbound_and_inbound_error_once_a_minute_for_each_direction_and_cause()
        {
            var rotation = new TlsCertificateRotation();
            var log = new RecordingLog();

            for (var i = 0; i < 3; i++)
            {
                // A callback's message may differ on every call: the cause is its type, so this is one cause.
                rotation.ReportOutboundFailure(new InvalidOperationException("request " + i + " failed"), "peer:1", log);
                rotation.ReportInboundFailure(new InvalidOperationException("request " + i + " failed"), "peer:2", log);
            }

            rotation.ReportInboundFailure(new ArgumentException("another cause"), "peer:3", log);

            log.Errors.Should().Equal(
                "Cannot build a TLS connection to [peer:1]: request 0 failed",
                "Cannot build a TLS connection for the incoming connection from [peer:2]: request 0 failed",
                "Cannot build a TLS connection for the incoming connection from [peer:3]: another cause");
        }

        [Fact(DisplayName = "Should_Log_The_Exact_Summary_Line_When_More_Than_100_Causes_Come_Within_A_Minute")]
        public void Should_log_the_exact_summary_line_when_more_than_100_causes_come_within_a_minute()
        {
            var rotation = new TlsCertificateRotation();
            var log = new RecordingLog();

            // 110 exception types, so 110 causes.
            var element = typeof(object);
            for (var i = 0; i < 110; i++)
            {
                element = element.MakeArrayType();
                var failure = (Exception)Activator.CreateInstance(typeof(NumberedFailure<>).MakeGenericType(element))!;
                rotation.ReportOutboundFailure(failure, "peer", log);
            }

            log.Errors.Count.Should().Be(101, "100 causes, and one line that says the others were not logged");
            log.Errors.Last().Should().Be(
                "Some \"Cannot build a TLS connection\" errors were not logged: more than 100 different causes came within a minute.");
        }

        [Fact(DisplayName = "Should_Log_The_Exact_Warning_And_Not_Throw_When_The_Poll_Fails")]
        public void Should_log_the_exact_warning_and_not_throw_when_the_poll_fails()
        {
            var log = new RecordingLog();

            TlsCertificateRotation.Poll(() => throw new InvalidOperationException("unexpected"), log);

            log.Warnings.Should().Equal(
                "The TLS certificate check that runs once a minute failed. The node keeps its current certificate and checks again in a minute.");
        }

        [Fact(DisplayName = "Should_Poll_Once_A_Minute")]
        public void Should_poll_once_a_minute() => TlsCertificateRotation.PollInterval.Should().Be(TimeSpan.FromMinutes(1));

        private sealed class NumberedFailure<T> : Exception
        {
            public NumberedFailure() : base("failure " + typeof(T).Name)
            {
            }
        }
    }
}

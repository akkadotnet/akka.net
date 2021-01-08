//-----------------------------------------------------------------------
// <copyright file="MailboxMemoryFootprintSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using NBench;

namespace Akka.Tests.Performance.Dispatch
{
    public class MailboxMemoryFootprintSpec
    {
        private Counter _createMailboxThroughput;

        private const string CreateThroughputCounter = "MailboxCreateThroughput";
        private const int MailboxCreateNumber = 10000;
        private ActorSystem _actorSystem = ActorSystem.Create("Testing");
        private MailboxType _unboundedMailboxType;
        private MailboxType _unboundedDequeBasedMailboxType;
        private MailboxType _boundedMailboxType;
        private MailboxType _boundedDequeBasedMailboxType;

        private List<Mailbox> _mailboxes;

        private static readonly Config MailboxConfig = ConfigurationFactory.ParseString(@"mailbox-capacity = 1000
        mailbox-push-timeout-time = 10s
        stash-capacity = -1
        ");
        
        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _createMailboxThroughput = context.GetCounter(CreateThroughputCounter);
            _mailboxes = new List<Mailbox>(MailboxCreateNumber);
            _unboundedMailboxType = new UnboundedMailbox();
            _boundedMailboxType = new BoundedMailbox(_actorSystem.Settings, MailboxConfig);
            _unboundedDequeBasedMailboxType = new UnboundedDequeBasedMailbox(_actorSystem.Settings, MailboxConfig);
            _boundedDequeBasedMailboxType = new BoundedDequeBasedMailbox(_actorSystem.Settings, MailboxConfig);
        }

        private Mailbox CreateMailbox(MailboxType mailboxType)
        {
            return new Mailbox(mailboxType.Create(ActorRefs.Nobody, _actorSystem));
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 ConcurrentQueueMailboxes", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void UnboundedMailbox()
        {
            for (var i = 0; i < MailboxCreateNumber; i++)
            {
                _mailboxes.Add(CreateMailbox(_unboundedMailboxType));
                _createMailboxThroughput.Increment();
            }
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 UnboundedDequeBasedMailboxes", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void UnboundedDequeBasedMailbox()
        {
            for (var i = 0; i < MailboxCreateNumber; i++)
            {
                _mailboxes.Add(CreateMailbox(_unboundedDequeBasedMailboxType));
                _createMailboxThroughput.Increment();
            }
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 BoundedDequeBasedMailboxes", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void BoundedMailbox()
        {
            for (var i = 0; i < MailboxCreateNumber; i++)
            {
                _mailboxes.Add(CreateMailbox(_boundedMailboxType));
                _createMailboxThroughput.Increment();
            }
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 BoundedDequeBasedMailboxes", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void BoundedDequeBasedMailbox()
        {
            for (var i = 0; i < MailboxCreateNumber; i++)
            {
                _mailboxes.Add(CreateMailbox(_boundedDequeBasedMailboxType));
                _createMailboxThroughput.Increment();
            }
        }

        [PerfCleanup]
        public void Teardown(BenchmarkContext context)
        {
            var shutdownTimeout = TimeSpan.FromSeconds(1);
            var cleanShutdown = _actorSystem.Terminate().Wait(shutdownTimeout);
            if (!cleanShutdown)
            {
                context.Trace.Warning($"Failed to shutdown ActorSystem within {shutdownTimeout}");
            }
            _mailboxes.Clear();
            _mailboxes = null;
        }
    }
}

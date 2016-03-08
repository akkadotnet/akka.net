using System.Collections.Generic;
using Akka.Dispatch;
using NBench;

namespace Akka.Tests.Performance.Dispatch
{
    public class MailboxMemoryFootprintSpec
    {
        private Counter _createMailboxThroughput;

        private const string CreateThroughputCounter = "MailboxCreateThroughput";
        private const int MailboxCreateNumber = 10000;

        private List<Mailbox> _mailboxes;
        
        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _createMailboxThroughput = context.GetCounter(CreateThroughputCounter);
            _mailboxes = new List<Mailbox>(MailboxCreateNumber);
        }

        [PerfBenchmark(Description = "Measures the amount of memory used by 10,000 ConcurrentQueueMailboxes", RunMode = RunMode.Iterations, NumberOfIterations = 13, TestMode = TestMode.Measurement)]
        [MemoryMeasurement(MemoryMetric.TotalBytesAllocated)]
        [CounterMeasurement(CreateThroughputCounter)]
        public void ConcurrentQueueMailbox()
        {
            for (var i = 0; i < MailboxCreateNumber; i++)
            {
                _mailboxes.Add(new ConcurrentQueueMailbox());
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
                _mailboxes.Add(new UnboundedDequeBasedMailbox());
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
                _mailboxes.Add(new BoundedDequeBasedMailbox());
                _createMailboxThroughput.Increment();
            }
        }

        [PerfCleanup]
        public void Teardown()
        {
            _mailboxes.Clear();
            _mailboxes = null;
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="BatchWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using DotNetty.Buffers;
using DotNetty.Common.Concurrency;
using DotNetty.Transport.Channels;
using Akka.Configuration;

namespace Akka.Remote.Transport.DotNetty
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Configuration object for <see cref="BatchWriter"/>
    /// </summary>
    internal class BatchWriterSettings
    {
        public const int DefaultMaxPendingWrites = 30;
        public const long DefaultMaxPendingBytes = 16 * 1024L;
        public static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromMilliseconds(40);

        public BatchWriterSettings(Config hocon)
        {
            EnableBatching = hocon.GetBoolean("enabled", true);
            MaxPendingWrites = hocon.GetInt("max-pending-writes", DefaultMaxPendingWrites);
            MaxPendingBytes = hocon.GetByteSize("max-pending-bytes", null) ?? DefaultMaxPendingBytes;
            FlushInterval = hocon.GetTimeSpan("flush-interval", DefaultFlushInterval, false);
        }

        public BatchWriterSettings(TimeSpan? maxDuration = null, bool enableBatching = true, 
            int maxPendingWrites = DefaultMaxPendingWrites, long maxPendingBytes = DefaultMaxPendingBytes)
        {
            EnableBatching = enableBatching;
            MaxPendingWrites = maxPendingWrites;
            FlushInterval = maxDuration ?? DefaultFlushInterval;
            MaxPendingBytes = maxPendingBytes;
        }

        /// <summary>
        /// Toggle for turning this feature on or off.
        /// </summary>
        /// <remarks>
        /// Defaults to <c>true</c>.
        /// </remarks>
        public bool EnableBatching { get; }

        /// <summary>
        /// The maximum amount of buffered writes that can be buffered before flushing I/O.
        /// </summary>
        /// <remarks>
        /// Defaults to 30.
        /// </remarks>
        public int MaxPendingWrites { get; }

        /// <summary>
        /// In the event of low-traffic channels, the maximum amount of time we'll wait before flushing writes.
        /// </summary>
        /// <remarks>
        /// Defaults to 40 milliseconds.
        /// </remarks>
        public TimeSpan FlushInterval { get; }

        /// <summary>
        /// The maximum number of outstanding bytes that can be written prior to a flush.
        /// </summary>
        /// <remarks>
        /// Defaults to 16kb.
        /// </remarks>
        public long MaxPendingBytes { get; }
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// Responsible for batching socket writes together into fewer sys calls to the socket.
    /// </summary>
    internal class BatchWriter : ChannelHandlerAdapter
    {
        public readonly BatchWriterSettings Settings;

        internal bool CanSchedule { get; private set; } = true;

        public BatchWriter(BatchWriterSettings settings)
        {
            Settings = settings;
        }

        private int _currentPendingWrites = 0;
        private long _currentPendingBytes;

        public bool HasPendingWrites => _currentPendingWrites > 0;

        public override void HandlerAdded(IChannelHandlerContext context)
        {
            if(Settings.EnableBatching)
                ScheduleFlush(context); // only schedule flush operations when batching is enabled
            base.HandlerAdded(context);
        }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            /*
             * Need to add the write to the rest of the pipeline first before we
             * include it in the formula for determining whether or not we flush
             * right now. The reason being is that if we did this the other way around,
             * we could flush first before the write was in the "flushable" buffer and
             * this can lead to "dangling writes" that never actually get transmitted
             * across the network.
             */
            var write = base.WriteAsync(context, message);
            if (Settings.EnableBatching)
            {
                _currentPendingBytes += ((IByteBuffer)message).ReadableBytes;
                _currentPendingWrites++;
                if (_currentPendingWrites >= Settings.MaxPendingWrites
                    || _currentPendingBytes >= Settings.MaxPendingBytes)
                {
                    context.Flush();
                    Reset();
                }
            }
            else
            {
                context.Flush();
            }
           

            return write;
        }

        public override Task CloseAsync(IChannelHandlerContext context)
        {
            // flush any pending writes first
            context.Flush();
            CanSchedule = false;
            return base.CloseAsync(context);
        }

        private void ScheduleFlush(IChannelHandlerContext context)
        {
            // Schedule a recurring flush - only fires when there's writable data
            var task = new FlushTask(context, Settings.FlushInterval, this);
            context.Executor.Schedule(task, Settings.FlushInterval);
        }

        public void Reset()
        {
            _currentPendingWrites = 0;
            _currentPendingBytes = 0;
        }

        class FlushTask : IRunnable
        {
            private readonly IChannelHandlerContext _context;
            private readonly TimeSpan _interval;
            private readonly BatchWriter _writer;

            public FlushTask(IChannelHandlerContext context, TimeSpan interval, BatchWriter writer)
            {
                _context = context;
                _interval = interval;
                _writer = writer;
            }

            public void Run()
            {
                if (_writer.HasPendingWrites)
                {
                    // execute a flush operation
                    _context.Flush();
                    _writer.Reset();
                }

                if(_writer.CanSchedule)
                    _context.Executor.Schedule(this, _interval); // reschedule
            }
        }
    }
}

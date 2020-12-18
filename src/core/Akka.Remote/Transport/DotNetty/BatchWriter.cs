//-----------------------------------------------------------------------
// <copyright file="BatchWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
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
        private class FlushCmd
        {
            public static readonly FlushCmd Instance = new FlushCmd();
            private FlushCmd() { }
        }

        public readonly BatchWriterSettings Settings;
        public readonly IScheduler Scheduler;
        private ICancelable _flushSchedule;

        public BatchWriter(BatchWriterSettings settings, IScheduler scheduler)
        {
            Settings = settings;
            Scheduler = scheduler;
        }

        private int _currentPendingWrites = 0;
        private long _currentPendingBytes;

        public bool HasPendingWrites => _currentPendingWrites > 0;

        public override void HandlerAdded(IChannelHandlerContext context)
        {
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

            _currentPendingBytes += ((IByteBuffer)message).ReadableBytes;
            _currentPendingWrites++;
            if (_currentPendingWrites >= Settings.MaxPendingWrites
                || _currentPendingBytes >= Settings.MaxPendingBytes)
            {
                context.Flush();
                Reset();
            }

            return write;
        }

        public override void Flush(IChannelHandlerContext context)
        {
            // reset statistics upon flush
            Reset();
            base.Flush(context);
        }

        public override void UserEventTriggered(IChannelHandlerContext context, object evt)
        {
            if (evt is FlushCmd)
            {
                if (HasPendingWrites)
                {
                    context.Flush();
                    Reset();
                }
            }
            else
            {
                base.UserEventTriggered(context, evt);
            }
        }

        public override Task CloseAsync(IChannelHandlerContext context)
        {
            // flush any pending writes first
            context.Flush();
            _flushSchedule?.Cancel();
            return base.CloseAsync(context);
        }

        private void ScheduleFlush(IChannelHandlerContext context)
        {
            // Schedule a recurring flush - only fires when there's writable data
            _flushSchedule = Scheduler.Advanced.ScheduleRepeatedlyCancelable(Settings.FlushInterval,
                Settings.FlushInterval,
                () =>
                {
                    // want to fire this event through the top of the pipeline
                    context.Channel.Pipeline.FireUserEventTriggered(FlushCmd.Instance);
                });
        }

        public void Reset()
        {
            _currentPendingWrites = 0;
            _currentPendingBytes = 0;
        }
    }
}

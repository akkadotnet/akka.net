//-----------------------------------------------------------------------
// <copyright file="BatchWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using DotNetty.Buffers;
using DotNetty.Common.Concurrency;
using DotNetty.Transport.Channels;
using Akka.Configuration;

namespace Akka.Remote.Transport.DotNetty
{
    internal class FlushConsolidationHandler : ChannelDuplexHandler
    {
        /// <summary>
        /// The default number of flushes after which a flush will be forwarded to downstream handlers (whether while in a
        /// read loop, or while batching outside of a read loop).
        /// </summary>
        public const int DEFAULT_EXPLICIT_FLUSH_AFTER_FLUSHES = 256;

        public int ExplicitFlushAfterFlushes { get; }
        public bool ConsolidateWhenNoReadInProgress { get; }

        public readonly IScheduler Scheduler;
        private int _flushPendingCount;
        private bool _readInProgress;
        private IChannelHandlerContext _context;
        private CancellationTokenSource _nextScheduledFlush;
        private FlushTask _flushTask;
        private Func<bool> _flushFunc;

        private class FlushTask : IRunnable
        {
            private readonly FlushConsolidationHandler _handler;

            public FlushTask(FlushConsolidationHandler handler)
            {
                _handler = handler;
            }

            public void Run()
            {
                if (_handler._flushPendingCount > 0 && !_handler._readInProgress)
                {
                    _handler._flushPendingCount = 0;
                    _handler._nextScheduledFlush?.Dispose();
                    _handler._nextScheduledFlush = null;
                    _handler._context.Flush();
                } // else we'll flush when the read completes
            }
        }

        public FlushConsolidationHandler() : this(DEFAULT_EXPLICIT_FLUSH_AFTER_FLUSHES, true){}

        public FlushConsolidationHandler(int explicitFlushAfterFlushes, bool consolidateWhenNoReadInProgress)
        {
            ExplicitFlushAfterFlushes = explicitFlushAfterFlushes;
            ConsolidateWhenNoReadInProgress = consolidateWhenNoReadInProgress;
            if (consolidateWhenNoReadInProgress)
            {
                _flushTask = new FlushTask(this);
                _flushFunc = () =>
                {
                    _flushTask?.Run();
                    return true;
                };
            }
        }

        public override void HandlerAdded(IChannelHandlerContext context)
        {
            _context = context;
        }

        public override void Flush(IChannelHandlerContext context)
        {
            if (_readInProgress)
            {
                // If there is still a read in progress we are sure we will see a channelReadComplete(...) call. Thus
                // we only need to flush if we reach the explicitFlushAfterFlushes limit.
                if (++_flushPendingCount == ExplicitFlushAfterFlushes)
                {
                    FlushNow(context);
                }
            } else if (ConsolidateWhenNoReadInProgress)
            {
                // Flush immediately if we reach the threshold, otherwise schedule
                if (++_flushPendingCount == ExplicitFlushAfterFlushes)
                {
                    FlushNow(context);
                }
                else
                {
                    ScheduleFlush(context);
                }
            }
            else
            {
                // Always flush directly
                FlushNow(context);
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext context)
        {
            // This may be the last event in the read loop, so flush now!
            ResetReadAndFlushIfNeeded(context);
            context.FireChannelReadComplete();
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            _readInProgress = true;
            context.FireChannelRead(message);
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            // To ensure we not miss to flush anything, do it now.
            ResetReadAndFlushIfNeeded(context);
            context.FireExceptionCaught(exception);
        }

        public override Task DisconnectAsync(IChannelHandlerContext context)
        {
            // Try to flush one last time if flushes are pending before disconnect the channel.
            ResetReadAndFlushIfNeeded(context);
            return context.DisconnectAsync();
        }

        public override Task CloseAsync(IChannelHandlerContext context)
        {
            // Try to flush one last time if flushes are pending before disconnect the channel.
            ResetReadAndFlushIfNeeded(context);
            return context.CloseAsync();
        }

        public override void ChannelWritabilityChanged(IChannelHandlerContext context)
        {
            if (!context.Channel.IsWritable)
            {
                // The writability of the channel changed to false, so flush all consolidated flushes now to free up memory.
                FlushIfNeeded(context);
            }

            context.FireChannelWritabilityChanged();
        }

        public override void HandlerRemoved(IChannelHandlerContext context)
        {
            FlushIfNeeded(context);
            _flushFunc = null;
            _flushTask = null;
        }

        private void ResetReadAndFlushIfNeeded(IChannelHandlerContext ctx)
        {
            _readInProgress = false;
            FlushIfNeeded(ctx);
        }

        private void FlushIfNeeded(IChannelHandlerContext ctx)
        {
            if (_flushPendingCount > 0)
            {
                FlushNow(ctx);
            }
        }

        private void FlushNow(IChannelHandlerContext ctx)
        {
            CancelScheduledFlush();
            _flushPendingCount = 0;
            ctx.Flush();
        }

        private void ScheduleFlush(IChannelHandlerContext ctx)
        {
            if (_nextScheduledFlush == null && ConsolidateWhenNoReadInProgress)
            {
                // Run as soon as possible, but still yield to give a chance for additional writes to enqueue.
                _nextScheduledFlush = new CancellationTokenSource();
                ctx.Channel.EventLoop.SubmitAsync(_flushFunc, _nextScheduledFlush.Token);
            }
        }

        private void CancelScheduledFlush()
        {
            if (_nextScheduledFlush != null)
            {
                _nextScheduledFlush.Cancel(false);
                _nextScheduledFlush = null;
            }
        }
    }

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

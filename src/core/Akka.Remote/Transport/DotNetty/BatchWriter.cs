//-----------------------------------------------------------------------
// <copyright file="BatchWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNetty.Transport.Channels;
using Akka.Configuration;

namespace Akka.Remote.Transport.DotNetty
{

    /* Adapted and Derived from  https://github.com/netty/netty/blob/4.1/handler/src/main/java/io/netty/handler/flush/FlushConsolidationHandler.java */
    /*
     * Copyright 2016 The Netty Project
     *
     * The Netty Project licenses this file to you under the Apache License,
     * version 2.0 (the "License"); you may not use this file except in compliance
     * with the License. You may obtain a copy of the License at:
     *
     *   https://www.apache.org/licenses/LICENSE-2.0
     *
     * Unless required by applicable law or agreed to in writing, software
     * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
     * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
     * License for the specific language governing permissions and limitations
     * under the License.
     */

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class FlushConsolidationHandler : ChannelDuplexHandler
    {
        /// <summary>
        /// The default number of flushes after which a flush will be forwarded to downstream handlers (whether while in a
        /// read loop, or while batching outside of a read loop).
        /// </summary>
        public const int DefaultExplicitFlushAfterFlushes = 256;

        public int ExplicitFlushAfterFlushes { get; }
        public bool ConsolidateWhenNoReadInProgress { get; }

        private int _flushPendingCount;
        private bool _readInProgress;
        private IChannelHandlerContext _context;
        private CancellationTokenSource _nextScheduledFlush;


        private static bool TryFlush(FlushConsolidationHandler handler)
        {
            if (handler._flushPendingCount > 0 && !handler._readInProgress)
            {
                handler._flushPendingCount = 0;
                handler._nextScheduledFlush?.Dispose();
                handler._nextScheduledFlush = null;
                handler._context.Flush();
                return true;
            } // else we'll flush when the read completes

            // didn't flush
            return false;
        }

        // cache the delegate
        private static readonly Func<object, bool> FlushOp = obj => TryFlush((FlushConsolidationHandler)obj);

        public FlushConsolidationHandler() : this(DefaultExplicitFlushAfterFlushes, true){}

        public FlushConsolidationHandler(int explicitFlushAfterFlushes) : this(explicitFlushAfterFlushes, true) { }

        public FlushConsolidationHandler(int explicitFlushAfterFlushes, bool consolidateWhenNoReadInProgress)
        {
            ExplicitFlushAfterFlushes = explicitFlushAfterFlushes;
            ConsolidateWhenNoReadInProgress = consolidateWhenNoReadInProgress;
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
                ctx.Channel.EventLoop.SubmitAsync(FlushOp, this, _nextScheduledFlush.Token);
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
            MaxExplicitFlushes = hocon.GetInt("max-pending-writes", DefaultMaxPendingWrites);
        }

        public BatchWriterSettings(TimeSpan? maxDuration = null, bool enableBatching = true,
            int maxExplicitFlushes = DefaultMaxPendingWrites, long maxPendingBytes = DefaultMaxPendingBytes)
        {
            EnableBatching = enableBatching;
            MaxExplicitFlushes = maxExplicitFlushes;
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
        public int MaxExplicitFlushes { get; }
    }
}

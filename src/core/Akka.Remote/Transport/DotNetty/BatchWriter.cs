//-----------------------------------------------------------------------
// <copyright file="BatchWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using DotNetty.Buffers;
using DotNetty.Common.Concurrency;
using DotNetty.Transport.Channels;

namespace Akka.Remote.Transport.DotNetty
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// Responsible for batching socket writes together into fewer sys calls to the socket.
    /// </summary>
    internal class BatchWriter : ChannelHandlerAdapter
    {
        private readonly int _maxPendingWrites;
        private readonly int _maxPendingMillis;
        private readonly int _maxPendingBytes;

        public BatchWriter(int maxPendingWrites = 20, int maxPendingMillis = 40, int maxPendingBytes = 128000)
        {
            _maxPendingWrites = maxPendingWrites;
            _maxPendingMillis = maxPendingMillis;
            _maxPendingBytes = maxPendingBytes;
        }

        private int _currentPendingWrites = 0;
        private long _currentPendingBytes;

        public bool HasPendingWrites => _currentPendingWrites > 0;

        public override void HandlerAdded(IChannelHandlerContext context)
        {
            ScheduleFlush(context);
            base.HandlerAdded(context);
        }

        public override Task WriteAsync(IChannelHandlerContext context, object message)
        {
            var write = base.WriteAsync(context, message);
            _currentPendingBytes += ((IByteBuffer)message).ReadableBytes;
            _currentPendingWrites++;
            if (_currentPendingWrites >= _maxPendingWrites
                || _currentPendingBytes >= _maxPendingBytes)
            {
                context.Flush();
                Reset();
            }

            return write;
        }

        void ScheduleFlush(IChannelHandlerContext context)
        {
            // Schedule a recurring flush - only fires when there's writable data
            var time = TimeSpan.FromMilliseconds(_maxPendingMillis);
            var task = new FlushTask(context, time, this);
            context.Executor.Schedule(task, time);
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

                // channel is still open
                if (_context.Channel.Open)
                {
                    _context.Executor.Schedule(this, _interval); // reschedule
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport.Streaming
{
    // This could be optimized with unsafe code
    internal static class LittleEndian
    {
        public static void WriteInt32(int value, byte[] buffer, int offset)
        {
            buffer[offset] = (byte)(value & 0x000000FF);
            buffer[offset + 1] = (byte)((value & 0x0000FF00) >> 8);
            buffer[offset + 2] = (byte)((value & 0x00FF0000) >> 16);
            buffer[offset + 3] = (byte)((value & 0xFF000000) >> 24);
        }

        public static int ReadInt32(byte[] buffer, int offset)
        {
            return buffer[offset] |
                   buffer[offset + 1] << 8 |
                   buffer[offset + 2] << 16 |
                   buffer[offset + 3] << 24;
        }
    }

    public class StreamAssociationHandle : AssociationHandle
    {
        private readonly StreamTransportSettings _settings;
        private readonly Stream _stream;
        private readonly object _state;
        private readonly AsyncQueue<ByteString> _writeQueue;
        private IHandleEventListener _eventListener;

        private readonly byte[] _readBuffer;
        private readonly byte[] _writeBuffer;

        private readonly TaskCompletionSource<bool> _initialized;
        private readonly TaskCompletionSource<bool> _disassociated;
        private readonly Task<bool> _stopped;
        private readonly Task<bool> _writeLoop;
        private readonly Task<bool> _readLoop;

        /// <summary>
        /// Returns true if gracefully stopped, otherwise false.
        /// </summary>
        public Task<bool> Stopped => _stopped;

        public StreamAssociationHandle(StreamTransportSettings settings, Stream stream, Address localAddress, Address remoteAddress, object state)
            : base(localAddress, remoteAddress)
        {
            _settings = settings;
            _stream = stream;
            _state = state;
            _writeQueue = new AsyncQueue<ByteString>(settings.RemoteDispatcher);

            _readBuffer = new byte[settings.StreamReadBufferSize];
            _writeBuffer = new byte[settings.StreamWriteBufferSize];

            _initialized = new TaskCompletionSource<bool>();
            _disassociated = new TaskCompletionSource<bool>();

            _stopped = Task.Run(() => StopRunner());
            _writeLoop = Task.Run(() => WriteLoop());
            _readLoop = Task.Run(() => ReadLoop());
        }

        internal void Initialize(IHandleEventListener eventListener)
        {
            _eventListener = eventListener;

            _readLoop.ContinueWith(task =>
            {
                bool gracefulStop = task.Result;

                //Log(gracefulStop, "Read loop completed");

                _disassociated.TrySetResult(gracefulStop);

            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            _writeLoop.ContinueWith(task =>
            {
                bool gracefulStop = task.Result;
                //Log(gracefulStop, "Write loop completed");
                _disassociated.TrySetResult(gracefulStop);

            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            _initialized.TrySetResult(true);
        }

        private void Log(bool isInfo, string message)
        {
            Log(isInfo ? LogLevel.InfoLevel : LogLevel.WarningLevel, message);
        }

        private void Log(LogLevel level, string message)
        {
            _settings.Log.Log(level, "{0} - ({1}:{2} -> {3}:{4})", message, LocalAddress.Host, LocalAddress.Port, RemoteAddress.Host, RemoteAddress.Port);
        }

        private async Task<bool> ReadLoop()
        {
            try
            {
                if (!await _initialized.Task)
                    return false;

                byte[] readBuffer = _readBuffer;
                int bufferLength = readBuffer.Length;

                int readBytes = 0;
                int readIndex = 0;

                while (true)
                {
                    int available = readBytes - readIndex;
                    if (available < sizeof (int))
                    {
                        // To simplify the message length read logic, we enforce the length bytes to be whole in the buffer

                        // Copy the partial length at the start of the buffer
                        if (available != 0)
                            Buffer.BlockCopy(readBuffer, readIndex, readBuffer, 0, available);

                        readIndex = 0;
                        readBytes = await _stream.ReadAsync(readBuffer, available, bufferLength - available).ConfigureAwait(false);

                        if (readBytes == 0)
                            return true;

                        // Adjust readBytes to include the partial length that we copied earlier
                        readBytes += available;
                    }

                    int payloadLength = LittleEndian.ReadInt32(readBuffer, readIndex);
                    readIndex += sizeof (int);

                    byte[] payloadBuffer = null;

                    if (payloadLength < 0 || payloadLength > _settings.FrameSizeHardLimit)
                    {
                        //TODO Log Error
                        return false;
                    }

                    if (payloadLength > _settings.MaximumFrameSize)
                    {
                        // This could happen if the MaximumFrameSize is configured bigger on the remote association.
                        // Normally they are dropped before getting sent so we don't receive them.

                        #region Skip the bytes

                        //TODO Log Error

                        int payloadOffset = 0;

                        while (payloadOffset < payloadLength)
                        {
                            if (readIndex == readBytes)
                            {
                                readIndex = 0;
                                readBytes = await _stream.ReadAsync(readBuffer, 0, bufferLength).ConfigureAwait(false);

                                if (readBytes == 0)
                                    return true;
                            }

                            available = Math.Min(readBytes - readIndex, payloadLength - payloadOffset);
                            readIndex += available;
                            payloadOffset += available;
                        }

                        #endregion
                    }
                    else if (payloadLength > _settings.ChunkedReadThreshold && payloadLength > bufferLength - readIndex)
                    {
                        // The payload is chunked as a protection against denial of service. Otherwise a malicious remote endpoint
                        // could make the node allocate big buffers without sending the data first.

                        #region Read Chunked

                        //TODO Could allow direct read if Socket.Available include the full payload length
                        //TODO The chunked buffers could be pooled (See RecyclableMemoryStream)

                        var chunks = new List<byte[]>();
                        int payloadOffset = 0;

                        while (payloadOffset < payloadLength)
                        {
                            if (readIndex == readBytes)
                            {
                                readIndex = 0;
                                readBytes = await _stream.ReadAsync(readBuffer, 0, bufferLength).ConfigureAwait(false);

                                if (readBytes == 0)
                                    return true;
                            }

                            available = Math.Min(readBytes - readIndex, payloadLength - payloadOffset);
                            byte[] chunk = new byte[available];
                            Buffer.BlockCopy(readBuffer, readIndex, chunk, 0, available);
                            chunks.Add(chunk);

                            readIndex += available;
                            payloadOffset += available;
                        }

                        payloadOffset = 0;
                        payloadBuffer = new byte[payloadLength];

                        foreach (byte[] chunk in chunks)
                        {
                            int chunkLength = chunk.Length;
                            Buffer.BlockCopy(chunk, 0, payloadBuffer, payloadOffset, chunkLength);
                            payloadOffset += chunkLength;
                        }

                        #endregion
                    }
                    else
                    {
                        payloadBuffer = new byte[payloadLength];
                        int payloadOffset = 0;

                        while (payloadOffset < payloadLength)
                        {
                            if (readIndex == readBytes)
                            {
                                readIndex = 0;
                                readBytes = await _stream.ReadAsync(readBuffer, 0, bufferLength).ConfigureAwait(false);

                                if (readBytes == 0)
                                    return true;
                            }

                            available = Math.Min(readBytes - readIndex, payloadLength - payloadOffset);

                            Buffer.BlockCopy(readBuffer, readIndex, payloadBuffer, payloadOffset, available);
                            readIndex += available;
                            payloadOffset += available;
                        }
                    }

                    if (payloadBuffer != null)
                    {
                        //Log(LogLevel.DebugLevel, "Payload received");
                        var payload = new InboundPayload(ByteString.Unsafe.FromBytes(payloadBuffer));
                        _eventListener.Notify(payload);
                    }
                }
            }
            catch (Exception)
            {
                //TODO Log
                return false;
            }
        }

        public override bool Write(ByteString payload)
        {
            //Log(LogLevel.DebugLevel, "Queuing outgoing payload");
            _writeQueue.Enqueue(payload);

            //TODO Implement backoff based on total queued message length
            //     But make sure the backoff is not buggy in EndpointWriter first.
            return true;
        }

        private async Task<bool> WriteLoop()
        {
            try
            {
                if (!await _initialized.Task)
                    return false;

                byte[] writeBuffer = _writeBuffer;
                int bufferLength = writeBuffer.Length;
                int bufferOffset = 0;

                while (true)
                {
                    var item = _writeQueue.DequeueAsync();

                    ByteString payload;

                    if (item.IsCompleted)
                    {
                        if (item.IsCanceled)
                        {
                            if (bufferOffset > 0)
                            {
                                // We are closing, flush the buffer
                                await _stream.WriteAsync(writeBuffer, 0, bufferOffset).ConfigureAwait(false);
                            }

                            break;
                        }

                        payload = item.Value;
                    }
                    else
                    {
                        if (bufferOffset > 0)
                        {
                            // We are about to wait for data, flush the buffer
                            await _stream.WriteAsync(writeBuffer, 0, bufferOffset).ConfigureAwait(false);
                            bufferOffset = 0;
                        }

                        await item;

                        if (item.IsCanceled)
                            break;

                        payload = item.Value;
                    }

                    if (bufferLength - bufferOffset < sizeof (int))
                    {
                        // Not enough space to write payload length, flush the buffer
                        await _stream.WriteAsync(writeBuffer, 0, bufferOffset).ConfigureAwait(false);
                        bufferOffset = 0;
                    }

                    // Copy the length
                    int payloadLength = payload.Length;

                    if (payloadLength > _settings.MaximumFrameSize)
                    {
                        //Drop the message
                        //TODO Log Error
                    }
                    else
                    {
                        LittleEndian.WriteInt32(payloadLength, writeBuffer, bufferOffset);
                        bufferOffset += sizeof (int);

                        byte[] payloadBuffer = ByteString.Unsafe.GetBuffer(payload);
                        int payloadOffset = 0;

                        while (payloadOffset < payloadLength)
                        {
                            if (bufferLength == bufferOffset)
                            {
                                // Flush the buffer
                                await _stream.WriteAsync(writeBuffer, 0, bufferOffset).ConfigureAwait(false);
                                bufferOffset = 0;
                            }

                            int available = Math.Min(bufferLength - bufferOffset, payloadLength - payloadOffset);

                            Buffer.BlockCopy(payloadBuffer, payloadOffset, writeBuffer, bufferOffset, available);
                            payloadOffset += available;
                            bufferOffset += available;
                        }
                    }
                }
            }
            catch (Exception)
            {
                //TODO Log
                return false;
            }

            return true;
        }

        private async Task<bool> StopRunner()
        {
            var gracefulStop = await _disassociated.Task;

            _initialized.TrySetResult(false);

            if (gracefulStop)
                gracefulStop = await TryGracefulStop();

            Log(gracefulStop, "Association stopped");

            _writeQueue.Dispose();
            _settings.CloseStream(_stream, _state);

            _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));

            return gracefulStop;
        }

        private async Task<bool> TryGracefulStop()
        {
            try
            {
                _writeQueue.CompleteAdding();

                using (CancellationTokenSource cancel = new CancellationTokenSource(_settings.FlushWaitTimeout))
                {
                    bool gracefulStop = await _writeLoop.WithCancellation(cancel.Token);

                    if (!gracefulStop)
                        return false;

                    _settings.ShutdownOutput(_stream, _state);

                    gracefulStop = await _readLoop.WithCancellation(cancel.Token);

                    if (!gracefulStop)
                        return false;
                }
            }
            catch (Exception)
            {
                //TODO Log
                return false;
            }

            return true;
        }

        public override void Disassociate()
        {
            //Log(true, "Disassociate");
            _disassociated.TrySetResult(true);
        }
    }
}

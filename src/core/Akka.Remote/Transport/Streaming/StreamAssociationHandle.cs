using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
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
        private readonly AsyncQueue<ByteString> _writeQueue;
        private IHandleEventListener _eventListener;

        private readonly byte[] _readBuffer;
        private readonly byte[] _writeBuffer;
        private readonly TimeSpan _flushWaitOnShutdown;

        private readonly TaskCompletionSource<bool> _initialized;
        private readonly TaskCompletionSource<bool> _stopped;
        private readonly Task _writeLoop;

        /// <summary>
        /// Returns true if gracefully stopped, otherwise false.
        /// </summary>
        public Task<bool> Stopped => _stopped.Task;

        public StreamAssociationHandle(StreamTransportSettings settings, Stream stream, Address localAddress, Address remoteAddress)
            : base(localAddress, remoteAddress)
        {
            _settings = settings;
            _stream = stream;
            _writeQueue = new AsyncQueue<ByteString>();

            _readBuffer = new byte[settings.StreamReadBufferSize];
            _writeBuffer = new byte[settings.StreamWriteBufferSize];
            _flushWaitOnShutdown = settings.FlushWaitTimeout;

            _initialized = new TaskCompletionSource<bool>();
            _stopped = new TaskCompletionSource<bool>();
            _writeLoop = Task.Run(() => WriteLoop());
        }

        internal void Initialize(IHandleEventListener eventListener)
        {
            _eventListener = eventListener;
            Task.Run(() => ReadLoop())
                .ContinueWith(_ =>
                {
                    if (Stopped.IsCompleted)
                        return;

                    _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
                }, TaskContinuationOptions.OnlyOnFaulted);

            _writeLoop.ContinueWith(_ =>
            {
                _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
            }, TaskContinuationOptions.OnlyOnFaulted);

            _initialized.TrySetResult(true);
        }

        private async Task ReadLoop()
        {
            //TODO Add 100% test coverage for this

            byte[] readBuffer = _readBuffer;
            int bufferLength = readBuffer.Length;

            int readBytes = 0;
            int readIndex = 0;

            while (true)
            {
                int available = readBytes - readIndex;
                if (available < sizeof(int))
                {
                    // To simplify the message length read logic, we enforce the length bytes be whole in the buffer

                    // Copy the partial length at the start of the buffer
                    if (available != 0)
                        Buffer.BlockCopy(readBuffer, readIndex, readBuffer, 0, available);

                    readIndex = 0;
                    readBytes = await _stream.ReadAsync(readBuffer, available, bufferLength - available).ConfigureAwait(false);

                    if (readBytes == 0)
                    {
                        _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
                        return;
                    }

                    // Adjust readBytes to include the partial length that we copied earlier
                    readBytes += available;
                }

                int payloadLength = LittleEndian.ReadInt32(readBuffer, readIndex);
                readIndex += sizeof(int);

                byte[] payloadBuffer = null;

                if (payloadLength < 0 || payloadLength > _settings.FrameSizeHardLimit)
                {
                    //TODO Log Error
                    _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
                    return;
                }
                if (payloadLength > _settings.MaximumFrameSize)
                {
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
                            {
                                _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
                                return;
                            }
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
                            {
                                _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
                                return;
                            }
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
                            {
                                _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
                                return;
                            }
                        }

                        available = Math.Min(readBytes - readIndex, payloadLength - payloadOffset);

                        Buffer.BlockCopy(readBuffer, readIndex, payloadBuffer, payloadOffset, available);
                        readIndex += available;
                        payloadOffset += available;
                    }
                }

                if (payloadBuffer != null)
                {
                    var payload = new InboundPayload(ByteString.Unsafe.FromBytes(payloadBuffer));
                    _eventListener.Notify(payload);
                }
            }
        }

        public override bool Write(ByteString payload)
        {
            _writeQueue.Enqueue(payload);

            // Always return true, we don't want to use the backoff in EndpointWriter
            return true; //TODO Investigate if we need to use the backoff
        }

        private async Task WriteLoop()
        {
            //TODO Add 100% test coverage for this

            if (!await _initialized.Task)
                return;

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
                        break;

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


                if (bufferLength - bufferOffset < sizeof(int))
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
                    bufferOffset += sizeof(int);

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

        public override void Disassociate()
        {
            _initialized.TrySetResult(false);
            _writeQueue.CompleteAdding();

            Task.WhenAny(_writeLoop, Task.Delay(_flushWaitOnShutdown))
                .ContinueWith(completedTask =>
            {
                if (completedTask == _writeLoop)
                {
                    _stopped.TrySetResult(true);
                }
                else
                {
                    //TODO Add logging
                    _stopped.TrySetResult(false);
                }

                _writeQueue.Dispose();
                _stream.Close();
            });
        }
    }
}

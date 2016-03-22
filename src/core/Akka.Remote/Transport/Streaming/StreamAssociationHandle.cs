using System;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport.Streaming
{
    public class StreamAssociationHandle : AssociationHandle
    {
        private const int UserBufferSize = 1024*8;

        private readonly Stream _stream;
        private readonly AsyncQueue<ByteString> _writeQueue;
        private IHandleEventListener _eventListener;

        private byte[] _readBuffer;
        private byte[] _writeBuffer;

        public StreamAssociationHandle(Stream stream, Address localAddress, Address remoteAddress)
            : base(localAddress, remoteAddress)
        {
            _stream = stream;
            _writeQueue = new AsyncQueue<ByteString>();
        }

        internal void Initialize(IHandleEventListener eventListener)
        {
            _readBuffer = new byte[UserBufferSize];
            _writeBuffer = new byte[UserBufferSize];

            _eventListener = eventListener;
            Task.Run(() => ReadLoop())
                .ContinueWith(_ =>
                {
                    _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
                }, TaskContinuationOptions.OnlyOnFaulted);

            Task.Run(() => WriteLoop())
                .ContinueWith(_ =>
                {
                    _eventListener.Notify(new Disassociated(DisassociateInfo.Unknown));
                }, TaskContinuationOptions.OnlyOnFaulted);
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

                int payloadLength = BitConverter.ToInt32(readBuffer, readIndex);
                readIndex += sizeof(int);

                byte[] payloadBuffer = new byte[payloadLength];
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

                var payload = new InboundPayload(ByteString.Unsafe.FromBytes(payloadBuffer));
                _eventListener.Notify(payload);
            }
        }

        public override bool Write(ByteString payload)
        {
            _writeQueue.Enqueue(payload);

            return true;
        }

        private async Task WriteLoop()
        {
            //TODO Add 100% test coverage for this

            byte[] writeBuffer = _writeBuffer;
            int bufferLength = writeBuffer.Length;
            int bufferOffset = 0;

            try
            {
                while (true)
                {
                    var dequeueTask = _writeQueue.Dequeue();

                    ByteString payload;

                    if (dequeueTask.IsCompleted)
                        payload = dequeueTask.Result;
                    else
                    {
                        if (bufferOffset > 0)
                        {
                            // We are about to wait for data, flush the buffer
                            await _stream.WriteAsync(writeBuffer, 0, bufferOffset).ConfigureAwait(false);
                            bufferOffset = 0;
                        }

                        payload = await dequeueTask.ConfigureAwait(false);
                    }


                    if (bufferLength - bufferOffset < sizeof (int))
                    {
                        // Not enough space to write payload length, flush the buffer
                        await _stream.WriteAsync(writeBuffer, 0, bufferOffset).ConfigureAwait(false);
                        bufferOffset = 0;
                    }

                    // Copy the length
                    // We can assume that the write buffer length will always be greater than sizeof(int)!
                    int payloadLength = payload.Length;
                    byte[] lengthBuffer = BitConverter.GetBytes(payloadLength);
                    Buffer.BlockCopy(lengthBuffer, 0, writeBuffer, bufferOffset, sizeof(int));
                    bufferOffset += 4;

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
            catch (TaskCanceledException)
            { }
        }

        public override void Disassociate()
        {
            _writeQueue.Dispose();
            _stream.Close();
        }
    }
}

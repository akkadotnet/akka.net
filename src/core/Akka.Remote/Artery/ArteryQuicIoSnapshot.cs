// -----------------------------------------------------------------------
// <copyright file="ArteryQuicIoSnapshot.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System.Threading;

namespace Akka.Remote.Artery
{
    /// <summary>Opt-in ordinary-channel I/O counters; native write completion is not delivery.</summary>
    internal readonly record struct ArteryQuicIoSnapshot(long ReadOperations, long ReadBytes, long WriteOperations, long WriteBytes)
    {
        public static ArteryQuicIoSnapshot operator +(ArteryQuicIoSnapshot left, ArteryQuicIoSnapshot right) =>
            new(left.ReadOperations + right.ReadOperations, left.ReadBytes + right.ReadBytes,
                left.WriteOperations + right.WriteOperations, left.WriteBytes + right.WriteBytes);

        public static ArteryQuicIoSnapshot operator -(ArteryQuicIoSnapshot left, ArteryQuicIoSnapshot right) =>
            new(left.ReadOperations - right.ReadOperations, left.ReadBytes - right.ReadBytes,
                left.WriteOperations - right.WriteOperations, left.WriteBytes - right.WriteBytes);
    }

    internal sealed class ArteryQuicIoCounters
    {
        private long _readOperations;
        private long _readBytes;
        private long _writeOperations;
        private long _writeBytes;

        internal void Read(int bytes)
        {
            Interlocked.Add(ref _readBytes, bytes);
            Interlocked.Increment(ref _readOperations);
        }

        internal void Write(int bytes)
        {
            Interlocked.Add(ref _writeBytes, bytes);
            Interlocked.Increment(ref _writeOperations);
        }

        internal ArteryQuicIoSnapshot Snapshot => new(Volatile.Read(ref _readOperations), Volatile.Read(ref _readBytes),
            Volatile.Read(ref _writeOperations), Volatile.Read(ref _writeBytes));
    }
}

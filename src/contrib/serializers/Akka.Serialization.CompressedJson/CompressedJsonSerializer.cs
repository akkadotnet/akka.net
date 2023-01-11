using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Serialization.CompressedJson
{
    public class CompressedJsonSerializer: NewtonSoftJsonSerializer
    {
        public CompressedJsonSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public CompressedJsonSerializer(ExtendedActorSystem system, Config config) : base(system, config)
        {
        }

        public CompressedJsonSerializer(ExtendedActorSystem system, NewtonSoftJsonSerializerSettings settings) : base(system, settings)
        {
        }

        public override int Identifier => 35;

        public override byte[] ToBinary(object obj)
        {
            return Compress(base.ToBinary(obj));
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            return base.FromBinary(Decompress(bytes), type);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] Compress(byte[] data)
        {
            using var compressedStream = new MemoryStream();
            using var compressor = new GZipStream(compressedStream, CompressionMode.Compress);
            compressor.Write(data, 0, data.Length);
            compressor.Flush(); // It is critical to flush here
            return compressedStream.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] Decompress(byte[] raw)
        {
            using var compressedStream = new MemoryStream(raw);
            using var compressor = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var uncompressedStream = new MemoryStream();
            compressor.CopyTo(uncompressedStream);
            return uncompressedStream.ToArray();
        }
        
    }
}
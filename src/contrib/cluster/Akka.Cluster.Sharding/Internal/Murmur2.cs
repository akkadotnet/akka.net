using System;
using System.Text;

namespace Akka.Cluster.Sharding.Internal
{
    public static class Murmur2
    {
        public static int ToPositive(this int value)
            => value & 0x7fffffff;

        public static int GetHash(this byte[] data)
        {
            const uint seed = 0x9747b28c;
            // 'm' and 'r' are mixing constants generated offline.
            // They're not really 'magic', they just happen to work well.
            const uint m = 0x5bd1e995;
            const int r = 24;

            var length = (uint)data.Length;

            // Initialize the hash to a random value
            var h = seed ^ length;
            for (var i = 0; i < length; i += 4)
            {
                var k = BitConverter.ToUInt32(data, i);
                k *= m;
                k ^= k >> r;
                k *= m;
                h *= m;
                h ^= k;
            }
            // Handle the last few bytes of the input array
            switch (length % 4)
            {
                case 3:
                    h ^= (uint)data[(length & ~3) + 2] << 16;
                    h ^= (uint)data[(length & ~3) + 1] << 8;
                    h ^= data[length & ~3];
                    h *= m;
                    break;
                case 2:
                    h ^= (uint)data[(length & ~3) + 1] << 8;
                    h ^= data[length & ~3];
                    h *= m;
                    break;
                case 1:
                    h ^= data[length & ~3];
                    h *= m;
                    break;
            }

            h ^= h >> 13;
            h *= m;
            h ^= h >> 15;
            return (int)h;
        }

        // To match https://github.com/apache/kafka/blob/trunk/clients/src/main/java/org/apache/kafka/clients/producer/internals/DefaultPartitioner.java#L59
        public static string ShardId(string entityId, int nrShards)
            => (GetHash(Encoding.UTF8.GetBytes(entityId)).ToPositive() % nrShards).ToString();
    }
}

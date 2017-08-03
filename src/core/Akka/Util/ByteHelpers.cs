using Akka.IO;

namespace Akka.Util
{
    public static class ByteHelpers
    {
        public static byte[] PutInt(this byte[] target, int x, int offset = 0, ByteOrder order = ByteOrder.BigEndian)
        {
            if (order == ByteOrder.BigEndian)
            {
                target[offset + 0] = (byte)(x >> 24);
                target[offset + 1] = (byte)(x >> 16);
                target[offset + 2] = (byte)(x >> 8);
                target[offset + 3] = (byte)(x >> 0);
            }
            else
            {
                target[offset + 0] = (byte)(x >> 0);
                target[offset + 1] = (byte)(x >> 8);
                target[offset + 2] = (byte)(x >> 16);
                target[offset + 3] = (byte)(x >> 24);
            }

            return target;
        }
    }
}
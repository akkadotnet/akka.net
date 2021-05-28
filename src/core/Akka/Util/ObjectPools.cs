using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace Akka.Util
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class ObjectPools
    {
        public static readonly DefaultObjectPoolProvider Provider = new DefaultObjectPoolProvider();

        public static readonly ObjectPool<StringBuilder> StringBuilders = Provider.CreateStringBuilderPool(10, 1000);
    }
}

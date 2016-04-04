//-----------------------------------------------------------------------
// <copyright file="Base64Encoding.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Text;

namespace Akka.Util
{
    public static class Base64Encoding
    {
        public const string Base64Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+~";

        public static string Base64Encode(this long value)
        {
            var sb = new StringBuilder();
            var next = value;
            do
            {
                var index = (int)(next & 63);
                sb.Append(Base64Chars[index]);
                next = next >> 6;
            } while(next != 0);
            return sb.ToString();
        }

        public static string Base64Encode(this string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            return System.Convert.ToBase64String(bytes);
        }
    }
}


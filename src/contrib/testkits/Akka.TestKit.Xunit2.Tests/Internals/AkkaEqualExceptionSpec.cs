//-----------------------------------------------------------------------
// <copyright file="AkkaEqualExceptionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.TestKit.Xunit2.Internals;
using Xunit;

namespace Akka.TestKit.Xunit2.Tests.Internals;

public static class AkkaEqualExceptionSpec
{
#if NETFRAMEWORK
    [Fact]
    public static void Constructor_deserializes_message()
    {
        var originalException = new AkkaEqualException("Test message");

        AkkaEqualException deserializedException;
        using (var memoryStream = new System.IO.MemoryStream())
        {
            var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            formatter.Serialize(memoryStream, originalException);
            memoryStream.Seek(0, System.IO.SeekOrigin.Begin);
            deserializedException = (AkkaEqualException)formatter.Deserialize(memoryStream);
        }

        Assert.Equal(originalException.Message, deserializedException.Message);
    }
#endif
}

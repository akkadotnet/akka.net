using Akka.IO;
using Akka.MultiNodeTestRunner.Shared.Logging;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTestRunner.Shared.Tests.Logging
{
    /// <summary>
    /// Ensures that we can send <see cref="ByteString"/> objects that contain full
    /// .NET types over the network when collecting data from multiple nodes in a given test
    /// </summary>
    public class ByteStringSerializationTests : AkkaSpec
    {
        private Serializer InternalSerializer => Sys.Serialization.FindSerializerFor(typeof (SpecPass));

        private ByteStringSerializer _serializer;

        protected ByteStringSerializer Serializer => _serializer ?? (_serializer = new ByteStringSerializer(InternalSerializer));

        [Fact]
        public void Should_serialize_POCO_in_ByteString()
        {
            var specPass = new SpecPass(1, "SomeTest");
            var serialized = Serializer.ToByteString(specPass);
            var deserialized = Serializer.FromByteString(serialized) as SpecPass;
            Assert.NotNull(deserialized);
            Assert.Equal(specPass.NodeIndex, deserialized.NodeIndex);
            Assert.Equal(specPass.TestDisplayName, deserialized.TestDisplayName);
        }
    }
}

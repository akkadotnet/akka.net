using Akka.Tests.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.CompressedJson.Tests
{
    public class CompressedSerializerSpec: AkkaSerializationSpec
    {
        public CompressedSerializerSpec() : base(typeof(CompressedJsonSerializer))
        {
        }
        
        [Fact(Skip = "JSON serializer can not serialize Config")]
        public override void CanSerializeConfig()
        {
            base.CanSerializeConfig();
        }
        
        [Fact(DisplayName = "Compression must be turned on")]
        public void SettingTest()
        {
            var serializer = (CompressedJsonSerializer) Sys.Serialization.FindSerializerForType(typeof(BigData));
            var bytes = serializer.ToBinary(new BigData());
            bytes.Length.Should().BeLessThan(10 * 1024); // compressed size should be less than 10Kb
            var deserialized = serializer.FromBinary<BigData>(bytes);
            deserialized.Message.Should().Be(new string('x', 5 * 1024 * 1024));
        }
        
        private class BigData
        {
            public string Message { get; } = new string('x', 5 * 1024 * 1024); // 5 megabyte worth of chars
        }
    }
}


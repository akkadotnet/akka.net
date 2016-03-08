using Akka.Tests.Serialization;

namespace Akka.Serialization.WireTests
{
    public class WireTests : AkkaSerializationSpec
    {
        public WireTests() : base(typeof (WireSerializer))
        {            
        }
    }
}

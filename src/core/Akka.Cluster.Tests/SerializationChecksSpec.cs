using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class SerializationChecksSpec : ClusterSpecBase
    {
        [Fact]
        public void Settings_serializemessages_and_serializecreators_must_be_on_for_tests()
        {
            Sys.Settings.SerializeAllCreators.ShouldBeTrue();
            Sys.Settings.SerializeAllMessages.ShouldBeTrue();
        }
    }
}

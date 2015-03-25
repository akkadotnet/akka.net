using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class AddressSpec
    {
        [Fact]
        public void Host_is_lowercased_when_created()
        {
            var address = new Address("akka", "test", "HOSTNAME");
            address.Host.ShouldBe("hostname");
        }
    }
}
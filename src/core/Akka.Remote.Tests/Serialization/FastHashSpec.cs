#if FSCHECK
using System.Linq;
using Akka.Remote.Serialization;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class FastHashSpec
    {
        [Property]
        public Property FastHash_safe_and_unsafe_should_return_same_values(NonEmptyString str)
        {
            var s = str.Get;
            var safe = FastHash.OfString(s);
            var un = FastHash.OfStringFast(s);

            return (safe == un).Label("Safe and unsafe hash values should match");
        }
    }
}
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.TestKit.Xunit2;

namespace DocsExamples.Configuration
{
    public interface IAppProtocol{}

    public sealed class Ack : IAppProtocol{ }

    public sealed class Nack : IAppProtocol{ }



    public class SerializationSetupDocSpec : TestKit
    {

    }
}

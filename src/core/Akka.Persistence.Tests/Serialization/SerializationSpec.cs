//-----------------------------------------------------------------------
// <copyright file="SerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests.Serialization
{


    public class SerializationSpec : AkkaSpec
    {
        private class TestMessage
        {
            public string Message { get; set; }

            public TestMessage(string message)
            {
                Message = message;
            }
        }

        private readonly Akka.Serialization.Serialization _serialization;

        public SerializationSpec() : base(
            Configs.Config(
                Configs.CustomSerializers,
                ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf") // for akka-persistence-message
            ))
        {
            _serialization = Sys.Serialization;  
        }

        [Fact]
        public void Serialization_respects_default_serializer_parameter()
        {
            var message = new TestMessage("this is my test message");
            var serializer = _serialization.FindSerializerFor(message, "json");
            Assert.Equal(1, serializer.Identifier); //by default configuration the serializer id for json == newtonsoft == 1
        }

        [Fact]
        public void Serialization_falls_back_to_system_default_if_unknown_serializer_parameter()
        {
            var message = new TestMessage("this is my test message");
            var serializer = _serialization.FindSerializerFor(message, "unicorn");
            //since unicorn is an unknown serializer, the system default will be used
            //which incedentally is JSON at the moment
            Assert.Equal(-5, serializer.Identifier); //system serializer is currently configured to be hyperion which is -5
        }
    }
}

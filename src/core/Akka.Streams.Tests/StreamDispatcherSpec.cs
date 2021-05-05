//-----------------------------------------------------------------------
// <copyright file="StreamDispatcherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Dispatch;
using Akka.TestKit;
using Xunit;
using FluentAssertions;

namespace Akka.Streams.Tests
{
    public class StreamDispatcherSpec : AkkaSpec
    {
        [Fact]
        public void The_default_blocking_io_dispatcher_for_streams_must_be_the_same_as_the_default_blocking_io_dispatcher_for_actors()
        {
            var materializer = ActorMaterializer.Create(Sys);

            var streamIoDispatcher = Sys.Dispatchers.Lookup(ActorAttributes.IODispatcher.Name);
            var actorIoDispatcher = Sys.Dispatchers.Lookup(Dispatchers.DefaultBlockingDispatcherId);

            streamIoDispatcher.Should().Be(actorIoDispatcher);
        }

        [Fact]
        public void The_deprecated_default_stream_io_dispatcher_must_be_the_same_as_the_default_blocking_io_dispatcher_for_actors()
        {
            var materializer = ActorMaterializer.Create(Sys);

            var streamIoDispatcher = Sys.Dispatchers.Lookup("akka.stream.default-blocking-io-dispatcher");
            var actorIoDispatcher = Sys.Dispatchers.Lookup(Dispatchers.DefaultBlockingDispatcherId);

            streamIoDispatcher.Should().Be(actorIoDispatcher);
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="FlowDispatcherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowDispatcherSpec : AkkaSpec
    {
        private readonly ActorMaterializerSettings _defaultSettings;

        public FlowDispatcherSpec(ITestOutputHelper helper) : base("my-dispatcher = akka.test.stream-dispatcher", helper)
        {
            _defaultSettings = ActorMaterializerSettings.Create(Sys);
        }

        [Fact(Skip = "Need to rebase with dev first")]
        public void Flow_with_dispatcher_setting_must_use_the_default_dispatcher()
        {
            var materializer = ActorMaterializer.Create(Sys, _defaultSettings);

            var probe = CreateTestProbe();
            Source.From(Enumerable.Range(1, 3)).MapMaterializedValue(i =>
            {
                probe.Ref.Tell(Thread.CurrentThread.Name);
                return i;
            }).To(Sink.Ignore<int>()).Run(materializer);

            probe.ReceiveN(3).Where(s=>s is string).Cast<string>().ForEach(s =>
            {
                s.Should().StartWith(Sys.Name + "-akka.test.stream-dispatcher");
            });
        }

        [Fact(Skip = "Need to rebase with dev first")]
        public void Flow_with_dispatcher_setting_must_use_custom_dispatcher()
        {
            var materializer = ActorMaterializer.Create(Sys, _defaultSettings.WithDispatcher("my-dispatcher"));

            var probe = CreateTestProbe();
            Source.From(Enumerable.Range(1, 3)).MapMaterializedValue(i =>
            {
                probe.Ref.Tell(Thread.CurrentThread.Name);
                return i;
            }).To(Sink.Ignore<int>()).Run(materializer);

            probe.ReceiveN(3).Where(s => s is string).Cast<string>().ForEach(s =>
            {
                s.Should().StartWith(Sys.Name + "-my-dispatcher");
            });
        }
    }
}

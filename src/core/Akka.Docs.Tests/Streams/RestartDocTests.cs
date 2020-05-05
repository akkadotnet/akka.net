//-----------------------------------------------------------------------
// <copyright file="RestartDocTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DocsExamples.Streams
{
    public class RestartDocTests : TestKit
    {
        private ActorMaterializer Materializer { get; }

        public RestartDocTests(ITestOutputHelper output)
            : base("{}", output)
        {
            Materializer = Sys.Materializer();
        }

        private void DoSomethingElse()
        {
        }

        [Fact]
        public void Restart_stages_should_demonstrate_a_restart_with_backoff_source()
        {
            #region restart-with-backoff-source
            var httpClient = new HttpClient();

            var restartSource = RestartSource.WithBackoff(() =>
                {
                    // Create a source from a task
                    return Source.FromTask(
                        httpClient.GetAsync("http://example.com/eventstream") // Make a single request
                    )
                    .Select(c => c.Content.ReadAsStringAsync())
                    .Select(c => c.Result);
                }, 
                minBackoff: TimeSpan.FromSeconds(3), 
                maxBackoff: TimeSpan.FromSeconds(30),
                randomFactor: 0.2 // adds 20% "noise" to vary the intervals slightly
            );
            #endregion

            #region with-kill-switch
            var killSwitch = restartSource
                .ViaMaterialized(KillSwitches.Single<string>(), Keep.Right)
                .ToMaterialized(Sink.ForEach<string>(evt => Console.WriteLine($"Got event: {evt}")), Keep.Left)
                .Run(Materializer);

            DoSomethingElse();

            killSwitch.Shutdown();
            #endregion
        }
    }
}

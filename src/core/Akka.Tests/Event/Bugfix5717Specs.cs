//-----------------------------------------------------------------------
// <copyright file="Bugfix5717Specs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Event
{
    public class Bugfix5717Specs : AkkaSpec
    {
        public Bugfix5717Specs(ITestOutputHelper output) : base(Config.Empty, output){}
        
        /// <summary>
        /// Reproduction for https://github.com/akkadotnet/akka.net/issues/5717
        /// </summary>
        [Fact]
        public async Task Should_unsubscribe_from_all_topics_on_Terminate()
        {
            var es = Sys.EventStream;
            var tm1 = 1;
            var tm2 = "FOO";
            var a1 = CreateTestProbe();
            var a2 = CreateTestProbe();

            es.Subscribe(a1.Ref, typeof(int));
            es.Subscribe(a2.Ref, typeof(int));
            es.Subscribe(a2.Ref, typeof(string));
            es.Publish(tm1);
            es.Publish(tm2);
            await a1.ExpectMsgAsync(tm1);
            await a2.ExpectMsgAsync(tm1);
            await a2.ExpectMsgAsync(tm2);

            // kill second test probe
            Watch(a2);
            Sys.Stop(a2);
            await ExpectTerminatedAsync(a2);

            /*
             * It's possible that the `Terminate` message may not have been processed by the
             * Unsubscriber yet, so we want to try this operation more than once to see if it
             * eventually executes the unsubscribe on the EventStream.
             *
             * If it still fails after multiple attempts, the issue is that the unsub was never
             * executed in the first place.
             */
            await AwaitAssertAsync(async () =>
            {
                await EventFilter.DeadLetter().ExpectAsync(0, async () =>
                {
                    es.Publish(tm1);
                    es.Publish(tm2);
                    await a1.ExpectMsgAsync(tm1);
                });
            }, interval:TimeSpan.FromSeconds(250));
        }       
    }
}

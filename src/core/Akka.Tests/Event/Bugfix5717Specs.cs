// //-----------------------------------------------------------------------
// // <copyright file="Bugfix5717Specs.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

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
        public void Should_unsubscribe_from_all_topics_on_Terminate()
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
            a1.ExpectMsg(tm1);
            a2.ExpectMsg(tm1);
            a2.ExpectMsg(tm2);

            // kill second test probe
            Watch(a2);
            Sys.Stop(a2);
            ExpectTerminated(a2);

            EventFilter.DeadLetter().Expect(0, () =>
            {
                es.Publish(tm1);
                es.Publish(tm2);
                a1.ExpectMsg(tm1);
            });
        }       
    }
}
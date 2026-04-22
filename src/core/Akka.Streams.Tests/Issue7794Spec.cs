// -----------------------------------------------------------------------
//  <copyright file="Issue7794Spec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Xunit;

namespace Akka.Streams.Tests.Implementation;

public class Issue7794Spec: AkkaSpec
{
    private ActorMaterializer Materializer { get; }

    public Issue7794Spec(ITestOutputHelper helper) : base(helper)
    {
        Materializer = Sys.Materializer();
    }
    
    [Fact(DisplayName = "ChannelSource should not throw NRE when Channel completes")]
    public async Task Issue_7794_ChannelSource_NRE()
    {
        var channel = Channel.CreateBounded<Message<string, string>>(new BoundedChannelOptions(capacity: 100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

        var streamRes = ChannelSource.FromReader(channel.Reader)
            .Select(e => e)
            .RunWith(Sink.Ignore<Message<string, string>>(), Materializer);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            channel.Writer.Complete();
        });

        await streamRes;
    }
    
    private class Message<TKey, TValue>
    {
        public TKey Key { get; set; }
        public TValue Value { get; set; }
    }
}
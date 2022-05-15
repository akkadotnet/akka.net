//-----------------------------------------------------------------------
// <copyright file="BugSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Streams.Tests
{
    public class BugSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public BugSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public async Task Issue_4580_EmptyByteStringCausesPipeToBeClosed()
        {
            var serverPipe = new NamedPipeServerStream("unique-pipe-name", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 20, 20);
            var serverPipeConnectionTask = serverPipe.WaitForConnectionAsync();

            var clientPipe = new NamedPipeClientStream(".", "unique-pipe-name", PipeDirection.In, PipeOptions.Asynchronous);
            clientPipe.Connect();

            await serverPipeConnectionTask;
            var cnt = 0;

            var writeToStreamTask = Source.From(Enumerable.Range(0, 100))
                .Select(i => ByteString.FromString(i.ToString()))
                .Select(bs => cnt++ == 10 ? ByteString.Empty : bs) // ByteString.Empty.ToArray() failed in original bug
                .ToMaterialized(StreamConverters.FromOutputStream(() => serverPipe), Keep.Right)
                .Run(Materializer);

            var result = new List<string>();
            var readFromStreamTask = StreamConverters.FromInputStream(() => clientPipe, 1)
                .RunForeach(bs => result.Add(bs.ToString(Encoding.ASCII)), Materializer);

            await Task.WhenAll(writeToStreamTask, readFromStreamTask);

            var expected = Enumerable.Range(0, 100)
                .SelectMany(i => i == 10 ? Array.Empty<string>() : i.ToString().Select(c => c.ToString()));
            expected.SequenceEqual(result).ShouldBeTrue();
        }

        [Fact]
        public async Task WithAttribute_should_be_inserted_last()
        {
            var stage = new BlankSinkStage<int>();
            
            var sink = Flow.FromGraph(stage)
                .ToMaterialized(Sink.Ignore<int>(), Keep.Right)
                .WithAttributes(new Attributes(new MarkerAttribute("WithAttribute")));
            
            await Source.From(new[] { 1, 2, 3, 4 })
                .RunWith(sink, Sys.Materializer());
            
            // Original bug: Marker will be "InitialAttribute", not "WithAttribute"
            ((MarkerAttribute)stage.Attributes.AttributeList.Last(a => a is MarkerAttribute)).Marker.Should().Be("WithAttribute");
        }

        [Fact]
        public async Task AddAttribute_should_be_inserted_last()
        {
            var stage = new BlankSinkStage<int>();
            
            var sink = Flow.FromGraph(stage)
                .ToMaterialized(Sink.Ignore<int>(), Keep.Right)
                .WithAttributes(new Attributes(new MarkerAttribute("AddAttribute")));
            
            await Source.From(new[] { 1, 2, 3, 4 })
                .RunWith(sink, Sys.Materializer());
            
            // Original bug: Marker will be "InitialAttribute", not "AddAttribute"
            ((MarkerAttribute)stage.Attributes.AttributeList.Last(a => a is MarkerAttribute)).Marker.Should().Be("AddAttribute");
        }

        private sealed class MarkerAttribute : Attributes.IAttribute
        {
            public readonly string Marker;

            public MarkerAttribute(string marker)
            {
                Marker = marker;
            }

            /// <inheritdoc/>
            public override string ToString() => "MarkerAttribute";
        }
        
        private class BlankSinkStage<T> : GraphStage<FlowShape<T, T>>
        {
            public Inlet<T> In { get; } = new Inlet<T>("source.in");
            public Outlet<T> Out { get; } = new Outlet<T>("source.out");
            
            public int IllegalDecideCallCount { get; private set; }
            
            // Original bug: InitialAttributes is injected to the end of the module attributes, not the front.
            protected override Attributes InitialAttributes { get; } =
                new Attributes(new MarkerAttribute("InitialAttribute"));
            public override FlowShape<T, T> Shape { get; }

            public Attributes Attributes => Module.Attributes;
            
            public BlankSinkStage()
            {
                Shape = new FlowShape<T, T>(In, Out);
            }
            
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new Logic(this);

            private class Logic : GraphStageLogic
            {
                public Logic(BlankSinkStage<T> stage) : base(stage.Shape)
                {
                    var localStage = stage;
                    
                    SetHandler(
                        localStage.In, 
                        onPush: () =>
                        {
                            Push(localStage.Out, Grab(localStage.In));
                        });
                    
                    SetHandler(
                        localStage.Out, 
                        onPull: () =>
                        {
                            TryPull(localStage.In);
                        });
                }
            }
        }
    }
}

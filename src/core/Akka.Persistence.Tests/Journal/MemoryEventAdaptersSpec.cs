//-----------------------------------------------------------------------
// <copyright file="MemoryEventAdaptersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Persistence.Tests.Journal
{
    public class MemoryEventAdaptersSpec: AkkaSpec
    {
        private readonly ExtendedActorSystem _extendedActorSystem;
        private readonly Config _memoryConfig;
        
        public MemoryEventAdaptersSpec()
        {
            var config = ConfigurationFactory.ParseString(@"
akka.persistence.journal {
  plugin = ""akka.persistence.journal.inmem""


  # adapters defined for all plugins
  common-event-adapter-bindings {
  }

  inmem {
    # showcases re-using and concating configuration of adapters

    event-adapters {
      example  = """ + typeof (ExampleEventAdapter).FullName + @", Akka.Persistence.Tests""
      marker   = """ + typeof (MarkerInterfaceAdapter).FullName + @", Akka.Persistence.Tests""
      precise  = """ + typeof (PreciseAdapter).FullName + @", Akka.Persistence.Tests""
      reader  = """ + typeof (ReaderAdapter).FullName + @", Akka.Persistence.Tests""
      writer  = """ + typeof (WriterAdapter).FullName + @", Akka.Persistence.Tests""
      another-reader  = """ + typeof(AnotherReaderAdapter).FullName + @", Akka.Persistence.Tests""
    }
    event-adapter-bindings = {
      """ + typeof (IEventMarkerInterface).FullName + @", Akka.Persistence.Tests"" = marker
      ""System.String"" = example
      """ + typeof (PreciseAdapterEvent).FullName + @", Akka.Persistence.Tests"" = precise
      """ + typeof (ReadMeEvent).FullName + @", Akka.Persistence.Tests"" = reader
      """ + typeof (WriteMeEvent).FullName + @", Akka.Persistence.Tests"" = writer
      """ + typeof(ReadMeTwiceEvent).FullName + @", Akka.Persistence.Tests"" = [reader, another-reader]
    }
  }
}").WithFallback(ConfigurationFactory.Default());

            _extendedActorSystem = (ExtendedActorSystem) Sys;
            _memoryConfig = config.GetConfig("akka.persistence.journal.inmem");
        }

        [Fact]
        public void EventAdapters_should_parse_configuration_and_resolve_adapter_definitions()
        {
            var adapters = EventAdapters.Create(_extendedActorSystem, _memoryConfig);
            adapters.Get<IEventMarkerInterface>().GetType().Should().Be(typeof (MarkerInterfaceAdapter));
        }

        [Fact]
        public void EventAdapters_should_pick_the_most_specific_adapter_available()
        {
            var adapters = EventAdapters.Create(_extendedActorSystem, _memoryConfig);

            // sanity check: precise case, matching non-user classes
            adapters.Get<string>().GetType().Should().Be(typeof (ExampleEventAdapter));

            // pick adapter by implemented marker interface
            adapters.Get<SampleEvent>().GetType().Should().Be(typeof (MarkerInterfaceAdapter));

            // more general adapter matches as well, but most specific one should be picked
            adapters.Get<PreciseAdapterEvent>().GetType().Should().Be(typeof (PreciseAdapter));

            // no adapter defined for Long, should return identity adapter
            adapters.Get<long>().GetType().Should().Be(typeof (IdentityEventAdapter));
        }

        [Fact] public void EventAdapters_should_fail_with_useful_message_when_binding_to_not_defined_adapter()
        {
            var badConfig = ConfigurationFactory.ParseString(@"
akka.persistence.journal.inmem {
  event-adapter-bindings {
    ""System.Integer"" = undefined-adapter
  }
}");
            var combinedConfig = badConfig.GetConfig("akka.persistence.journal.inmem");

            var ex = Assert.Throws<ArgumentException>(() => EventAdapters.Create(_extendedActorSystem, combinedConfig));
            ex.Message.Contains("System.Integer was bound to undefined event-adapter: undefined-adapter").Should().BeTrue();
        }

        [Fact]
        public void EventAdapters_should_allow_implementing_only_the_read_side_IReadEventAdapter()
        {
            var adapters = EventAdapters.Create(_extendedActorSystem, _memoryConfig);

            // read-side only adapter
            var readAdapter = adapters.Get<ReadMeEvent>();
            readAdapter.FromJournal(readAdapter.ToJournal(new ReadMeEvent()), "").Events.First().Should().Be("from-ReadMeEvent()");
        }

        [Fact]
        public void EventAdapters_should_allow_implementing_only_the_write_side_IWriteEventAdapter()
        {
            var adapters = EventAdapters.Create(_extendedActorSystem, _memoryConfig);

            // write-side only adapter
            var writeAdapter = adapters.Get<WriteMeEvent>();
            writeAdapter.FromJournal(writeAdapter.ToJournal(new WriteMeEvent()), "").Events.First().Should().Be("to-WriteMeEvent()");
        }

        [Fact]
        public void EventAdapters_should_allow_combining_only_the_readside_CombinedReadEventAdapter()
        {
            var adapters = EventAdapters.Create(_extendedActorSystem, _memoryConfig);

            // combined-read-side only adapter
            var readAdapter = adapters.Get<ReadMeTwiceEvent>();
            readAdapter.FromJournal(readAdapter.ToJournal(new ReadMeTwiceEvent()), "").Events
                .Select(c => c.ToString())
                .Should()
                .BeEquivalentTo("from-ReadMeTwiceEvent()", "again-ReadMeTwiceEvent()");
        }
    }

    public abstract class BaseTestAdapter : IEventAdapter
    {
        public virtual string Manifest(object evt)
        {
            return string.Empty;
        }

        public virtual object ToJournal(object evt)
        {
            return evt;
        }

        public virtual IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Single(evt);
        }
    }

    public class ExampleEventAdapter : BaseTestAdapter { }
    public class MarkerInterfaceAdapter : BaseTestAdapter { }
    public class PreciseAdapter : BaseTestAdapter { }

    public class ReadMeEvent
    {
        public override string ToString()
        {
            return "ReadMeEvent()";
        }
    }

    public class ReadMeTwiceEvent
    {
        public override string ToString()
        {
            return "ReadMeTwiceEvent()";
        }
    }

    public class ReaderAdapter : IReadEventAdapter
    {
        public IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Single("from-" + evt);
        }
    }

    public class AnotherReaderAdapter : IReadEventAdapter
    {
        public IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Single("again-" + evt);
        }
    }

    public class WriteMeEvent
    {
        public override string ToString()
        {
            return "WriteMeEvent()";
        }
    }
    public class WriterAdapter : IWriteEventAdapter
    {
        public string Manifest(object evt)
        {
            return string.Empty;
        }

        public object ToJournal(object evt)
        {
            return "to-" + evt;
        }
    }

    public interface IEventMarkerInterface { }
    public sealed class SampleEvent : IEventMarkerInterface { }
    public sealed class PreciseAdapterEvent : IEventMarkerInterface { }
}

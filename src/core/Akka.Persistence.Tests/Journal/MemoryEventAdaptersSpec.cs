//-----------------------------------------------------------------------
// <copyright file="MemoryEventAdapterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests.Journal
{
    public class MemoryEventAdaptersSpec: AkkaSpec
    {
        private readonly ExtendedActorSystem _extendedActorySystem;
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
    }
    event-adapter-bindings = {
      """ + typeof (IEventMarkerInterface).FullName + @", Akka.Persistence.Tests"" = marker
      ""System.String"" = example
      """ + typeof (PreciseAdapterEvent).FullName + @", Akka.Persistence.Tests"" = precise
      """ + typeof (ReadMeEvent).FullName + @", Akka.Persistence.Tests"" = reader
      """ + typeof (WriteMeEvent).FullName + @", Akka.Persistence.Tests"" = writer
    }
  }
}").WithFallback(ConfigurationFactory.Load());

            _extendedActorySystem = (ExtendedActorSystem) Sys;
            _memoryConfig = config.GetConfig("akka.persistence.journal.inmem");
        }

        [Fact]
        public void EventAdapters_should_parse_configuration_and_resolve_adapter_definitions()
        {
            var adapters = EventAdapters.Create(_extendedActorySystem, _memoryConfig);
            adapters.Get<IEventMarkerInterface>().GetType().ShouldBe(typeof (MarkerInterfaceAdapter));
        }

        [Fact]
        public void EventAdapters_should_pick_the_most_specific_adapter_available()
        {
            var adapters = EventAdapters.Create(_extendedActorySystem, _memoryConfig);

            // sanity check: precise case, matching non-user classes
            adapters.Get<string>().GetType().ShouldBe(typeof (ExampleEventAdapter));

            // pick adapter by implemented marker interface
            adapters.Get<SampleEvent>().GetType().ShouldBe(typeof (MarkerInterfaceAdapter));

            // more general adapter matches as well, but most specific one should be picked
            adapters.Get<PreciseAdapterEvent>().GetType().ShouldBe(typeof (PreciseAdapter));

            // no adapter defined for Long, should return identity adapter
            adapters.Get<long>().GetType().ShouldBe(typeof (IdentityEventAdapter));
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

            var ex = Assert.Throws<ArgumentException>(() => EventAdapters.Create(_extendedActorySystem, combinedConfig));
            ex.Message.Contains("System.Integer was bound to undefined event-adapter: undefined-adapter").ShouldBeTrue();
        }

        [Fact]
        public void EventAdapters_should_allow_implementing_only_the_read_side_IReadEventAdapter()
        {
            var adapters = EventAdapters.Create(_extendedActorySystem, _memoryConfig);

            // read-side only adapter
            var readAdapter = adapters.Get<ReadMeEvent>();
            readAdapter.FromJournal(readAdapter.ToJournal(new ReadMeEvent()), "").Events.First().ShouldBe("from-ReadMeEvent()");
        }

        [Fact]
        public void EventAdapters_should_allow_implementing_only_the_write_side_IWriteEventAdapter()
        {
            var adapters = EventAdapters.Create(_extendedActorySystem, _memoryConfig);

            // write-side only adapter
            var writeAdapter = adapters.Get<WriteMeEvent>();
            writeAdapter.FromJournal(writeAdapter.ToJournal(new WriteMeEvent()), "").Events.First().ShouldBe("to-WriteMeEvent()");
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
    public class ReaderAdapter : IReadEventAdapter
    {
        public IEventSequence FromJournal(object evt, string manifest)
        {
            return EventSequence.Single("from-" + evt);
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
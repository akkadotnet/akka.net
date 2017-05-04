---
uid: event-adapters
title: Event Adapters
---
# Event adapters
In long running projects using event sourcing sometimes the need arises to detach the data model from the domain model completely.

Event Adapters help in situations where:

- **Version Migrations** – existing events stored in Version 1 should be "upcasted" to a new Version 2 representation, and the process of doing so involves actual code, not just changes on the serialization layer. For these scenarios the toJournal function is usually an identity function, however the fromJournal is implemented as `v1.Event=>v2.Event`, performing the necessary mapping inside the `FromJournal` method. This technique is sometimes referred to as "upcasting" in other CQRS libraries.
- **Separating Domain and Data models** – thanks to EventAdapters it is possible to completely separate the domain model from the model used to persist data in the Journals. For example one may want to use case classes in the domain model, however persist their protocol-buffer (or any other binary serialization format) counter-parts to the Journal. A simple `ToJournal:MyModel=>MyDataModel` and `FromJournal:MyDataModel=>MyModel` adapter can be used to implement this feature.
- **Journal Specialized Data Types** – exposing data types understood by the underlying Journal, for example for data stores which understand JSON it is possible to write an EventAdapter `ToJournal:object=>JSON` such that the Journal can directly store the json instead of serializing the object to its binary representation.

```csharp
public class MyEventAdapter : IEventAdapter
{
    public string Manifest(object evt)
    {
        return string.Empty; // when no manifest needed, return ""
    }

    public object ToJournal(object evt)
    {
        return evt; // identity
    }

    public IEventSequence FromJournal(object evt, string manifest)
    {
        return EventSequence.Single(evt); // identity
    }
}
```
Then in order for it to be used on events coming to and from the journal you must bind it using the below configuration syntax:
```hocon
akka.persistence.journal {
	<journal_identifier> {
		event-adapters {
			tagging = "<fully qualified event adapter type name with assembly>"
			v1 = "<fully qualified event adapter type name with assembly>"
			v2 = "<fully qualified event adapter type name with assembly>"
		}

		event-adapter-bindings {
			"<fully qualified event type name with assembly>" = v1
			"<fully qualified event type name with assembly>" = [v2, tagging]
		}
	}
}
```
It is possible to bind multiple adapters to one class for recovery, in which case the `FromJournal` methods of all bound adapters will be applied to a given matching event (in order of definition in the configuration). Since each adapter may return from 0 to n adapted events (called as `EventSequence`), each adapter can investigate the event and if it should indeed adapt it return the adapted event(s) for it. Other adapters which do not have anything to contribute during this adaptation simply return `EventSequence.Empty`. The adapted events are then delivered in-order to the `PersistentActor` during replay.


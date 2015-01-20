# What is Akka?

### Scalable real-time transaction processing

We believe that writing correct concurrent, fault-tolerant and scalable applications is too hard. Most of the time it's because we are using the wrong tools and the wrong level of abstraction. Akka is here to change that. Using the Actor Model we raise the abstraction level and provide a better platform to build scalable, resilient and responsive applications—see the Reactive Manifesto for more details. For fault-tolerance we adopt the "let it crash" model which the telecom industry has used with great success to build applications that self-heal and systems that never stop. Actors also provide the abstraction for transparent distribution and the basis for truly scalable and fault-tolerant applications.

Akka is Open Source and available under the Apache 2 License.

Download from https://github.com/akkadotnet/akka.net.

Please note that all code samples compile, so if you want direct access to the sources, have a look over at the Akka Docs subproject on github: for Java and Scala.

## Akka implements a unique hybrid
### Actors
Actors give you:

* Simple and high-level abstractions for concurrency and parallelism.
* Asynchronous, non-blocking and highly performant event-driven programming model.
* Very lightweight event-driven processes (several million actors per GB of heap memory).
See the chapter for C# or F#.

### Fault Tolerance
* Supervisor hierarchies with "let-it-crash" semantics.
* Supervisor hierarchies can span over multiple JVMs to provide truly fault-tolerant systems.
* Excellent for writing highly fault-tolerant systems that self-heal and never stop.
See Fault Tolerance (Scala) and Fault Tolerance (Java).

### Location Transparency
Everything in Akka is designed to work in a distributed environment: all interactions of actors use pure message passing and everything is asynchronous.

For an overview of the cluster support see the Java and Scala documentation chapters.

### Persistence
Messages received by an actor can optionally be persisted and replayed when the actor is started or restarted. This allows actors to recover their state, even after JVM crashes or when being migrated to another node.

You can find more details in the respective chapter for Java or Scala.

## C# and F# APIs
Akka has both a C# Documentation and a F# Documentation.

## Akka can be used in two different ways
TODO

## Commercial Support
TODO
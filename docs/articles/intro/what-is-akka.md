---
layout: docs.hbs
title: What is Akka
---
# What is Akka?

## Scalable, distributed real-time transaction processing

We believe that writing correct, concurrent, fault-tolerant and scalable applications is too hard.

Most of the time, that's because we are using the wrong tools and the wrong level of abstraction. Akka is here to change that.

By using the Actor Model, we raise the abstraction level and provide a better platform to build scalable, resilient and responsive applications—see the [Reactive Manifesto](http://www.reactivemanifesto.org/) for more details.

For fault-tolerance we adopt the "let it crash" model, which the telecom industry has used with great success to build applications that self-heal and systems that never stop. Actors also provide the abstraction for transparent distribution and the basis for truly scalable and fault-tolerant applications.

Akka.NET is Open Source and available under the [Apache 2 License](http://www.apache.org/licenses/LICENSE-2.0) -- [explained in plain english here](https://www.tldrlegal.com/l/apache2).

Download from https://github.com/akkadotnet/akka.net.

## A unique hybrid
### Actors
Actors give you:

* Simple and high-level abstractions for concurrency and parallelism.
* Asynchronous, non-blocking and highly performant event-driven programming model.
* Very lightweight event-driven processes (several million actors per GB of heap memory).
See the chapter for C# or F#.

### Fault Tolerance
* Supervisor hierarchies with "let it crash" semantics.
* Supervisor hierarchies can span over multiple virtual machines to provide truly fault-tolerant systems.
* Excellent for writing highly fault-tolerant systems that self-heal and never stop.
See [Fault Tolerance](xref:fault-tolerance).

### Location Transparency
Everything in Akka is designed to work in a distributed environment: all interactions of actors use pure message passing and everything is asynchronous.

### Persistence
State changes experienced by an actor can optionally be persisted and replayed when the actor is started or restarted. This allows actors to recover their state, even after JVM crashes or when being migrated to another node.

## Commercial Support
Akka.NET is supported by [Petabridge, a company making distributed, realtime computing easy for .NET developers](http://petabridge.com).

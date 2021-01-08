---
uid: streams-quickstart
title: Quickstart
---

# Streams Quickstart Guide

To use Akka Streams, an additional module is required, so we first make sure ```Akka.Streams``` is added to our project:

```
Install-Package Akka.Streams
```

A stream usually begins at a source, so this is also how we start an Akka Stream. Before we create one, we import the full complement of streaming tools:

```csharp
using Akka.Streams;
using Akka.Streams.Dsl;
```
Now we will start with a rather simple source, emitting the integers 1 to 100;

```csharp
Source<T, NotUsed> source = Source.From(Enumerable.Range(1,100))
```
The `Source` type is parametrized with two types: the first one is the type of element that this source emits and the second one may signal that running the source produces some auxiliary value (e.g. a network source may provide information about the bound port or the peer's address). Where no auxiliary information is produced, the type `NotUsed` is used -- and a simple range of integers surely falls into this category.

Having created this source means that we have a description of how to emit the first 100 natural numbers, but this source is not yet active. In order to get those numbers out we have to run it:

```csharp
source.RunForeach(i => Console.WriteLine(i.ToString()), materializer)
```
This line will complement the source with a consumer function--in this example we simply print out the numbers to the console--and pass this little stream setup to an Actor that runs it. This activation is signaled by having "run" be part of the method name; there are other methods that run Akka Streams, and they all follow this pattern.

You may wonder where the Actor gets created that runs the stream, and you are probably also asking yourself what this materializer means. In order to get this value we first need to create an Actor system:
```csharp
using (var system = ActorSystem.Create("system"))
using (var materializer = system.Materializer())
```
There are other ways to create a materializer, e.g. from an `ActorContext` when using streams from within Actors. The `Materializer` is a factory for stream execution engines, it is the thing that makes streams run --you don't need to worry about any of the details just now apart from that you need one for calling any of the run methods on a `Source`. 

The nice thing about Akka Streams is that the `Source` is just a description of what you want to run, and like an architect's blueprint it can be reused, incorporated into a larger design. We may choose to transform the source of integers and write it to a file instead:
```csharp
  var factorials = source.Scan(new BigInteger(1), (acc, next) => acc * next);
  var result =
      factorials
          .Select(num => ByteString.FromString($"{num}\n"))
          .RunWith(FileIO.ToFile(new FileInfo("factorials.txt")), materializer);
```
First we use the `scan` combinator to run a computation over the whole stream: starting with the number 1 `(BigInteger(1))` we multiple by each of the incoming numbers, one after the other; the `scan` operation emits the initial value and then every calculation result. This yields the series of factorial numbers which we stash away as a Source for later reuse --it is important to keep in mind that nothing is actually computed yet, this is just a description of what we want to have computed once we run the stream. Then we convert the resulting series of numbers into a stream of `ByteString` objects describing lines in a text file. This stream is then run by attaching a file as the receiver of the data. In the terminology of Akka Streams this is called a `Sink. IOResult` is a type that IO operations return in Akka Streams in order to tell you how many bytes or elements were consumed and whether the stream terminated normally or exceptionally.

## Reusable Pieces
One of the nice parts of Akka Stream --and something that other stream libraries do not offer-- is that not only sources can be reused like blueprints, all other elements can be as well. We can take the file-writing `Sink`, prepend the processing steps necessary to get the `ByteString` elements from incoming string and package that up as a reusable piece as well. Since the language for writing these streams always flows from left to right (just like plain English), we need a starting point that is like a source but with an "open" input. In Akka Streams this is called a `Flow`:
```csharp
public static Sink<string, Task<IOResult>> LineSink(string filename) {
 return Flow.Create<string>()
   .Select(s => ByteString.FromString($"{s}\n"))
   .ToMaterialized(FileIO.ToFile(new FileInfo(filename)), Keep.Right);
}
```
Starting from a flow of strings we convert each to `ByteString` and then feed to the already known file-writing `Sink`. The resulting blueprint is a `Sink<string, Task<IOResult>>`, which means that it accepts strings as its input and when materialized it will create auxiliary information of type `Task<IOResult>` (when chaining operations on a `Source` or `Flow` the type of the auxiliary information --called the "materialized value"-- is given by the leftmost starting point; since we want to retain what the `FileIO.ToFile` sink has to offer, we need to say `Keep.Right`).

We can use the new and shiny Sink we just created by attaching it to our factorials source --after a small adaptation to turn the numbers into strings:
```csharp
factorials.Select(_ => _.ToString()).RunWith(LineSink("factorial2.txt"), materializer);
```

## Time-Based Processing
Before we start looking at a more involved example we explore the streaming nature of what Akka Streams can do. Starting from the `factorials` source we transform the stream by zipping it together with another stream, represented by a `Source` that emits the number 0 to 100: the first number emitted by the `factorials` source is the factorial of zero, the second is the factorial of one, and so on. We combine these two by forming strings like "3! = 6".

```csharp
 await factorials
	 .ZipWith(Source.From(Enumerable.Range(0, 100)), (num, idx) => $"{idx}! = {num}")
	 .Throttle(1, TimeSpan.FromSeconds(1), 1, ThrottleMode.Shaping)
	 .RunForeach(Console.WriteLine, materializer);
```
All operations so far have been time-independent and could have been performed in the same fashion on strict collections of elements. The next line demonstrates that we are in fact dealing with streams that can flow at a certain speed: we use the `throttle` combinator to slow down the stream to 1 element per second (the second 1 in the argument list is the maximum size of a burst that we want to allow--passing 1 means that the first element gets through immediately and the second then has to wait for one second and so on).

If you run this program you will see one line printed per second. One aspect that is not immediately visible deserves mention, though: if you try and set the streams to produce a billion numbers each then you will notice that your environment does not crash with an OutOfMemoryError, even though you will also notice that running the streams happens in the background, asynchronously (this is the reason for the auxiliary information to be provided as a `Task`). The secret that makes this work is that Akka Streams implicitly implement pervasive flow control, all combinators respect back-pressure. This allows the throttle combinator to signal to all its upstream sources of data that it can only accept elements at a certain rate--when the incoming rate is higher than one per second the throttle combinator will assert back-pressure upstream.

This is basically all there is to Akka Streams in a nutshell--glossing over the fact that there are dozens of sources and sinks and many more stream transformation combinators to choose from, see also [Overview of built-in stages and their semantics](xref:streams-builtin-stages).

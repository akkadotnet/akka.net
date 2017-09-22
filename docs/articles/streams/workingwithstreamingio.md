---
uid: streams-io
title: Working with streaming IO
---

# Working with streaming IO

## Streaming File IO

Akka Streams provide simple Sources and Sinks that can work with `ByteString` instances to perform IO operations on files.

Streaming data from a file is as easy as creating a `FileIO.FromFile` given a target file, and an optional
``chunkSize`` which determines the buffer size determined as one "element" in such stream:

```csharp
var file = new FileInfo("example.csv");
var result = FileIO.FromFile(file)
    .To(Sink.Ignore<ByteString>())
    .Run(materializer);
```

Please note that these processing stages are backed by Actors and by default are configured to run on a pre-configured
threadpool-backed dispatcher dedicated for File IO. This is very important as it isolates the blocking file IO operations from the rest
of the ActorSystem allowing each dispatcher to be utilised in the most efficient way. If you want to configure a custom
dispatcher for file IO operations globally, you can do so by changing the ``akka.stream.blocking-io-dispatcher``,
or for a specific stage by specifying a custom Dispatcher in code, like this:

```csharp
FileIO.FromFile(file)
    .WithAttributes(ActorAttributes.CreateDispatcher("custom-blocking-io-dispatcher"));
```

## Context

The generator imports nested schemas from referenced assemblies. Its current formatter registrations live on the serializer and are resolved when emitting that serializer. Stream reference implementations live inside Akka.Streams. They already expose the partner actor path to the Streams serializers and have constructors that take the partner actor reference.

## Goals / Non-Goals

The proof of concept exercises message defaults and serializer overrides on real typed stream references. It covers generated envelopes and reflection-free construction of their field values. Bare stream reference dispatch and native remoting startup are separate concerns.

## Decisions

### Declare the default on the message schema

A message can attach `AkkaMessageFormatter<TTarget, TFormatter>` to supply its default. Capture the declaration when extracting the schema so it is available when another compilation imports the message. Apply it to the declaring message's matching fields and collection positions. A nested message supplies its own defaults.

Apply the serializer's exact registration when it exists. That preserves the existing protocol's choice and makes the message usable by other serializers. A selected formatter owns its whole field position, so the generator can stop schema discovery at that position. Generated formatter instances use the target and formatter identities so two messages can use different codecs for the same target.

### Reuse the typed constructor inside Streams

Add `SourceRefFormatter<T>` and `SinkRefFormatter<T>` in Akka.Streams. Each uses the transport-aware actor path and resolves it through the receiving provider. The formatter instantiates the built-in ref through its closed constructor, which the native compiler can see from generated consumer code. Akka.Streams references the existing Serialization V2 runtime for the formatter contract and size helper.

## Risks / Trade-offs

An imported formatter must be accessible from the assembly that generates the serializer. Duplicate registrations within the same scope are ambiguous. Diagnose these when they affect generated code.

A formatter controls bytes as well as reconstruction. Moving an existing formatter to a message preserves bytes wherever it selects the same representation. A different representation requires wire evolution for each affected protocol.

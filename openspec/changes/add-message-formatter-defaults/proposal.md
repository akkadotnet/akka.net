## Why

A reusable message can require a custom field codec. Today every generated serializer that includes that message must register the codec itself, which makes a typed stream reference difficult to embed in a reusable schema.

## What Changes

- Allow a serializable message to declare a default formatter for an exact field type. An explicit registration on the serializer takes precedence.
- Support closed generic formatter targets and implementations for the typed stream reference use case in #8673.
- Provide source and sink reference formatters that encode the partner actor path and reconstruct a reference using the field's compile-time element type.
- Exercise precedence, schema reuse and real stream references through generated serializers, including reconstruction in an ordinary Native AOT consumer.

## Capabilities

### New Capabilities

- `message-formatter-defaults` defines formatter selection and typed stream reference reconstruction.

### Modified Capabilities

The active `messagepack-sourcegen-validation` change records how these defaults fit its existing formatter and imported schema requirements.

## Impact

The change adds a message attribute, extends formatter resolution and adds typed formatters to Akka.Streams. Existing serializer registrations continue to select their encoding. The typed field format carries the partner actor path. Bare stream references continue to use their registered serializers.

## Purpose

Let reusable generated message schemas supply field codecs and let each owning serializer choose its protocol representation.

## ADDED Requirements

### Requirement: A message can declare an exact type formatter default

A type annotated with `AkkaSerializable` SHALL be able to supply a formatter for a matching field type. Selection SHALL prefer an exact registration on the serializer, then an exact default on the declaring message, then ordinary generated handling. The chosen formatter SHALL handle the field's write, read and size operations.

For a nullable value type field, selection SHALL use the underlying type. The generator SHALL handle nil for nullable fields.

#### Scenario: A referenced message supplies its own codec
- **WHEN** a serializer embeds a message from another assembly whose matching field has a message-declared formatter
- **THEN** it SHALL use that formatter without an additional serializer registration

#### Scenario: An explicit serializer registration takes precedence
- **WHEN** the serializer and declaring message register formatters for the same target type
- **THEN** the generated read, write and size paths SHALL use the serializer's formatter

#### Scenario: Two nested schemas choose different codecs
- **WHEN** two embedded messages declare different default formatters for the same target
- **THEN** each SHALL use its own formatter when the serializer has no override

#### Scenario: An object field carries an envelope payload
- **WHEN** a field is declared as `object` or `object?`
- **THEN** the generator SHALL use the existing runtime serializer envelope

### Requirement: Formatters can handle closed generic stream references

Formatter declarations SHALL support exact closed generic reference types and closed generic formatter implementations. A generated message SHALL be able to encode a typed source or sink reference using its partner actor path.

#### Scenario: A closed generic stream reference is reconstructed
- **WHEN** a generated serializer reads a source or sink reference field using its typed stream reference formatter
- **THEN** the formatter SHALL resolve the actor path through the receiving system and construct a reference of the field's declared element type without dynamic type loading

#### Scenario: Distinct element types select their own formatter
- **WHEN** a serializer handles fields using different closed constructions of the same stream reference interface
- **THEN** it SHALL resolve the registration for each exact closed type

#### Scenario: A nullable typed stream reference is absent
- **WHEN** a nullable stream reference field has a null value
- **THEN** the generated serializer SHALL round trip the null value using MessagePack nil

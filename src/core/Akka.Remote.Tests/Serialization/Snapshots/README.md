# Actor-selection protocol fixtures

Legacy id 6 remains protobuf with an empty manifest. Native id 46 introduces the explicit
manifest `a`, with generated MessagePack fields 1 = wrapped payload, 2 = selection elements,
3 = WildCardFanOut. Each element has field 1 = kind (0 child name, 1 child pattern, 2 parent),
field 2 = matcher (null for parent). These field ids and discriminators are permanent.

The parity corpus covers every supported legacy path kind, empty paths, primitive/binary payloads,
and nested selection wrappers. Its WildCardFanOut is false because legacy omits that field.
A separate V2-only check proves true survives; unsupported recursive selectors are rejected
before output is written. No legacy serializer source or bytes change.

The nine legacy fixtures were captured and reviewed before the nine V2 fixtures, on Linux x64
with SDK 10.0.101. Legacy MessageContainerSerializer.cs remains the unchanged baseline blob
`b18753a96289782209d5c8127afe88a4410ffb89` from `dev` at `2d6a06797`. Each legacy dump records
its empty manifest; each V2 dump records `a`. Nested payloads keep their own serializer ids and
formats (the nested-selection snapshot uses legacy id 6 because that fixture system writes legacy).
Expected files must be reviewed, never generated or accepted automatically by a passing test.

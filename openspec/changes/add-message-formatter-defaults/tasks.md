## 1. Generator

- [x] 1.1 Add the message formatter attribute and closed generic formatter support. Verify an imported schema supplies its formatter and an exact serializer override wins.
- [x] 1.2 Verify generated serializers can use distinct codecs for the same target type and that existing serializer formatter fixtures still pass.

## 2. Typed stream references

- [x] 2.1 Add the Streams formatters and round trip typed source and sink references in a generated message. Transfer stream elements after reconstruction.
- [x] 2.2 Publish and run an ordinary Native AOT consumer with dynamic type loading disabled. Verify both closed reference types round trip.

## 3. Review and delivery

- [x] 3.1 Verify the OpenSpec changes, focused suites, API approvals and Slopwatch checks. Resolve independent review findings.
- [x] 3.2 Document the typed stream reference example and formatter precedence in the source-generated serialization guide.

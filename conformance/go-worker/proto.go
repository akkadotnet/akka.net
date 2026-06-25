package main

// Minimal hand-rolled protobuf (proto3) encoder/decoder, sufficient for the small set of
// Akka.NET remoting + cluster messages this worker needs. Avoids any codegen/toolchain dependency.

import (
	"encoding/binary"
	"errors"
	"fmt"
)

// ---- encoding ----

func appendVarint(b []byte, v uint64) []byte {
	for v >= 0x80 {
		b = append(b, byte(v)|0x80)
		v >>= 7
	}
	return append(b, byte(v))
}

func wireTag(field, wire int) uint64 { return uint64(field)<<3 | uint64(wire) }

// pbBytes encodes a length-delimited field (wire type 2): bytes, strings, embedded messages.
func pbBytes(field int, val []byte) []byte {
	b := appendVarint(nil, wireTag(field, 2))
	b = appendVarint(b, uint64(len(val)))
	return append(b, val...)
}

func pbString(field int, s string) []byte { return pbBytes(field, []byte(s)) }

// pbVarint encodes a varint field (wire type 0): int32/int64/uint32/uint64/bool/enum.
func pbVarint(field int, v uint64) []byte {
	b := appendVarint(nil, wireTag(field, 0))
	return appendVarint(b, v)
}

// pbFixed64 encodes a fixed64 field (wire type 1), little-endian.
func pbFixed64(field int, v uint64) []byte {
	b := appendVarint(nil, wireTag(field, 1))
	var x [8]byte
	binary.LittleEndian.PutUint64(x[:], v)
	return append(b, x[:]...)
}

// ---- decoding ----

type pbField struct {
	num  int
	wire int
	data []byte // wire type 2
	val  uint64 // wire types 0, 1, 5
}

func uvarint(b []byte) (uint64, int) {
	var v uint64
	var s uint
	for i := 0; i < len(b); i++ {
		c := b[i]
		if c < 0x80 {
			if i > 9 || (i == 9 && c > 1) {
				return 0, -1
			}
			return v | uint64(c)<<s, i + 1
		}
		v |= uint64(c&0x7f) << s
		s += 7
	}
	return 0, 0
}

func pbParse(b []byte) ([]pbField, error) {
	var out []pbField
	i := 0
	for i < len(b) {
		t, n := uvarint(b[i:])
		if n <= 0 {
			return nil, errors.New("protobuf: bad tag")
		}
		i += n
		field := int(t >> 3)
		wire := int(t & 7)
		switch wire {
		case 0:
			v, n := uvarint(b[i:])
			if n <= 0 {
				return nil, errors.New("protobuf: bad varint")
			}
			i += n
			out = append(out, pbField{field, 0, nil, v})
		case 1:
			if i+8 > len(b) {
				return nil, errors.New("protobuf: short fixed64")
			}
			out = append(out, pbField{field, 1, nil, binary.LittleEndian.Uint64(b[i:])})
			i += 8
		case 2:
			l, n := uvarint(b[i:])
			if n <= 0 {
				return nil, errors.New("protobuf: bad length")
			}
			i += n
			if i+int(l) > len(b) {
				return nil, errors.New("protobuf: short bytes")
			}
			out = append(out, pbField{field, 2, b[i : i+int(l)], 0})
			i += int(l)
		case 5:
			if i+4 > len(b) {
				return nil, errors.New("protobuf: short fixed32")
			}
			out = append(out, pbField{field, 5, nil, uint64(binary.LittleEndian.Uint32(b[i:]))})
			i += 4
		default:
			return nil, fmt.Errorf("protobuf: unsupported wire type %d", wire)
		}
	}
	return out, nil
}

// field returns the first field with the given number, or nil.
func pbGet(fields []pbField, num int) *pbField {
	for i := range fields {
		if fields[i].num == num {
			return &fields[i]
		}
	}
	return nil
}

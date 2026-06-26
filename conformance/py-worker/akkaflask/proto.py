"""Minimal hand-rolled protobuf (proto3) encode/decode for the Akka.NET remoting + cluster messages
this client needs. No codegen, no dependencies."""

import struct


def varint(v):
    out = bytearray()
    v = int(v)
    while v >= 0x80:
        out.append((v & 0x7F) | 0x80)
        v >>= 7
    out.append(v & 0x7F)
    return bytes(out)


def _tag(field, wire):
    return varint((field << 3) | wire)


def pb_bytes(field, val):
    """Length-delimited field (wire type 2): bytes, strings, embedded messages."""
    return _tag(field, 2) + varint(len(val)) + val


def pb_string(field, s):
    return pb_bytes(field, s.encode("utf-8"))


def pb_varint(field, v):
    return _tag(field, 0) + varint(v)


def pb_fixed64(field, v):
    return _tag(field, 1) + struct.pack("<Q", v & 0xFFFFFFFFFFFFFFFF)


def read_varint(buf, pos):
    shift = 0
    result = 0
    while True:
        b = buf[pos]
        pos += 1
        result |= (b & 0x7F) << shift
        if not (b & 0x80):
            return result, pos
        shift += 7


def pb_parse(buf):
    """Parse a message into a list of fields: {num, wire, data} for wire 2, {num, wire, val} otherwise.
    Repeated fields appear multiple times, in order."""
    fields = []
    pos = 0
    n = len(buf)
    while pos < n:
        tag, pos = read_varint(buf, pos)
        num = tag >> 3
        wire = tag & 7
        if wire == 0:
            val, pos = read_varint(buf, pos)
            fields.append({"num": num, "wire": 0, "val": val})
        elif wire == 1:
            val = struct.unpack_from("<Q", buf, pos)[0]
            pos += 8
            fields.append({"num": num, "wire": 1, "val": val})
        elif wire == 2:
            ln, pos = read_varint(buf, pos)
            data = bytes(buf[pos:pos + ln])
            pos += ln
            fields.append({"num": num, "wire": 2, "data": data})
        elif wire == 5:
            val = struct.unpack_from("<I", buf, pos)[0]
            pos += 4
            fields.append({"num": num, "wire": 5, "val": val})
        else:
            raise ValueError("protobuf: unsupported wire type %d" % wire)
    return fields


def pb_get(fields, num):
    for f in fields:
        if f["num"] == num:
            return f
    return None


def parse_packed_varints(buf):
    out = []
    pos = 0
    while pos < len(buf):
        v, pos = read_varint(buf, pos)
        out.append(v)
    return out


def pack_varints(vals):
    return b"".join(varint(v) for v in vals)

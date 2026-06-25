'use strict';
// Minimal hand-rolled protobuf (proto3) encoder/decoder for the small set of Akka.NET remoting +
// cluster messages this worker needs. No codegen, no dependencies. Varints use BigInt internally so
// 64-bit fields are exact; callers convert small fields back to Number.

function varintBytes(v) {
  let n = typeof v === 'bigint' ? v : BigInt(v);
  const out = [];
  while (n >= 0x80n) {
    out.push(Number((n & 0x7fn) | 0x80n));
    n >>= 7n;
  }
  out.push(Number(n & 0x7fn));
  return Buffer.from(out);
}

function tag(field, wire) {
  return varintBytes((field << 3) | wire);
}

// length-delimited (wire type 2): bytes, strings, embedded messages
function pbBytes(field, val) {
  return Buffer.concat([tag(field, 2), varintBytes(val.length), val]);
}

function pbString(field, s) {
  return pbBytes(field, Buffer.from(s, 'utf8'));
}

// varint (wire type 0)
function pbVarint(field, v) {
  return Buffer.concat([tag(field, 0), varintBytes(v)]);
}

// fixed64 (wire type 1), little-endian
function pbFixed64(field, v) {
  const b = Buffer.alloc(8);
  b.writeBigUInt64LE(BigInt(v));
  return Buffer.concat([tag(field, 1), b]);
}

function readVarint(buf, pos) {
  let shift = 0n;
  let result = 0n;
  for (;;) {
    const b = buf[pos++];
    result |= BigInt(b & 0x7f) << shift;
    if ((b & 0x80) === 0) break;
    shift += 7n;
  }
  return [result, pos];
}

// Parse a message into a flat list of { num, wire, data?, val? }. Repeated fields appear multiple times.
function pbParse(buf) {
  const fields = [];
  let pos = 0;
  while (pos < buf.length) {
    let t;
    [t, pos] = readVarint(buf, pos);
    const num = Number(t >> 3n);
    const wire = Number(t & 7n);
    if (wire === 0) {
      let v;
      [v, pos] = readVarint(buf, pos);
      fields.push({ num, wire, val: v });
    } else if (wire === 1) {
      const v = buf.readBigUInt64LE(pos);
      pos += 8;
      fields.push({ num, wire, val: v });
    } else if (wire === 2) {
      let len;
      [len, pos] = readVarint(buf, pos);
      const l = Number(len);
      const data = buf.subarray(pos, pos + l);
      pos += l;
      fields.push({ num, wire, data });
    } else if (wire === 5) {
      const v = BigInt(buf.readUInt32LE(pos));
      pos += 4;
      fields.push({ num, wire, val: v });
    } else {
      throw new Error('protobuf: unsupported wire type ' + wire);
    }
  }
  return fields;
}

function pbGet(fields, num) {
  return fields.find((f) => f.num === num);
}

function parsePackedVarints(buf) {
  const out = [];
  let pos = 0;
  while (pos < buf.length) {
    let v;
    [v, pos] = readVarint(buf, pos);
    out.push(Number(v));
  }
  return out;
}

function packVarints(vals) {
  return Buffer.concat(vals.map((v) => varintBytes(v)));
}

module.exports = {
  pbBytes, pbString, pbVarint, pbFixed64, pbParse, pbGet,
  parsePackedVarints, packVarints, varintBytes,
};

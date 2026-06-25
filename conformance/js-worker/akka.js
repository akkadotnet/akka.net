'use strict';
// Akka.NET remoting + cluster wire format: framing, the Akka protocol PDUs (ASSOCIATE / HEARTBEAT /
// PAYLOAD), the remote envelope, ActorSelection unwrapping, and cluster-message (de)serialization.

const P = require('./proto');

// ---- Akka protocol command types (WireFormats.proto CommandType) ----
const CMD = { NONE: 0, ASSOCIATE: 1, DISASSOCIATE: 2, HEARTBEAT: 3, DISASSOCIATE_SHUTTING_DOWN: 4, DISASSOCIATE_QUARANTINED: 5 };

// Serializer identifiers.
const CLUSTER_SERIALIZER_ID = 5;            // ClusterMessageSerializer
const MESSAGE_CONTAINER_SERIALIZER_ID = 6;  // MessageContainerSerializer (wraps ActorSelection messages)

// Cluster message string manifests (ClusterMessageSerializer).
const MANIFEST = {
  InitJoin: 'Akka.Cluster.InternalClusterAction+InitJoin, Akka.Cluster',
  InitJoinAck: 'Akka.Cluster.InternalClusterAction+InitJoinAck, Akka.Cluster',
  InitJoinNack: 'Akka.Cluster.InternalClusterAction+InitJoinNack, Akka.Cluster',
  Join: 'Akka.Cluster.InternalClusterAction+Join, Akka.Cluster',
  Welcome: 'Akka.Cluster.InternalClusterAction+Welcome, Akka.Cluster',
  Leave: 'Akka.Cluster.ClusterUserAction+Leave, Akka.Cluster',
  ExitingConfirmed: 'Akka.Cluster.InternalClusterAction+ExitingConfirmed, Akka.Cluster',
  GossipEnvelope: 'Akka.Cluster.GossipEnvelope, Akka.Cluster',
  GossipStatus: 'Akka.Cluster.GossipStatus, Akka.Cluster',
  Heartbeat: 'HB',
  HeartbeatRsp: 'HBR',
};

// Member statuses (ClusterMessages.proto Member.MemberStatus).
const STATUS = { Joining: 0, Up: 1, Leaving: 2, Exiting: 3, Down: 4, Removed: 5, WeaklyUp: 6 };
function statusName(s) {
  return Object.keys(STATUS).find((k) => STATUS[k] === s) || ('status(' + s + ')');
}

// ---- Address ----

function parseAddress(s) {
  const i = s.indexOf('://');
  const protocol = s.slice(0, i);
  let rest = s.slice(i + 3);
  const at = rest.indexOf('@');
  const system = rest.slice(0, at);
  let hp = rest.slice(at + 1);
  const sl = hp.indexOf('/');
  if (sl >= 0) hp = hp.slice(0, sl);
  const lc = hp.lastIndexOf(':');
  return { protocol, system, host: hp.slice(0, lc), port: parseInt(hp.slice(lc + 1), 10) };
}

function addrString(a) {
  return `${a.protocol}://${a.system}@${a.host}:${a.port}`;
}
function actorPath(a, path) {
  return addrString(a) + path;
}
// AddressData{ system=1, hostname=2, port=3, protocol=4 }
function addressData(a) {
  return Buffer.concat([
    P.pbString(1, a.system),
    P.pbString(2, a.host),
    P.pbVarint(3, a.port),
    P.pbString(4, a.protocol),
  ]);
}
// UniqueAddress{ address=1 (AddressData), uid=2 (uint32) }
function uniqueAddress(a, uid) {
  return Buffer.concat([P.pbBytes(1, addressData(a)), P.pbVarint(2, uid)]);
}

// ---- Framing: 4-byte little-endian length prefix (payload only) ----

function frame(payload) {
  const hdr = Buffer.alloc(4);
  hdr.writeUInt32LE(payload.length);
  return Buffer.concat([hdr, payload]);
}

// FrameReader accumulates socket bytes and invokes onFrame(payload) per complete frame.
class FrameReader {
  constructor(onFrame) {
    this.buf = Buffer.alloc(0);
    this.onFrame = onFrame;
  }
  push(chunk) {
    this.buf = Buffer.concat([this.buf, chunk]);
    for (;;) {
      if (this.buf.length < 4) return;
      const len = this.buf.readUInt32LE(0);
      if (this.buf.length < 4 + len) return;
      const payload = this.buf.subarray(4, 4 + len);
      this.buf = this.buf.subarray(4 + len);
      this.onFrame(payload);
    }
  }
}

// ---- Akka protocol PDUs ----

// AkkaProtocolMessage{ instruction: AkkaControlMessage{ commandType=1, handshakeInfo=2 } }
function constructAssociate(origin, uid) {
  const hs = Buffer.concat([P.pbBytes(1, addressData(origin)), P.pbFixed64(2, uid)]); // AkkaHandshakeInfo
  const ctrl = Buffer.concat([P.pbVarint(1, CMD.ASSOCIATE), P.pbBytes(2, hs)]);
  return P.pbBytes(2, ctrl); // instruction
}
function constructHeartbeat() {
  return P.pbBytes(2, P.pbVarint(1, CMD.HEARTBEAT));
}
function constructPayload(inner) {
  return P.pbBytes(1, inner); // AkkaProtocolMessage.payload
}

// ---- Remote envelope ----

const SEQ_UNDEFINED = 0xffffffffffffffffn; // ulong.MaxValue: unacked delivery

// AckAndEnvelopeContainer{ envelope: RemoteEnvelope{ recipient=1, message=2 (Payload), sender=4, seq=5 } }
function constructMessage(recipientPath, senderPath, serializerId, manifest, message) {
  const payload = Buffer.concat([
    P.pbBytes(1, message),
    P.pbVarint(2, serializerId),
    P.pbBytes(3, Buffer.from(manifest, 'utf8')),
  ]);
  const env = Buffer.concat([
    P.pbBytes(1, P.pbString(1, recipientPath)), // recipient ActorRefData{ path=1 }
    P.pbBytes(2, payload),                      // message Payload
    P.pbBytes(4, P.pbString(1, senderPath)),    // sender ActorRefData
    P.pbFixed64(5, SEQ_UNDEFINED),
  ]);
  const container = P.pbBytes(2, env);
  return constructPayload(container);
}

// ---- Cluster message bodies ----

// Join{ node=1 (UniqueAddress), roles=2 (repeated string), appVersion=3 (string) }
function constructJoin(node, uid, roles, appVersion) {
  const parts = [P.pbBytes(1, uniqueAddress(node, uid))];
  for (const r of roles) parts.push(P.pbString(2, r));
  parts.push(P.pbString(3, appVersion));
  return Buffer.concat(parts);
}

// HeartBeatResponse{ from=1 (UniqueAddress), sequenceNr=2 (int64), creationTime=3 (int64) }
function buildHeartbeatRsp(fromUA, seq, creationTime) {
  return Buffer.concat([P.pbBytes(1, fromUA), P.pbVarint(2, seq), P.pbVarint(3, creationTime)]);
}

// GossipEnvelope{ from=1, to=2 (UniqueAddress), serializedGossip=3 (bytes) }
function buildGossipEnvelope(fromUA, toUA, gossip) {
  return Buffer.concat([P.pbBytes(1, fromUA), P.pbBytes(2, toUA), P.pbBytes(3, gossip)]);
}

// ---- Inbound parsing ----

function parseAddressData(b) {
  const f = P.pbParse(b);
  const a = { protocol: '', system: '', host: '', port: 0 };
  let x;
  if ((x = P.pbGet(f, 1))) a.system = x.data.toString('utf8');
  if ((x = P.pbGet(f, 2))) a.host = x.data.toString('utf8');
  if ((x = P.pbGet(f, 3))) a.port = Number(x.val);
  if ((x = P.pbGet(f, 4))) a.protocol = x.data.toString('utf8');
  return a;
}

function parseActorRef(b) {
  const f = P.pbParse(b);
  const x = P.pbGet(f, 1);
  return x ? x.data.toString('utf8') : '';
}

// Parse an AkkaProtocolMessage into a control PDU or a payload (with envelope fields).
function parsePdu(buf) {
  const fields = P.pbParse(buf);
  const instr = P.pbGet(fields, 2);
  if (instr && instr.wire === 2) {
    const cf = P.pbParse(instr.data);
    const pdu = { control: true, commandType: 0, origin: null, uid: 0n };
    const ct = P.pbGet(cf, 1);
    if (ct) pdu.commandType = Number(ct.val);
    const hs = P.pbGet(cf, 2);
    if (hs && hs.wire === 2) {
      const hf = P.pbParse(hs.data);
      const o = P.pbGet(hf, 1);
      if (o && o.wire === 2) pdu.origin = parseAddressData(o.data);
      const u = P.pbGet(hf, 2);
      if (u) pdu.uid = u.val;
    }
    return pdu;
  }
  const pl = P.pbGet(fields, 1);
  const pdu = { control: false, recipientPath: '', senderPath: '', serializerId: 0, manifest: '', message: Buffer.alloc(0) };
  if (pl && pl.wire === 2) {
    const cf = P.pbParse(pl.data); // AckAndEnvelopeContainer
    const env = P.pbGet(cf, 2);
    if (env && env.wire === 2) {
      const ef = P.pbParse(env.data);
      let x;
      if ((x = P.pbGet(ef, 1))) pdu.recipientPath = parseActorRef(x.data);
      if ((x = P.pbGet(ef, 4))) pdu.senderPath = parseActorRef(x.data);
      const m = P.pbGet(ef, 2);
      if (m && m.wire === 2) {
        const mf = P.pbParse(m.data); // Payload
        if ((x = P.pbGet(mf, 1))) pdu.message = x.data;
        if ((x = P.pbGet(mf, 2))) pdu.serializerId = Number(x.val);
        if ((x = P.pbGet(mf, 3))) pdu.manifest = x.data.toString('utf8');
      }
    }
  }
  return pdu;
}

// SelectionEnvelope{ payload=1 (Payload), pattern=2 (repeated Selection{ type=1, matcher=2 }) }
function parseSelectionEnvelope(buf) {
  const f = P.pbParse(buf);
  const out = { serializerId: 0, manifest: '', message: Buffer.alloc(0), path: [] };
  const p = P.pbGet(f, 1);
  if (p && p.wire === 2) {
    const pf = P.pbParse(p.data);
    let x;
    if ((x = P.pbGet(pf, 1))) out.message = x.data;
    if ((x = P.pbGet(pf, 2))) out.serializerId = Number(x.val);
    if ((x = P.pbGet(pf, 3))) out.manifest = x.data.toString('utf8');
  }
  for (const fld of f) {
    if (fld.num === 2 && fld.wire === 2) {
      const sf = P.pbParse(fld.data);
      const mt = P.pbGet(sf, 2);
      if (mt) out.path.push(mt.data.toString('utf8'));
    }
  }
  return out;
}

function parseWelcome(b) {
  const f = P.pbParse(b);
  const from = P.pbGet(f, 1);
  const g = P.pbGet(f, 2);
  return { fromUA: from ? from.data : null, gossip: g ? g.data : Buffer.alloc(0) };
}

function parseGossipEnvelope(b) {
  const f = P.pbParse(b);
  const from = P.pbGet(f, 1);
  const g = P.pbGet(f, 3);
  return { fromUA: from ? from.data : null, gossip: g ? g.data : Buffer.alloc(0) };
}

// Heartbeat{ from=1 (AddressData), sequenceNr=2 (int64), creationTime=3 (sint64) }
function zigzagDecode(n) {
  return (n >> 1n) ^ -(n & 1n);
}
function parseHeartbeat(b) {
  const f = P.pbParse(b);
  let seq = 0n, ct = 0n, x;
  if ((x = P.pbGet(f, 2))) seq = x.val;
  if ((x = P.pbGet(f, 3))) ct = zigzagDecode(x.val);
  return { seq, creationTime: ct };
}

// ---- Gossip surgery: find our index and mark ourselves "seen" ----

function reemit(f) {
  if (f.wire === 0) return P.pbVarint(f.num, f.val);
  if (f.wire === 1) return P.pbFixed64(f.num, f.val);
  if (f.wire === 2) return P.pbBytes(f.num, f.data);
  if (f.wire === 5) {
    const b = Buffer.alloc(4);
    b.writeUInt32LE(Number(f.val));
    return Buffer.concat([P.varintBytes((f.num << 3) | 5), b]);
  }
  return Buffer.alloc(0);
}

// Index of (host, port, uid) within the gossip's allAddresses (field 1, repeated UniqueAddress).
function gossipAddressIndex(gossip, host, port, uid) {
  const f = P.pbParse(gossip);
  let idx = 0;
  for (const fld of f) {
    if (fld.num !== 1 || fld.wire !== 2) continue;
    const ua = P.pbParse(fld.data);
    let uaUid = 0, uaHost = '', uaPort = 0, x;
    if ((x = P.pbGet(ua, 2))) uaUid = Number(x.val);
    if ((x = P.pbGet(ua, 1))) {
      const ad = P.pbParse(x.data);
      let y;
      if ((y = P.pbGet(ad, 2))) uaHost = y.data.toString('utf8');
      if ((y = P.pbGet(ad, 3))) uaPort = Number(y.val);
    }
    if (uaHost === host && uaPort === port && uaUid === uid) return idx;
    idx++;
  }
  return -1;
}

// Status of the member with the given allAddresses index (Member{ addressIndex=1, status=3 }).
function gossipMemberStatus(gossip, addressIndex) {
  const f = P.pbParse(gossip);
  for (const fld of f) {
    if (fld.num !== 4 || fld.wire !== 2) continue;
    const mf = P.pbParse(fld.data);
    let ai = -1, st = 0, x;
    if ((x = P.pbGet(mf, 1))) ai = Number(x.val);
    if ((x = P.pbGet(mf, 3))) st = Number(x.val);
    if (ai === addressIndex) return st;
  }
  return null;
}

// Return gossip bytes with workerIndex added to overview.seen, leaving everything else (incl. the
// vector clock) byte-identical so the seed treats it as the same version and unions the seen sets.
function patchGossipSeen(gossip, workerIndex) {
  const f = P.pbParse(gossip);
  const parts = [];
  let overviewDone = false;
  for (const fld of f) {
    if (fld.num === 5 && fld.wire === 2) {
      parts.push(P.pbBytes(5, patchOverviewSeen(fld.data, workerIndex)));
      overviewDone = true;
    } else {
      parts.push(reemit(fld));
    }
  }
  if (!overviewDone) parts.push(P.pbBytes(5, P.pbBytes(1, P.packVarints([workerIndex]))));
  return Buffer.concat(parts);
}

function patchOverviewSeen(ov, workerIndex) {
  const f = P.pbParse(ov);
  const seen = new Set();
  const order = [];
  const others = [];
  for (const fld of f) {
    if (fld.num === 1) {
      const vals = fld.wire === 2 ? P.parsePackedVarints(fld.data) : [Number(fld.val)];
      for (const v of vals) if (!seen.has(v)) { seen.add(v); order.push(v); }
    } else {
      others.push(reemit(fld));
    }
  }
  if (!seen.has(workerIndex)) order.push(workerIndex);
  return Buffer.concat([P.pbBytes(1, P.packVarints(order)), ...others]);
}

module.exports = {
  CMD, CLUSTER_SERIALIZER_ID, MESSAGE_CONTAINER_SERIALIZER_ID, MANIFEST, STATUS, statusName,
  parseAddress, addrString, actorPath, addressData, uniqueAddress,
  frame, FrameReader,
  constructAssociate, constructHeartbeat, constructMessage, constructJoin,
  buildHeartbeatRsp, buildGossipEnvelope,
  parsePdu, parseSelectionEnvelope, parseWelcome, parseGossipEnvelope, parseHeartbeat,
  gossipAddressIndex, gossipMemberStatus, patchGossipSeen,
};

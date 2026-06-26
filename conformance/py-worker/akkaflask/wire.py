"""Akka.NET remoting + cluster wire format: framing, the Akka protocol PDUs (ASSOCIATE / HEARTBEAT /
PAYLOAD), the remote envelope, ActorSelection unwrapping, and cluster-message (de)serialization."""

import struct

from . import proto as P

# Akka protocol command types (WireFormats.proto CommandType).
CMD_NONE = 0
CMD_ASSOCIATE = 1
CMD_DISASSOCIATE = 2
CMD_HEARTBEAT = 3

# Serializer identifiers.
CLUSTER_SERIALIZER_ID = 5            # ClusterMessageSerializer
MESSAGE_CONTAINER_SERIALIZER_ID = 6  # MessageContainerSerializer (wraps ActorSelection messages)

# Cluster message string manifests (ClusterMessageSerializer).
M_INIT_JOIN = "Akka.Cluster.InternalClusterAction+InitJoin, Akka.Cluster"
M_INIT_JOIN_ACK = "Akka.Cluster.InternalClusterAction+InitJoinAck, Akka.Cluster"
M_JOIN = "Akka.Cluster.InternalClusterAction+Join, Akka.Cluster"
M_WELCOME = "Akka.Cluster.InternalClusterAction+Welcome, Akka.Cluster"
M_LEAVE = "Akka.Cluster.ClusterUserAction+Leave, Akka.Cluster"
M_EXITING_CONFIRMED = "Akka.Cluster.InternalClusterAction+ExitingConfirmed, Akka.Cluster"
M_GOSSIP_ENVELOPE = "Akka.Cluster.GossipEnvelope, Akka.Cluster"
M_HEARTBEAT = "HB"
M_HEARTBEAT_RSP = "HBR"
M_HEARTBEAT_LEGACY = "Akka.Cluster.ClusterHeartbeatSender+Heartbeat, Akka.Cluster"

# Member statuses (ClusterMessages.proto Member.MemberStatus).
S_JOINING, S_UP, S_LEAVING, S_EXITING, S_DOWN, S_REMOVED, S_WEAKLY_UP = range(7)
_STATUS_NAMES = ["Joining", "Up", "Leaving", "Exiting", "Down", "Removed", "WeaklyUp"]


def status_name(s):
    return _STATUS_NAMES[s] if 0 <= s < len(_STATUS_NAMES) else "status(%d)" % s


class Address:
    __slots__ = ("protocol", "system", "host", "port")

    def __init__(self, protocol, system, host, port):
        self.protocol = protocol
        self.system = system
        self.host = host
        self.port = port

    def __str__(self):
        return "%s://%s@%s:%d" % (self.protocol, self.system, self.host, self.port)

    def path(self, p):
        return str(self) + p


def parse_address(s):
    i = s.index("://")
    protocol = s[:i]
    rest = s[i + 3:]
    at = rest.index("@")
    system = rest[:at]
    hp = rest[at + 1:]
    sl = hp.find("/")
    if sl >= 0:
        hp = hp[:sl]
    host, _, port = hp.rpartition(":")
    return Address(protocol, system, host, int(port))


# AddressData{ system=1, hostname=2, port=3, protocol=4 }
def address_data(a):
    return (P.pb_string(1, a.system) + P.pb_string(2, a.host)
            + P.pb_varint(3, a.port) + P.pb_string(4, a.protocol))


# UniqueAddress{ address=1 (AddressData), uid=2 (uint32) }
def unique_address(a, uid):
    return P.pb_bytes(1, address_data(a)) + P.pb_varint(2, uid)


# ---- Framing: 4-byte little-endian length prefix (payload only) ----

def frame(payload):
    return struct.pack("<I", len(payload)) + payload


def recv_exact(sock, n):
    buf = bytearray()
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("connection closed")
        buf += chunk
    return bytes(buf)


def read_frame(sock):
    hdr = recv_exact(sock, 4)
    n = struct.unpack("<I", hdr)[0]
    return recv_exact(sock, n) if n else b""


# ---- Akka protocol PDUs ----

def construct_associate(origin, uid):
    hs = P.pb_bytes(1, address_data(origin)) + P.pb_fixed64(2, uid)  # AkkaHandshakeInfo
    ctrl = P.pb_varint(1, CMD_ASSOCIATE) + P.pb_bytes(2, hs)         # AkkaControlMessage
    return P.pb_bytes(2, ctrl)                                       # AkkaProtocolMessage.instruction


def construct_heartbeat():
    return P.pb_bytes(2, P.pb_varint(1, CMD_HEARTBEAT))


def construct_payload(inner):
    return P.pb_bytes(1, inner)  # AkkaProtocolMessage.payload


_SEQ_UNDEFINED = 0xFFFFFFFFFFFFFFFF  # ulong.MaxValue: unacked delivery


# AckAndEnvelopeContainer{ envelope: RemoteEnvelope{ recipient=1, message=2 (Payload), sender=4, seq=5 } }
def construct_message(recipient_path, sender_path, serializer_id, manifest, message):
    payload = (P.pb_bytes(1, message) + P.pb_varint(2, serializer_id)
               + P.pb_bytes(3, manifest.encode("utf-8")))
    env = (P.pb_bytes(1, P.pb_string(1, recipient_path))   # recipient ActorRefData{ path=1 }
           + P.pb_bytes(2, payload)                        # message Payload
           + P.pb_bytes(4, P.pb_string(1, sender_path))    # sender ActorRefData
           + P.pb_fixed64(5, _SEQ_UNDEFINED))
    return construct_payload(P.pb_bytes(2, env))            # AckAndEnvelopeContainer.envelope=2


# ---- Cluster message bodies ----

def construct_join(node, uid, roles, app_version):
    out = P.pb_bytes(1, unique_address(node, uid))
    for r in roles:
        out += P.pb_string(2, r)
    out += P.pb_string(3, app_version)
    return out


# HeartBeatResponse{ from=1 (UniqueAddress), sequenceNr=2 (int64), creationTime=3 (int64) }
def build_heartbeat_rsp(from_ua, seq, creation_time):
    return P.pb_bytes(1, from_ua) + P.pb_varint(2, seq) + P.pb_varint(3, creation_time)


# GossipEnvelope{ from=1, to=2 (UniqueAddress), serializedGossip=3 (bytes) }
def build_gossip_envelope(from_ua, to_ua, gossip):
    return P.pb_bytes(1, from_ua) + P.pb_bytes(2, to_ua) + P.pb_bytes(3, gossip)


# ---- Inbound parsing ----

class Pdu:
    __slots__ = ("control", "command_type", "origin", "uid",
                 "recipient_path", "sender_path", "serializer_id", "manifest", "message")


def _parse_address_data(b):
    f = P.pb_parse(b)
    a = Address("", "", "", 0)
    x = P.pb_get(f, 1)
    if x:
        a.system = x["data"].decode("utf-8")
    x = P.pb_get(f, 2)
    if x:
        a.host = x["data"].decode("utf-8")
    x = P.pb_get(f, 3)
    if x:
        a.port = x["val"]
    x = P.pb_get(f, 4)
    if x:
        a.protocol = x["data"].decode("utf-8")
    return a


def _parse_actor_ref(b):
    x = P.pb_get(P.pb_parse(b), 1)
    return x["data"].decode("utf-8") if x else ""


def parse_pdu(buf):
    fields = P.pb_parse(buf)
    pdu = Pdu()
    pdu.control = False
    pdu.command_type = 0
    pdu.origin = None
    pdu.uid = 0
    pdu.recipient_path = ""
    pdu.sender_path = ""
    pdu.serializer_id = 0
    pdu.manifest = ""
    pdu.message = b""

    instr = P.pb_get(fields, 2)
    if instr and instr["wire"] == 2:
        pdu.control = True
        cf = P.pb_parse(instr["data"])
        ct = P.pb_get(cf, 1)
        if ct:
            pdu.command_type = ct["val"]
        hs = P.pb_get(cf, 2)
        if hs and hs["wire"] == 2:
            hf = P.pb_parse(hs["data"])
            o = P.pb_get(hf, 1)
            if o and o["wire"] == 2:
                pdu.origin = _parse_address_data(o["data"])
            u = P.pb_get(hf, 2)
            if u:
                pdu.uid = u["val"]
        return pdu

    pl = P.pb_get(fields, 1)
    if pl and pl["wire"] == 2:
        cf = P.pb_parse(pl["data"])  # AckAndEnvelopeContainer
        env = P.pb_get(cf, 2)
        if env and env["wire"] == 2:
            ef = P.pb_parse(env["data"])
            x = P.pb_get(ef, 1)
            if x:
                pdu.recipient_path = _parse_actor_ref(x["data"])
            x = P.pb_get(ef, 4)
            if x:
                pdu.sender_path = _parse_actor_ref(x["data"])
            m = P.pb_get(ef, 2)
            if m and m["wire"] == 2:
                mf = P.pb_parse(m["data"])  # Payload
                x = P.pb_get(mf, 1)
                if x:
                    pdu.message = x["data"]
                x = P.pb_get(mf, 2)
                if x:
                    pdu.serializer_id = x["val"]
                x = P.pb_get(mf, 3)
                if x:
                    pdu.manifest = x["data"].decode("utf-8")
    return pdu


# SelectionEnvelope{ payload=1 (Payload), pattern=2 (repeated Selection{ type=1, matcher=2 }) }
def parse_selection_envelope(buf):
    f = P.pb_parse(buf)
    serializer_id, manifest, message, path = 0, "", b"", []
    p = P.pb_get(f, 1)
    if p and p["wire"] == 2:
        pf = P.pb_parse(p["data"])
        x = P.pb_get(pf, 1)
        if x:
            message = x["data"]
        x = P.pb_get(pf, 2)
        if x:
            serializer_id = x["val"]
        x = P.pb_get(pf, 3)
        if x:
            manifest = x["data"].decode("utf-8")
    for fld in f:
        if fld["num"] == 2 and fld["wire"] == 2:
            mt = P.pb_get(P.pb_parse(fld["data"]), 2)
            if mt:
                path.append(mt["data"].decode("utf-8"))
    return serializer_id, manifest, message, path


def parse_welcome(b):
    f = P.pb_parse(b)
    fr = P.pb_get(f, 1)
    g = P.pb_get(f, 2)
    return (fr["data"] if fr else None), (g["data"] if g else b"")


def parse_gossip_envelope(b):
    f = P.pb_parse(b)
    fr = P.pb_get(f, 1)
    g = P.pb_get(f, 3)
    return (fr["data"] if fr else None), (g["data"] if g else b"")


def _zigzag(n):
    return (n >> 1) ^ -(n & 1)


# Heartbeat{ from=1 (AddressData), sequenceNr=2 (int64), creationTime=3 (sint64) }
def parse_heartbeat(b):
    f = P.pb_parse(b)
    seq = ct = 0
    x = P.pb_get(f, 2)
    if x:
        seq = x["val"]
    x = P.pb_get(f, 3)
    if x:
        ct = _zigzag(x["val"]) & 0xFFFFFFFFFFFFFFFF
    return seq, ct


# ---- Gossip surgery: find our index and mark ourselves "seen" ----

def _reemit(f):
    if f["wire"] == 0:
        return P.pb_varint(f["num"], f["val"])
    if f["wire"] == 1:
        return P.pb_fixed64(f["num"], f["val"])
    if f["wire"] == 2:
        return P.pb_bytes(f["num"], f["data"])
    if f["wire"] == 5:
        return P.varint((f["num"] << 3) | 5) + struct.pack("<I", f["val"])
    return b""


def gossip_address_index(gossip, host, port, uid):
    f = P.pb_parse(gossip)
    idx = 0
    for fld in f:
        if fld["num"] != 1 or fld["wire"] != 2:
            continue
        ua = P.pb_parse(fld["data"])
        ua_uid = ua_host = ua_port = None
        x = P.pb_get(ua, 2)
        if x:
            ua_uid = x["val"]
        x = P.pb_get(ua, 1)
        if x:
            ad = P.pb_parse(x["data"])
            y = P.pb_get(ad, 2)
            ua_host = y["data"].decode("utf-8") if y else ""
            y = P.pb_get(ad, 3)
            ua_port = y["val"] if y else 0
        if ua_host == host and ua_port == port and ua_uid == uid:
            return idx
        idx += 1
    return -1


# Member{ addressIndex=1, status=3 }
def gossip_member_status(gossip, address_index):
    for fld in P.pb_parse(gossip):
        if fld["num"] != 4 or fld["wire"] != 2:
            continue
        mf = P.pb_parse(fld["data"])
        ai = P.pb_get(mf, 1)
        st = P.pb_get(mf, 3)
        if ai and ai["val"] == address_index:
            return st["val"] if st else 0
    return None


def patch_gossip_seen(gossip, worker_index):
    out = []
    overview_done = False
    for fld in P.pb_parse(gossip):
        if fld["num"] == 5 and fld["wire"] == 2:
            out.append(P.pb_bytes(5, _patch_overview_seen(fld["data"], worker_index)))
            overview_done = True
        else:
            out.append(_reemit(fld))
    if not overview_done:
        out.append(P.pb_bytes(5, P.pb_bytes(1, P.pack_varints([worker_index]))))
    return b"".join(out)


def _patch_overview_seen(ov, worker_index):
    seen = []
    seen_set = set()
    others = []
    for fld in P.pb_parse(ov):
        if fld["num"] == 1:
            vals = P.parse_packed_varints(fld["data"]) if fld["wire"] == 2 else [fld["val"]]
            for v in vals:
                if v not in seen_set:
                    seen_set.add(v)
                    seen.append(v)
        else:
            others.append(_reemit(fld))
    if worker_index not in seen_set:
        seen.append(worker_index)
    return P.pb_bytes(1, P.pack_varints(seen)) + b"".join(others)

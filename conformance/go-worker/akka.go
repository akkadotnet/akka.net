package main

// Akka.NET remoting wire format: framing, the Akka protocol PDUs (ASSOCIATE / HEARTBEAT / PAYLOAD),
// the remote envelope, and cluster-message serialization. See WireFormats.proto / ContainerFormats.proto
// and ClusterMessages.proto in the akka.net repo for the authoritative definitions.

import (
	"encoding/binary"
	"fmt"
	"io"
	"net"
	"strconv"
	"strings"
)

// ---- Akka protocol command types (WireFormats.proto CommandType) ----
const (
	cmdNONE                     = 0
	cmdASSOCIATE                = 1
	cmdDISASSOCIATE             = 2
	cmdHEARTBEAT                = 3
	cmdDISASSOCIATE_SHUTTINGDOWN = 4
	cmdDISASSOCIATE_QUARANTINED  = 5
)

// ClusterMessageSerializer identifier (akka.cluster Cluster.conf serialization-identifiers).
const clusterSerializerId = 5

// MessageContainerSerializer identifier (Akka.Remote): wraps ActorSelection messages. The seed sends
// gossip and heartbeats via ActorSelection, so they arrive wrapped in a SelectionEnvelope.
const messageContainerSerializerId = 6

// Cluster message string manifests (ClusterMessageSerializer).
const (
	manifestInitJoin         = "Akka.Cluster.InternalClusterAction+InitJoin, Akka.Cluster"
	manifestInitJoinAck      = "Akka.Cluster.InternalClusterAction+InitJoinAck, Akka.Cluster"
	manifestInitJoinNack     = "Akka.Cluster.InternalClusterAction+InitJoinNack, Akka.Cluster"
	manifestJoin             = "Akka.Cluster.InternalClusterAction+Join, Akka.Cluster"
	manifestWelcome          = "Akka.Cluster.InternalClusterAction+Welcome, Akka.Cluster"
	manifestLeave            = "Akka.Cluster.ClusterUserAction+Leave, Akka.Cluster"
	manifestExitingConfirmed = "Akka.Cluster.InternalClusterAction+ExitingConfirmed, Akka.Cluster"
	manifestGossipEnvelope   = "Akka.Cluster.GossipEnvelope, Akka.Cluster"
	manifestGossipStatus     = "Akka.Cluster.GossipStatus, Akka.Cluster"
	manifestHeartbeat        = "HB"
	manifestHeartbeatRsp     = "HBR"
)

// ---- Address ----

type Address struct {
	Protocol string // "akka.tcp"
	System   string // cluster (actor system) name
	Host     string
	Port     int
}

func parseAddress(s string) (Address, error) {
	// akka.tcp://System@host:port
	var a Address
	i := strings.Index(s, "://")
	if i < 0 {
		return a, fmt.Errorf("bad address %q", s)
	}
	a.Protocol = s[:i]
	rest := s[i+3:]
	at := strings.Index(rest, "@")
	if at < 0 {
		return a, fmt.Errorf("bad address %q", s)
	}
	a.System = rest[:at]
	hp := rest[at+1:]
	// strip any trailing path
	if sl := strings.Index(hp, "/"); sl >= 0 {
		hp = hp[:sl]
	}
	host, portStr, err := net.SplitHostPort(hp)
	if err != nil {
		return a, err
	}
	a.Host = host
	a.Port, err = strconv.Atoi(portStr)
	return a, err
}

func (a Address) String() string {
	return fmt.Sprintf("%s://%s@%s:%d", a.Protocol, a.System, a.Host, a.Port)
}

// actorPath returns the full serialization-format path for an actor on this address.
func (a Address) actorPath(path string) string {
	return a.String() + path
}

// addressData encodes an AddressData message (system=1, hostname=2, port=3, protocol=4).
func (a Address) addressData() []byte {
	var b []byte
	b = append(b, pbString(1, a.System)...)
	b = append(b, pbString(2, a.Host)...)
	b = append(b, pbVarint(3, uint64(a.Port))...)
	b = append(b, pbString(4, a.Protocol)...)
	return b
}

// ---- Framing: 4-byte little-endian length prefix (payload only) ----

func writeFrame(w io.Writer, payload []byte) error {
	var hdr [4]byte
	binary.LittleEndian.PutUint32(hdr[:], uint32(len(payload)))
	if _, err := w.Write(hdr[:]); err != nil {
		return err
	}
	_, err := w.Write(payload)
	return err
}

func readFrame(r io.Reader) ([]byte, error) {
	var hdr [4]byte
	if _, err := io.ReadFull(r, hdr[:]); err != nil {
		return nil, err
	}
	n := binary.LittleEndian.Uint32(hdr[:])
	if n == 0 {
		return []byte{}, nil
	}
	buf := make([]byte, n)
	if _, err := io.ReadFull(r, buf); err != nil {
		return nil, err
	}
	return buf, nil
}

// ---- Akka protocol PDUs ----

// constructAssociate builds AkkaProtocolMessage{ instruction: { commandType: ASSOCIATE, handshakeInfo } }.
func constructAssociate(origin Address, uid uint64) []byte {
	// AkkaHandshakeInfo: origin=1 (AddressData), uid=2 (fixed64)
	var hs []byte
	hs = append(hs, pbBytes(1, origin.addressData())...)
	hs = append(hs, pbFixed64(2, uid)...)
	// AkkaControlMessage: commandType=1 (varint), handshakeInfo=2
	var ctrl []byte
	ctrl = append(ctrl, pbVarint(1, cmdASSOCIATE)...)
	ctrl = append(ctrl, pbBytes(2, hs)...)
	// AkkaProtocolMessage: instruction=2
	return pbBytes(2, ctrl)
}

// constructHeartbeat builds AkkaProtocolMessage{ instruction: { commandType: HEARTBEAT } }.
func constructHeartbeat() []byte {
	ctrl := pbVarint(1, cmdHEARTBEAT)
	return pbBytes(2, ctrl)
}

// constructPayload builds AkkaProtocolMessage{ payload: inner } (payload=1, bytes).
func constructPayload(inner []byte) []byte {
	return pbBytes(1, inner)
}

// ---- Cluster message bodies ----

// uniqueAddress encodes UniqueAddress{ address=1 (AddressData), uid=2 (uint32) }.
func uniqueAddress(a Address, uid uint32) []byte {
	var b []byte
	b = append(b, pbBytes(1, a.addressData())...)
	b = append(b, pbVarint(2, uint64(uid))...)
	return b
}

// constructJoin encodes Join{ node=1 (UniqueAddress), roles=2 (repeated string), appVersion=3 (string) }.
func constructJoin(node Address, uid uint32, roles []string, appVersion string) []byte {
	var b []byte
	b = append(b, pbBytes(1, uniqueAddress(node, uid))...)
	for _, r := range roles {
		b = append(b, pbString(2, r)...)
	}
	b = append(b, pbString(3, appVersion)...)
	return b
}

// ---- Remote envelope ----

// actorRefData encodes ActorRefData{ path=1 }.
func actorRefData(path string) []byte {
	return pbString(1, path)
}

// payloadMsg encodes Payload{ message=1, serializerId=2, messageManifest=3 }.
func payloadMsg(serializerId int, manifest string, message []byte) []byte {
	var b []byte
	b = append(b, pbBytes(1, message)...)
	b = append(b, pbVarint(2, uint64(serializerId))...)
	b = append(b, pbBytes(3, []byte(manifest))...)
	return b
}

const seqUndefined = ^uint64(0) // ulong.MaxValue: unacked delivery

// constructMessage builds the full PAYLOAD PDU carrying one actor message:
// AkkaProtocolMessage{ payload: AckAndEnvelopeContainer{ envelope: RemoteEnvelope{...} } }.
func constructMessage(recipientPath, senderPath string, serializerId int, manifest string, message []byte) []byte {
	// RemoteEnvelope: recipient=1, message=2 (Payload), sender=4, seq=5 (fixed64)
	var env []byte
	env = append(env, pbBytes(1, actorRefData(recipientPath))...)
	env = append(env, pbBytes(2, payloadMsg(serializerId, manifest, message))...)
	env = append(env, pbBytes(4, actorRefData(senderPath))...)
	env = append(env, pbFixed64(5, seqUndefined)...)
	// AckAndEnvelopeContainer: envelope=2
	container := pbBytes(2, env)
	return constructPayload(container)
}

// ---- Heartbeat (HB) / HeartbeatRsp (HBR) ----

func zigzagDecode(n uint64) int64 { return int64(n>>1) ^ -int64(n&1) }

// parseHeartbeat reads Heartbeat{ from=1 (AddressData), sequenceNr=2 (int64), creationTime=3 (sint64) }.
func parseHeartbeat(msg []byte) (seq int64, creationTime int64) {
	f, _ := pbParse(msg)
	if x := pbGet(f, 2); x != nil {
		seq = int64(x.val)
	}
	if x := pbGet(f, 3); x != nil {
		creationTime = zigzagDecode(x.val)
	}
	return
}

// buildHeartbeatRsp encodes HeartBeatResponse{ from=1 (UniqueAddress), sequenceNr=2 (int64), creationTime=3 (int64) }.
func buildHeartbeatRsp(fromUA []byte, seq, creationTime int64) []byte {
	var b []byte
	b = append(b, pbBytes(1, fromUA)...)
	b = append(b, pbVarint(2, uint64(seq))...)
	b = append(b, pbVarint(3, uint64(creationTime))...)
	return b
}

// ---- Welcome / GossipEnvelope ----

// parseWelcome reads Welcome{ from=1 (UniqueAddress), gossip=2 (Gossip) } and returns the raw gossip bytes.
func parseWelcome(msg []byte) (fromUA, gossip []byte) {
	f, _ := pbParse(msg)
	if x := pbGet(f, 1); x != nil {
		fromUA = x.data
	}
	if x := pbGet(f, 2); x != nil {
		gossip = x.data
	}
	return
}

// parseGossipEnvelope reads GossipEnvelope{ from=1, to=2 (UniqueAddress), serializedGossip=3 (bytes) }.
func parseGossipEnvelope(msg []byte) (fromUA, toUA, gossip []byte) {
	f, _ := pbParse(msg)
	if x := pbGet(f, 1); x != nil {
		fromUA = x.data
	}
	if x := pbGet(f, 2); x != nil {
		toUA = x.data
	}
	if x := pbGet(f, 3); x != nil {
		gossip = x.data
	}
	return
}

// buildGossipEnvelope encodes GossipEnvelope{ from=1, to=2, serializedGossip=3 }.
func buildGossipEnvelope(fromUA, toUA, gossip []byte) []byte {
	var b []byte
	b = append(b, pbBytes(1, fromUA)...)
	b = append(b, pbBytes(2, toUA)...)
	b = append(b, pbBytes(3, gossip)...)
	return b
}

// parseSelectionEnvelope unwraps an ActorSelectionMessage (MessageContainerSerializer):
// SelectionEnvelope{ payload=1 (Payload), pattern=2 (repeated Selection{ type=1, matcher=2 }) }.
// Returns the inner message's serializer id, manifest, bytes, and the selection path (matchers).
func parseSelectionEnvelope(msg []byte) (innerSerializerId int, innerManifest string, innerMsg []byte, path []string) {
	f, _ := pbParse(msg)
	if p := pbGet(f, 1); p != nil && p.wire == 2 {
		pf, _ := pbParse(p.data)
		if m := pbGet(pf, 1); m != nil {
			innerMsg = m.data
		}
		if s := pbGet(pf, 2); s != nil {
			innerSerializerId = int(s.val)
		}
		if mm := pbGet(pf, 3); mm != nil {
			innerManifest = string(mm.data)
		}
	}
	for _, fld := range f {
		if fld.num == 2 && fld.wire == 2 {
			sf, _ := pbParse(fld.data)
			if mt := pbGet(sf, 2); mt != nil {
				path = append(path, string(mt.data))
			}
		}
	}
	return
}

// ---- Gossip surgery: find our index and mark ourselves "seen" ----

// reemit re-encodes a parsed field back to wire form (used to copy fields verbatim).
func reemit(f pbField) []byte {
	switch f.wire {
	case 0:
		return pbVarint(f.num, f.val)
	case 1:
		return pbFixed64(f.num, f.val)
	case 2:
		return pbBytes(f.num, f.data)
	case 5:
		b := appendVarint(nil, wireTag(f.num, 5))
		var x [4]byte
		binary.LittleEndian.PutUint32(x[:], uint32(f.val))
		return append(b, x[:]...)
	}
	return nil
}

func parsePackedVarints(b []byte) []uint64 {
	var out []uint64
	i := 0
	for i < len(b) {
		v, n := uvarint(b[i:])
		if n <= 0 {
			break
		}
		i += n
		out = append(out, v)
	}
	return out
}

func packVarints(vals []int) []byte {
	var b []byte
	for _, v := range vals {
		b = appendVarint(b, uint64(v))
	}
	return b
}

// Member statuses (ClusterMessages.proto Member.MemberStatus).
const (
	statusJoining  = 0
	statusUp       = 1
	statusLeaving  = 2
	statusExiting  = 3
	statusDown     = 4
	statusRemoved  = 5
	statusWeaklyUp = 6
)

func statusName(s int) string {
	switch s {
	case statusJoining:
		return "Joining"
	case statusUp:
		return "Up"
	case statusLeaving:
		return "Leaving"
	case statusExiting:
		return "Exiting"
	case statusDown:
		return "Down"
	case statusRemoved:
		return "Removed"
	case statusWeaklyUp:
		return "WeaklyUp"
	default:
		return fmt.Sprintf("status(%d)", s)
	}
}

// gossipMemberStatus returns the status of the member with the given allAddresses index.
// Member{ addressIndex=1, upNumber=2, status=3, ... }.
func gossipMemberStatus(gossip []byte, addressIndex int) (int, bool) {
	f, _ := pbParse(gossip)
	for _, fld := range f {
		if fld.num != 4 || fld.wire != 2 { // Member
			continue
		}
		mf, _ := pbParse(fld.data)
		ai := -1
		st := 0
		if x := pbGet(mf, 1); x != nil {
			ai = int(x.val)
		}
		if x := pbGet(mf, 3); x != nil {
			st = int(x.val)
		}
		if ai == addressIndex {
			return st, true
		}
	}
	return 0, false
}

// gossipAddressIndex finds the index of (host, port, uid) within the gossip's allAddresses (field 1).
func gossipAddressIndex(gossip []byte, host string, port int, uid uint32) int {
	f, _ := pbParse(gossip)
	idx := 0
	for _, fld := range f {
		if fld.num != 1 || fld.wire != 2 {
			continue
		}
		ua, _ := pbParse(fld.data)
		var uaUID uint32
		var uaHost string
		var uaPort int
		if u := pbGet(ua, 2); u != nil {
			uaUID = uint32(u.val)
		}
		if a := pbGet(ua, 1); a != nil {
			ad, _ := pbParse(a.data)
			if h := pbGet(ad, 2); h != nil {
				uaHost = string(h.data)
			}
			if p := pbGet(ad, 3); p != nil {
				uaPort = int(p.val)
			}
		}
		if uaHost == host && uaPort == port && uaUID == uid {
			return idx
		}
		idx++
	}
	return -1
}

// patchGossipSeen returns the gossip bytes with workerIndex added to overview.seen, leaving the
// version (vector clock), members and everything else byte-identical so the reference node treats
// it as the same version and simply unions the seen sets (achieving convergence).
func patchGossipSeen(gossip []byte, workerIndex int) []byte {
	f, _ := pbParse(gossip)
	var out []byte
	overviewDone := false
	for _, fld := range f {
		if fld.num == 5 && fld.wire == 2 { // GossipOverview
			out = append(out, pbBytes(5, patchOverviewSeen(fld.data, workerIndex))...)
			overviewDone = true
		} else {
			out = append(out, reemit(fld)...)
		}
	}
	if !overviewDone {
		out = append(out, pbBytes(5, pbBytes(1, packVarints([]int{workerIndex})))...)
	}
	return out
}

// patchOverviewSeen adds workerIndex to the seen set (field 1, packed int32) of a GossipOverview.
func patchOverviewSeen(ov []byte, workerIndex int) []byte {
	f, _ := pbParse(ov)
	seen := map[int]bool{}
	var order []int
	var others []byte
	for _, fld := range f {
		if fld.num == 1 {
			if fld.wire == 2 {
				for _, v := range parsePackedVarints(fld.data) {
					if !seen[int(v)] {
						seen[int(v)] = true
						order = append(order, int(v))
					}
				}
			} else if fld.wire == 0 {
				if !seen[int(fld.val)] {
					seen[int(fld.val)] = true
					order = append(order, int(fld.val))
				}
			}
		} else {
			others = append(others, reemit(fld)...)
		}
	}
	if !seen[workerIndex] {
		order = append(order, workerIndex)
	}
	var out []byte
	out = append(out, pbBytes(1, packVarints(order))...) // re-encode seen as packed
	out = append(out, others...)
	return out
}

// ---- Parsing inbound PDUs ----

type inboundPdu struct {
	isControl   bool
	commandType int
	origin      Address // for ASSOCIATE
	uid         uint64
	// for payload:
	recipientPath string
	senderPath    string
	serializerId  int
	manifest      string
	message       []byte
}

func parsePdu(frame []byte) (inboundPdu, error) {
	var p inboundPdu
	fields, err := pbParse(frame)
	if err != nil {
		return p, err
	}
	if instr := pbGet(fields, 2); instr != nil && instr.wire == 2 {
		// control message
		p.isControl = true
		cf, err := pbParse(instr.data)
		if err != nil {
			return p, err
		}
		if ct := pbGet(cf, 1); ct != nil {
			p.commandType = int(ct.val)
		}
		if hs := pbGet(cf, 2); hs != nil && hs.wire == 2 {
			hf, err := pbParse(hs.data)
			if err == nil {
				if o := pbGet(hf, 1); o != nil && o.wire == 2 {
					p.origin = parseAddressData(o.data)
				}
				if u := pbGet(hf, 2); u != nil {
					p.uid = u.val
				}
			}
		}
		return p, nil
	}
	if pl := pbGet(fields, 1); pl != nil && pl.wire == 2 {
		// payload: AckAndEnvelopeContainer
		cf, err := pbParse(pl.data)
		if err != nil {
			return p, err
		}
		if env := pbGet(cf, 2); env != nil && env.wire == 2 {
			ef, err := pbParse(env.data)
			if err != nil {
				return p, err
			}
			if r := pbGet(ef, 1); r != nil && r.wire == 2 {
				p.recipientPath = parseActorRef(r.data)
			}
			if s := pbGet(ef, 4); s != nil && s.wire == 2 {
				p.senderPath = parseActorRef(s.data)
			}
			if m := pbGet(ef, 2); m != nil && m.wire == 2 {
				mf, err := pbParse(m.data)
				if err == nil {
					if mm := pbGet(mf, 1); mm != nil {
						p.message = mm.data
					}
					if sid := pbGet(mf, 2); sid != nil {
						p.serializerId = int(sid.val)
					}
					if man := pbGet(mf, 3); man != nil {
						p.manifest = string(man.data)
					}
				}
			}
		}
	}
	return p, nil
}

func parseAddressData(b []byte) Address {
	var a Address
	f, err := pbParse(b)
	if err != nil {
		return a
	}
	if x := pbGet(f, 1); x != nil {
		a.System = string(x.data)
	}
	if x := pbGet(f, 2); x != nil {
		a.Host = string(x.data)
	}
	if x := pbGet(f, 3); x != nil {
		a.Port = int(x.val)
	}
	if x := pbGet(f, 4); x != nil {
		a.Protocol = string(x.data)
	}
	return a
}

func parseActorRef(b []byte) string {
	f, err := pbParse(b)
	if err != nil {
		return ""
	}
	if x := pbGet(f, 1); x != nil {
		return string(x.data)
	}
	return ""
}

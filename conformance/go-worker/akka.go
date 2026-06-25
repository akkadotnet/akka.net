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

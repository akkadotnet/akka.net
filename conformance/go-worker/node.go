package main

// A bidirectional Akka.NET cluster node. The worker dials the seed (connection A, carrying
// worker->seed messages) AND listens on its advertised port for the seed's dial-back
// (connection B, carrying seed->worker messages). It answers cluster heartbeats so it stays
// reachable, and echoes gossip with itself marked "seen" so the cluster actually converges.

import (
	"fmt"
	"net"
	"sync"
	"time"
)

const (
	daemonPath = "/system/cluster/core/daemon"
	hbRecvPath = "/system/cluster/heartbeatReceiver"
	echoPath   = "/user/echo"
)

// isEchoSelection reports whether an ActorSelection path targets our /user/echo routee.
func isEchoSelection(path []string) bool {
	return len(path) >= 1 && path[len(path)-1] == "echo"
}

type Node struct {
	self Address
	seed Address
	uid  uint32

	out   net.Conn // connection A: worker -> seed
	outMu sync.Mutex

	mu                   sync.Mutex
	seedUA               []byte // seed's UniqueAddress bytes (learned from Welcome/gossip)
	workerIndex          int    // our index within the gossip's allAddresses
	selfStatus           int    // latest observed membership status of ourselves (-1 = unknown)
	gossipSent           int
	hbLogged             bool
	echoLogged           bool
	exitingConfirmedSent bool
}

func newNode(self, seed Address, uid uint32) *Node {
	return &Node{self: self, seed: seed, uid: uid, workerIndex: -1, selfStatus: -1}
}

func (n *Node) SelfStatus() int {
	n.mu.Lock()
	defer n.mu.Unlock()
	return n.selfStatus
}

func (n *Node) ExitingConfirmedSent() bool {
	n.mu.Lock()
	defer n.mu.Unlock()
	return n.exitingConfirmedSent
}

// sendLeave asks the cluster to remove this node gracefully (Leave carries the node's Address).
func (n *Node) sendLeave() error {
	return n.send(n.seed.actorPath(daemonPath), n.self.actorPath(daemonPath), manifestLeave, n.self.addressData())
}

func firstN(b []byte, n int) []byte {
	if len(b) < n {
		return b
	}
	return b[:n]
}

func (n *Node) selfUA() []byte { return uniqueAddress(n.self, n.uid) }

// send writes one cluster message (serializer 5) over connection A (worker -> seed).
func (n *Node) send(recipientPath, senderPath, manifest string, msg []byte) error {
	return n.sendRaw(recipientPath, senderPath, clusterSerializerId, manifest, msg)
}

// sendRaw writes one actor message with an explicit serializer id over connection A. Used to echo a
// broadcast back to its sender verbatim, preserving whatever serializer the broadcast used.
func (n *Node) sendRaw(recipientPath, senderPath string, serializerId int, manifest string, msg []byte) error {
	frame := constructMessage(recipientPath, senderPath, serializerId, manifest, msg)
	n.outMu.Lock()
	defer n.outMu.Unlock()
	if n.out == nil {
		return fmt.Errorf("no outbound connection")
	}
	return writeFrame(n.out, frame)
}

// ---- connection A: outbound to the seed ----

func (n *Node) connectOutbound() error {
	conn, err := net.DialTimeout("tcp", fmt.Sprintf("%s:%d", n.seed.Host, n.seed.Port), 5*time.Second)
	if err != nil {
		return err
	}
	n.out = conn
	logf("TCP connected to seed (conn A)")

	assoc := make(chan Address, 1)
	go n.readControl(conn, "A", assoc)

	if err := writeFrame(conn, constructAssociate(n.self, uint64(n.uid))); err != nil {
		return err
	}
	logf("A-> ASSOCIATE (origin=%s uid=%d)", n.self, n.uid)
	select {
	case <-assoc:
		logf("A<- ASSOCIATE reply from seed")
	case <-time.After(5 * time.Second):
		logf("WARNING: no ASSOCIATE reply on conn A within 5s")
	}

	// transport keepalive
	go func() {
		t := time.NewTicker(1 * time.Second)
		defer t.Stop()
		for range t.C {
			n.outMu.Lock()
			err := writeFrame(conn, constructHeartbeat())
			n.outMu.Unlock()
			if err != nil {
				return
			}
		}
	}()
	return nil
}

// readControl reads a connection that only carries control PDUs of interest (conn A), logging them.
func (n *Node) readControl(conn net.Conn, tag string, assoc chan<- Address) {
	for {
		frame, err := readFrame(conn)
		if err != nil {
			return
		}
		pdu, err := parsePdu(frame)
		if err != nil {
			continue
		}
		if pdu.isControl && pdu.commandType == cmdASSOCIATE {
			select {
			case assoc <- pdu.origin:
			default:
			}
		}
	}
}

// ---- connection B: inbound listener (the seed dials back) ----

func (n *Node) listen() error {
	ln, err := net.Listen("tcp", fmt.Sprintf("%s:%d", n.self.Host, n.self.Port))
	if err != nil {
		return err
	}
	logf("listening on %s:%d (conn B)", n.self.Host, n.self.Port)
	go func() {
		for {
			conn, err := ln.Accept()
			if err != nil {
				return
			}
			go n.handleInbound(conn)
		}
	}()
	return nil
}

func (n *Node) handleInbound(conn net.Conn) {
	defer conn.Close()
	logf("B<- seed connected (conn B from %s)", conn.RemoteAddr())
	associated := false
	for {
		frame, err := readFrame(conn)
		if err != nil {
			return
		}
		pdu, err := parsePdu(frame)
		if err != nil {
			continue
		}
		if pdu.isControl {
			switch pdu.commandType {
			case cmdASSOCIATE:
				// reply with our own ASSOCIATE so the seed considers the association open
				if !associated {
					_ = writeFrame(conn, constructAssociate(n.self, uint64(n.uid)))
					associated = true
					logf("B-> ASSOCIATE reply sent")
				}
			case cmdHEARTBEAT:
				// transport keepalive; ignore
			default:
				logf("B<- control type=%d", pdu.commandType)
			}
			continue
		}
		n.dispatch(pdu)
	}
}

// dispatch handles a cluster message the seed sent us on connection B. Messages sent via
// ActorSelection (gossip, heartbeats) arrive wrapped in a SelectionEnvelope (serializer 6); unwrap
// them to the real cluster message first.
func (n *Node) dispatch(pdu inboundPdu) {
	manifest := pdu.manifest
	message := pdu.message
	serializerId := pdu.serializerId
	var selPath []string
	if pdu.serializerId == messageContainerSerializerId {
		serializerId, manifest, message, selPath = parseSelectionEnvelope(pdu.message)
	}

	// A cluster broadcast router targets /user/echo on each node; echo the message back to the sender.
	if isEchoSelection(selPath) {
		if err := n.sendRaw(pdu.senderPath, n.self.actorPath(echoPath), serializerId, manifest, message); err != nil {
			logf("echo reply failed: %v", err)
		}
		n.mu.Lock()
		first := !n.echoLogged
		n.echoLogged = true
		n.mu.Unlock()
		if first {
			logf("A-> Echo reply to broadcast at /user/echo (further ones silent)")
		}
		return
	}

	switch manifest {
	case manifestInitJoinAck:
		logf("B<- InitJoinAck")
	case manifestWelcome:
		fromUA, gossip := parseWelcome(message)
		logf("B<- Welcome (gossip %d bytes)", len(gossip))
		n.onGossip(gossip, fromUA)
	case manifestGossipEnvelope:
		fromUA, _, gossip := parseGossipEnvelope(message)
		n.onGossip(gossip, fromUA)
	case manifestHeartbeat, "Akka.Cluster.ClusterHeartbeatSender+Heartbeat, Akka.Cluster":
		seq, ct := parseHeartbeat(message)
		// reply HeartbeatRsp to the heartbeat's sender, over connection A
		if err := n.send(pdu.senderPath, n.self.actorPath(hbRecvPath), manifestHeartbeatRsp,
			buildHeartbeatRsp(n.selfUA(), seq, ct)); err != nil {
			logf("HBR send failed: %v", err)
		}
		n.mu.Lock()
		first := !n.hbLogged
		n.hbLogged = true
		n.mu.Unlock()
		if first {
			logf("A-> HeartbeatRsp (answering cluster heartbeats; further ones silent)")
		}
	default:
		logf("B<- UNHANDLED recipient=%s serializer=%d manifest=%q msg=%dB hdr=%x",
			pdu.recipientPath, pdu.serializerId, manifest, len(message), firstN(message, 24))
	}
}

// onGossip records itself in the seen set of the received gossip and echoes it back, so the
// reference node observes convergence.
func (n *Node) onGossip(gossip, fromUA []byte) {
	if len(gossip) == 0 {
		return
	}
	n.mu.Lock()
	n.seedUA = fromUA
	if n.workerIndex < 0 {
		n.workerIndex = gossipAddressIndex(gossip, n.self.Host, n.self.Port, n.uid)
	}
	idx := n.workerIndex
	seedUA := n.seedUA
	n.mu.Unlock()

	if idx < 0 {
		// not yet present in the members list; echo unchanged so the seed sees us gossiping
		idx = gossipAddressIndex(gossip, n.self.Host, n.self.Port, n.uid)
	}

	var patched []byte
	if idx >= 0 {
		patched = patchGossipSeen(gossip, idx)
	} else {
		patched = gossip
	}

	if err := n.send(n.seed.actorPath(daemonPath), n.self.actorPath(daemonPath),
		manifestGossipEnvelope, buildGossipEnvelope(n.selfUA(), seedUA, patched)); err != nil {
		logf("gossip reply failed: %v", err)
		return
	}
	n.mu.Lock()
	n.gossipSent++
	cnt := n.gossipSent
	n.mu.Unlock()
	if cnt <= 3 || cnt%10 == 0 {
		logf("A-> Gossip echoed (seen+=index %d, #%d)", idx, cnt)
	}

	// Track our own membership status so we can react to Leaving/Exiting transitions.
	if idx >= 0 {
		if st, ok := gossipMemberStatus(gossip, idx); ok {
			n.onStatus(st)
		}
	}
}

// onStatus reacts to our own membership status as observed in incoming gossip. When the leader has
// moved us to Exiting, we confirm completion of the exit (ExitingConfirmed), which is what lets the
// leader remove us cleanly.
func (n *Node) onStatus(st int) {
	n.mu.Lock()
	changed := st != n.selfStatus
	n.selfStatus = st
	confirm := st == statusExiting && !n.exitingConfirmedSent
	if confirm {
		n.exitingConfirmedSent = true
	}
	n.mu.Unlock()

	if changed {
		logf("observed self status = %s", statusName(st))
	}
	if confirm {
		if err := n.send(n.seed.actorPath(daemonPath), n.self.actorPath(daemonPath),
			manifestExitingConfirmed, n.selfUA()); err != nil {
			logf("ExitingConfirmed failed: %v", err)
		} else {
			logf("A-> ExitingConfirmed")
		}
	}
}

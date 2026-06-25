package main

// A minimal Akka.NET-compatible cluster worker, written in Go, driven step by step by the
// ACT (Akka Conformance Tester) reference seed. This is intentionally grown one conformance
// step at a time. Current target: ACT step 1 (initial contact) — associate with the seed and
// send InitJoin so the reference node records InitJoin + replies InitJoinAck.

import (
	"crypto/rand"
	"encoding/binary"
	"flag"
	"fmt"
	"net"
	"os"
	"time"
)

func logf(format string, args ...any) {
	fmt.Printf("[%s] go-worker: "+format+"\n", append([]any{time.Now().Format("15:04:05.000")}, args...)...)
}

func randUid() uint64 {
	var b [8]byte
	_, _ = rand.Read(b[:])
	u := binary.LittleEndian.Uint64(b[:])
	if u == 0 {
		u = 1
	}
	return u
}

func main() {
	seedFlag := flag.String("seed", "", "seed node URI, e.g. akka.tcp://ConformanceCluster@127.0.0.1:5110")
	host := flag.String("host", "127.0.0.1", "advertised host of this worker")
	port := flag.Int("port", 6000, "advertised port of this worker")
	runSecs := flag.Int("run", 12, "seconds to stay connected")
	flag.Parse()

	if *seedFlag == "" {
		fmt.Fprintln(os.Stderr, "missing --seed")
		os.Exit(2)
	}

	seed, err := parseAddress(*seedFlag)
	if err != nil {
		fmt.Fprintln(os.Stderr, "bad --seed:", err)
		os.Exit(2)
	}

	self := Address{Protocol: "akka.tcp", System: seed.System, Host: *host, Port: *port}
	uid := randUid()
	logf("self=%s uid=%d  seed=%s", self, uid, seed)

	conn, err := net.DialTimeout("tcp", fmt.Sprintf("%s:%d", seed.Host, seed.Port), 5*time.Second)
	if err != nil {
		logf("DIAL FAILED: %v", err)
		os.Exit(1)
	}
	defer conn.Close()
	logf("TCP connected to seed")

	assocCh := make(chan Address, 1)
	go readLoop(conn, assocCh)

	// 1) Send our ASSOCIATE handshake.
	if err := writeFrame(conn, constructAssociate(self, uid)); err != nil {
		logf("send ASSOCIATE failed: %v", err)
		os.Exit(1)
	}
	logf("-> ASSOCIATE sent (origin=%s uid=%d)", self, uid)

	// 2) Wait for the seed's ASSOCIATE reply on this connection.
	select {
	case o := <-assocCh:
		logf("<- ASSOCIATE received from seed (origin=%s)", o)
	case <-time.After(5 * time.Second):
		logf("WARNING: no ASSOCIATE reply within 5s; sending InitJoin anyway")
	}

	// 3) Send InitJoin to the seed's cluster core daemon.
	daemon := "/system/cluster/core/daemon"
	initJoin := constructMessage(
		seed.actorPath(daemon),
		self.actorPath(daemon),
		clusterSerializerId, manifestInitJoin,
		[]byte{}, // InitJoin serializes to an empty protobuf
	)
	if err := writeFrame(conn, initJoin); err != nil {
		logf("send InitJoin failed: %v", err)
		os.Exit(1)
	}
	logf("-> InitJoin sent to %s (sender=%s)", seed.actorPath(daemon), self.actorPath(daemon))

	// 4) Keep the connection alive, sending heartbeats, so the seed can process InitJoin.
	hb := time.NewTicker(1 * time.Second)
	defer hb.Stop()
	deadline := time.After(time.Duration(*runSecs) * time.Second)
	for {
		select {
		case <-hb.C:
			if err := writeFrame(conn, constructHeartbeat()); err != nil {
				logf("heartbeat failed: %v", err)
				return
			}
		case <-deadline:
			logf("run window elapsed; closing")
			return
		}
	}
}

func readLoop(conn net.Conn, assocCh chan<- Address) {
	for {
		frame, err := readFrame(conn)
		if err != nil {
			logf("read loop ended: %v", err)
			return
		}
		pdu, err := parsePdu(frame)
		if err != nil {
			logf("<- [unparseable frame %d bytes]: %v", len(frame), err)
			continue
		}
		if pdu.isControl {
			switch pdu.commandType {
			case cmdASSOCIATE:
				select {
				case assocCh <- pdu.origin:
				default:
				}
				logf("<- control ASSOCIATE (origin=%s uid=%d)", pdu.origin, pdu.uid)
			case cmdHEARTBEAT:
				logf("<- control HEARTBEAT")
			case cmdDISASSOCIATE, cmdDISASSOCIATE_SHUTTINGDOWN, cmdDISASSOCIATE_QUARANTINED:
				logf("<- control DISASSOCIATE (type=%d)", pdu.commandType)
			default:
				logf("<- control type=%d", pdu.commandType)
			}
			continue
		}
		logf("<- PAYLOAD recipient=%s sender=%s serializer=%d manifest=%q (%d bytes)",
			pdu.recipientPath, pdu.senderPath, pdu.serializerId, pdu.manifest, len(pdu.message))
	}
}

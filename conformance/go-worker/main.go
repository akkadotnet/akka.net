package main

// A minimal Akka.NET-compatible cluster worker, written in Go, driven step by step by the
// ACT (Akka Conformance Tester) reference seed. It joins the C# seed, participates in gossip and
// heartbeats so it genuinely converges to Up, then (optionally) leaves gracefully.

import (
	"crypto/rand"
	"encoding/binary"
	"flag"
	"fmt"
	"os"
	"time"
)

func logf(format string, args ...any) {
	fmt.Printf("[%s] go-worker: "+format+"\n", append([]any{time.Now().Format("15:04:05.000")}, args...)...)
}

// randUid returns a non-zero 32-bit uid. The cluster UniqueAddress.uid is uint32, so we keep the
// node uid 32-bit and present the same value in the remoting handshake (as a fixed64) for consistency.
func randUid() uint32 {
	var b [4]byte
	_, _ = rand.Read(b[:])
	u := binary.LittleEndian.Uint32(b[:])
	if u == 0 {
		u = 1
	}
	return u
}

func main() {
	seedFlag := flag.String("seed", "", "seed node URI, e.g. akka.tcp://ConformanceCluster@127.0.0.1:5110")
	host := flag.String("host", "127.0.0.1", "advertised host of this worker")
	port := flag.Int("port", 6000, "advertised port of this worker")
	runSecs := flag.Int("run", 20, "seconds to stay in the cluster when not leaving gracefully")
	leave := flag.Bool("leave", true, "leave the cluster gracefully before exiting")
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
	n := newNode(self, seed, randUid())
	logf("self=%s uid=%d  seed=%s", self, n.uid, seed)

	// 1) Listen first, so the seed can dial back to deliver InitJoinAck / Welcome / gossip / heartbeats.
	if err := n.listen(); err != nil {
		logf("listen failed: %v", err)
		os.Exit(1)
	}

	// 2) Associate outbound with the seed.
	if err := n.connectOutbound(); err != nil {
		logf("connect failed: %v", err)
		os.Exit(1)
	}

	// 3) InitJoin (ACT step 1).
	if err := n.send(seed.actorPath(daemonPath), self.actorPath(daemonPath), manifestInitJoin, []byte{}); err != nil {
		logf("InitJoin failed: %v", err)
		os.Exit(1)
	}
	logf("A-> InitJoin")

	// 4) Join (ACT step 2). A real node sends this after InitJoinAck; the inbound ack arrives on
	// conn B and is logged, but we proceed promptly.
	time.Sleep(300 * time.Millisecond)
	roles := []string{"worker"}
	const appVersion = "1.5.60"
	if err := n.send(seed.actorPath(daemonPath), self.actorPath(daemonPath), manifestJoin,
		constructJoin(self, n.uid, roles, appVersion)); err != nil {
		logf("Join failed: %v", err)
		os.Exit(1)
	}
	logf("A-> Join (roles=%v version=%s uid=%d)", roles, appVersion, n.uid)

	// 5) Wait until the leader has moved us to Up (real convergence via gossip + heartbeats).
	if !waitFor(func() bool { return n.SelfStatus() == statusUp }, 20*time.Second) {
		logf("WARNING: never observed self = Up")
	} else {
		logf("*** worker is UP and a full member of the cluster ***")
	}

	if !*leave {
		time.Sleep(time.Duration(*runSecs) * time.Second)
		logf("run window elapsed; exiting (no graceful leave requested)")
		return
	}

	// 6) Settle briefly, then leave gracefully (ACT steps 6-9).
	time.Sleep(2 * time.Second)
	logf("--- initiating graceful leave ---")
	if err := n.sendLeave(); err != nil {
		logf("Leave failed: %v", err)
	} else {
		logf("A-> Leave(self)")
	}

	// 7) Stay alive through Leaving -> Exiting; onStatus sends ExitingConfirmed when we reach Exiting.
	if !waitFor(n.ExitingConfirmedSent, 20*time.Second) {
		logf("WARNING: never observed self = Exiting; sending ExitingConfirmed as a fallback")
		_ = n.send(seed.actorPath(daemonPath), self.actorPath(daemonPath), manifestExitingConfirmed, n.selfUA())
	}

	// 8) Linger so the leader records our clean removal (Removed, from Exiting), then exit.
	time.Sleep(5 * time.Second)
	logf("--- graceful leave complete; exiting ---")
}

// waitFor polls cond until it returns true or the timeout elapses.
func waitFor(cond func() bool, timeout time.Duration) bool {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if cond() {
			return true
		}
		time.Sleep(200 * time.Millisecond)
	}
	return cond()
}

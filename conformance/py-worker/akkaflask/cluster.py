"""A Flask-like interface over an Akka.NET-compatible cluster node.

Create a Cluster, register actors with the @app.actor(path) decorator (the return value of a handler
is sent back to the message's sender), and call run():

    app = Cluster("akka.tcp://ConformanceCluster@127.0.0.1:5110", port=6300)

    @app.actor("/user/echo")
    def echo(msg):
        return msg          # echo it back to the sender

    app.run()

Under the hood it speaks the real Akka.NET remoting + cluster wire protocol: the ASSOCIATE handshake
(bidirectionally), cluster heartbeats, gossip convergence and graceful leave — so the node genuinely
joins, converges to Up, serves its actors, and leaves cleanly.
"""

import os
import socket
import threading
import time

from .wire import (
    Address, parse_address, unique_address,
    frame, read_frame,
    construct_associate, construct_heartbeat, construct_message,
    construct_join, build_heartbeat_rsp, build_gossip_envelope,
    parse_pdu, parse_selection_envelope, parse_welcome, parse_gossip_envelope, parse_heartbeat,
    gossip_address_index, gossip_member_status, patch_gossip_seen, status_name,
    CMD_ASSOCIATE,
    CLUSTER_SERIALIZER_ID, MESSAGE_CONTAINER_SERIALIZER_ID,
    M_INIT_JOIN, M_INIT_JOIN_ACK, M_JOIN, M_WELCOME, M_LEAVE, M_EXITING_CONFIRMED,
    M_GOSSIP_ENVELOPE, M_HEARTBEAT, M_HEARTBEAT_RSP, M_HEARTBEAT_LEGACY,
    S_UP, S_EXITING,
)

DAEMON = "/system/cluster/core/daemon"
HB_RECEIVER = "/system/cluster/heartbeatReceiver"


def log(*args):
    ms = int(time.time() * 1000) % 1000
    t = time.strftime("%H:%M:%S", time.localtime()) + (".%03d" % ms)
    print("[%s] py-worker:" % t, *args, flush=True)


def _rand_uid():
    return int.from_bytes(os.urandom(4), "little") or 1


class Message:
    """A message delivered to an actor. Return it (or bytes/str) from a handler to reply to the sender."""

    __slots__ = ("serializer_id", "manifest", "payload", "sender")

    def __init__(self, serializer_id, manifest, payload, sender):
        self.serializer_id = serializer_id
        self.manifest = manifest
        self.payload = payload
        self.sender = sender

    @property
    def text(self):
        try:
            return self.payload.decode("utf-8")
        except Exception:
            return None

    def __repr__(self):
        return "Message(manifest=%r, %dB)" % (self.manifest, len(self.payload))


class Cluster:
    def __init__(self, seed, host="127.0.0.1", port=6300, system=None,
                 roles=("worker",), app_version="1.5.60"):
        self.seed = parse_address(seed)
        self.system = system or self.seed.system
        self.self = Address("akka.tcp", self.system, host, port)
        self.uid = _rand_uid()
        self.roles = list(roles)
        self.app_version = app_version

        self._handlers = {}            # actor path -> handler fn
        self._conn_a = None            # outbound socket (worker -> seed)
        self._send_lock = threading.Lock()
        self._state_lock = threading.Lock()
        self._assoc_event = threading.Event()
        self._up_event = threading.Event()
        self._stop_event = threading.Event()

        self._seed_ua = None
        self._worker_index = -1
        self._self_status = -1
        self._exiting_confirmed = False
        self._hb_logged = False
        self._echo_logged = False
        self._gossip_sent = 0

    # ---- Flask-like registration ----
    def actor(self, path):
        """Decorator: register a handler for an actor path (e.g. '/user/echo')."""
        def deco(fn):
            self._handlers[path] = fn
            return fn
        return deco

    # ---- lifecycle ----
    def run(self, leave=True, settle=2.0, idle=20.0, linger=5.0, up_timeout=20.0):
        log("self=%s uid=%d  seed=%s" % (self.self, self.uid, self.seed))
        self._listen()
        self._connect()
        self._send(self.seed.path(DAEMON), self.self.path(DAEMON), M_INIT_JOIN, b"")
        log("A-> InitJoin")
        time.sleep(0.3)
        self._send(self.seed.path(DAEMON), self.self.path(DAEMON), M_JOIN,
                   construct_join(self.self, self.uid, self.roles, self.app_version))
        log("A-> Join (roles=%s version=%s uid=%d)" % (self.roles, self.app_version, self.uid))

        if self._up_event.wait(up_timeout):
            log("*** worker is UP and a full member of the cluster ***")
        else:
            log("WARNING: never observed self = Up")

        if not leave:
            time.sleep(idle)
            log("run window elapsed; exiting (no graceful leave requested)")
            return self._stop()

        time.sleep(settle)
        log("--- initiating graceful leave ---")
        self._send(self.seed.path(DAEMON), self.self.path(DAEMON), M_LEAVE,
                   self._self_addr_data())
        log("A-> Leave(self)")
        deadline = time.time() + 20.0
        while time.time() < deadline and not self._exiting_confirmed:
            time.sleep(0.2)
        if not self._exiting_confirmed:
            log("WARNING: never observed Exiting; sending ExitingConfirmed as fallback")
            self._send(self.seed.path(DAEMON), self.self.path(DAEMON), M_EXITING_CONFIRMED,
                       unique_address(self.self, self.uid))
        time.sleep(linger)
        log("--- graceful leave complete; exiting ---")
        self._stop()

    def _stop(self):
        self._stop_event.set()
        try:
            if self._conn_a:
                self._conn_a.close()
        except Exception:
            pass

    def _self_addr_data(self):
        from .wire import address_data
        return address_data(self.self)

    # ---- connection A: outbound to the seed ----
    def _connect(self):
        s = socket.create_connection((self.seed.host, self.seed.port), timeout=5)
        s.settimeout(None)
        self._conn_a = s
        log("TCP connected to seed (conn A)")
        threading.Thread(target=self._read_conn_a, daemon=True).start()
        with self._send_lock:
            s.sendall(frame(construct_associate(self.self, self.uid)))
        log("A-> ASSOCIATE (origin=%s uid=%d)" % (self.self, self.uid))
        if self._assoc_event.wait(5.0):
            log("A<- ASSOCIATE reply from seed")
        else:
            log("WARNING: no ASSOCIATE reply on conn A within 5s")
        threading.Thread(target=self._transport_heartbeat, daemon=True).start()

    def _read_conn_a(self):
        try:
            while not self._stop_event.is_set():
                pdu = parse_pdu(read_frame(self._conn_a))
                if pdu.control and pdu.command_type == CMD_ASSOCIATE:
                    self._assoc_event.set()
        except Exception:
            pass

    def _transport_heartbeat(self):
        while not self._stop_event.is_set():
            with self._send_lock:
                if self._conn_a is not None:
                    try:
                        self._conn_a.sendall(frame(construct_heartbeat()))
                    except Exception:
                        return
            self._stop_event.wait(1.0)

    # ---- connection B: inbound listener (the seed dials back) ----
    def _listen(self):
        srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind((self.self.host, self.self.port))
        srv.listen(8)
        log("listening on %s:%d (conn B)" % (self.self.host, self.self.port))

        def accept_loop():
            while not self._stop_event.is_set():
                try:
                    conn, _ = srv.accept()
                except Exception:
                    return
                threading.Thread(target=self._handle_inbound, args=(conn,), daemon=True).start()

        threading.Thread(target=accept_loop, daemon=True).start()

    def _handle_inbound(self, conn):
        log("B<- seed connected (conn B from %s)" % (conn.getpeername(),))
        associated = False
        try:
            while not self._stop_event.is_set():
                pdu = parse_pdu(read_frame(conn))
                if pdu.control:
                    if pdu.command_type == CMD_ASSOCIATE and not associated:
                        associated = True
                        conn.sendall(frame(construct_associate(self.self, self.uid)))
                        log("B-> ASSOCIATE reply sent")
                    continue
                self._dispatch(pdu)
        except Exception:
            pass
        finally:
            try:
                conn.close()
            except Exception:
                pass

    # ---- sending over connection A ----
    def _send_raw(self, recipient, sender, serializer_id, manifest, message):
        data = frame(construct_message(recipient, sender, serializer_id, manifest, message))
        with self._send_lock:
            if self._conn_a is not None:
                try:
                    self._conn_a.sendall(data)
                except Exception as e:
                    log("send failed: %r" % e)

    def _send(self, recipient, sender, manifest, message):
        self._send_raw(recipient, sender, CLUSTER_SERIALIZER_ID, manifest, message)

    # ---- dispatch inbound cluster messages ----
    def _dispatch(self, pdu):
        manifest = pdu.manifest
        message = pdu.message
        serializer_id = pdu.serializer_id
        sel_path = []
        if pdu.serializer_id == MESSAGE_CONTAINER_SERIALIZER_ID:
            serializer_id, manifest, message, sel_path = parse_selection_envelope(pdu.message)

        # A message addressed to a user actor via ActorSelection (e.g. a broadcast to /user/echo).
        if sel_path:
            full = "/" + "/".join(sel_path)
            handler = self._handlers.get(full) or self._handlers.get("/" + sel_path[-1])
            if handler is not None:
                self._invoke_actor(full, handler,
                                   Message(serializer_id, manifest, message, pdu.sender_path))
                return

        if manifest == M_INIT_JOIN_ACK:
            log("B<- InitJoinAck")
        elif manifest == M_WELCOME:
            from_ua, gossip = parse_welcome(message)
            log("B<- Welcome (gossip %d bytes)" % len(gossip))
            self._on_gossip(gossip, from_ua)
        elif manifest == M_GOSSIP_ENVELOPE:
            from_ua, gossip = parse_gossip_envelope(message)
            self._on_gossip(gossip, from_ua)
        elif manifest in (M_HEARTBEAT, M_HEARTBEAT_LEGACY):
            seq, ct = parse_heartbeat(message)
            self._send(pdu.sender_path, self.self.path(HB_RECEIVER), M_HEARTBEAT_RSP,
                       build_heartbeat_rsp(unique_address(self.self, self.uid), seq, ct))
            if not self._hb_logged:
                self._hb_logged = True
                log("A-> HeartbeatRsp (answering cluster heartbeats; further ones silent)")

    def _invoke_actor(self, actor_path, handler, msg):
        try:
            reply = handler(msg)
        except Exception as e:
            log("actor handler error: %r" % e)
            return
        if reply is None:
            return
        sender = self.self.path(actor_path if actor_path in self._handlers else "/user/echo")
        if isinstance(reply, Message):
            self._send_raw(msg.sender, sender, reply.serializer_id, reply.manifest, reply.payload)
        elif isinstance(reply, (bytes, bytearray)):
            self._send_raw(msg.sender, sender, msg.serializer_id, msg.manifest, bytes(reply))
        elif isinstance(reply, str):
            self._send_raw(msg.sender, sender, msg.serializer_id, msg.manifest, reply.encode("utf-8"))
        if not self._echo_logged:
            self._echo_logged = True
            log("A-> reply from actor %s to %s (further ones silent)" % (actor_path, msg.sender))

    # ---- gossip + membership ----
    def _on_gossip(self, gossip, from_ua):
        if not gossip:
            return
        with self._state_lock:
            if from_ua:
                self._seed_ua = from_ua
            if self._worker_index < 0:
                self._worker_index = gossip_address_index(gossip, self.self.host, self.self.port, self.uid)
            idx = self._worker_index
            seed_ua = self._seed_ua
        patched = patch_gossip_seen(gossip, idx) if idx >= 0 else gossip
        if seed_ua is not None:
            self._send(self.seed.path(DAEMON), self.self.path(DAEMON), M_GOSSIP_ENVELOPE,
                       build_gossip_envelope(unique_address(self.self, self.uid), seed_ua, patched))
            with self._state_lock:
                self._gossip_sent += 1
                n = self._gossip_sent
            if n <= 3 or n % 10 == 0:
                log("A-> Gossip echoed (seen+=index %d, #%d)" % (idx, n))
        if idx >= 0:
            st = gossip_member_status(gossip, idx)
            if st is not None:
                self._on_status(st)

    def _on_status(self, st):
        with self._state_lock:
            changed = st != self._self_status
            self._self_status = st
            confirm = st == S_EXITING and not self._exiting_confirmed
            if confirm:
                self._exiting_confirmed = True
        if changed:
            log("observed self status = %s" % status_name(st))
        if st == S_UP:
            self._up_event.set()
        if confirm:
            self._send(self.seed.path(DAEMON), self.self.path(DAEMON), M_EXITING_CONFIRMED,
                       unique_address(self.self, self.uid))
            log("A-> ExitingConfirmed")

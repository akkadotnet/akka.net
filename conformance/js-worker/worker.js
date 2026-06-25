'use strict';
// An Akka.NET cluster worker in JavaScript, grown one ACT conformance step at a time.
//
// It speaks the Akka.NET remoting + cluster wire protocol directly (see akka.js / proto.js):
//   - bidirectional remoting: dials the seed (conn A, worker->seed) AND listens for the seed's
//     dial-back (conn B, seed->worker), since Akka opens a separate association per direction;
//   - answers cluster heartbeats so the failure detector keeps it reachable;
//   - echoes gossip with itself added to the 'seen' set so the leader observes convergence (Up);
//   - leaves gracefully: Leave(self) -> Exiting -> ExitingConfirmed -> clean removal.

const net = require('net');
const crypto = require('crypto');
const A = require('./akka');

const DAEMON = '/system/cluster/core/daemon';
const HB_RECEIVER = '/system/cluster/heartbeatReceiver';

function log(...args) {
  const t = new Date().toISOString().slice(11, 23);
  console.log(`[${t}] js-worker:`, ...args);
}

function randUid() {
  const u = crypto.randomBytes(4).readUInt32LE(0);
  return u === 0 ? 1 : u;
}

function parseArgs() {
  const out = { seed: '', host: '127.0.0.1', port: 6100, run: 20, leave: 'true' };
  for (const a of process.argv.slice(2)) {
    const m = a.match(/^--([^=]+)=(.*)$/);
    if (!m) continue;
    if (m[1] === 'port' || m[1] === 'run') out[m[1]] = parseInt(m[2], 10);
    else out[m[1]] = m[2];
  }
  return out;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function waitUntil(pred, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (pred()) return true;
    await sleep(200);
  }
  return pred();
}

class ClusterNode {
  constructor(self, seed, uid) {
    this.self = self;
    this.seed = seed;
    this.uid = uid;
    this.connA = null;        // outbound (worker -> seed)
    this.seedUA = null;       // seed's UniqueAddress bytes
    this.workerIndex = -1;    // our index in the gossip allAddresses
    this.selfStatus = -1;     // latest observed membership status of ourselves
    this.exitingConfirmedSent = false;
    this.hbLogged = false;
    this.echoLogged = false;
    this.gossipSent = 0;
  }

  selfUA() { return A.uniqueAddress(this.self, this.uid); }

  // send one cluster message (serializer 5) over connection A (worker -> seed)
  send(recipient, sender, manifest, msg) {
    this.sendRaw(recipient, sender, A.CLUSTER_SERIALIZER_ID, manifest, msg);
  }

  // send with an explicit serializer id (used to echo a broadcast back verbatim)
  sendRaw(recipient, sender, serializerId, manifest, msg) {
    if (!this.connA) return;
    this.connA.write(A.frame(A.constructMessage(recipient, sender, serializerId, manifest, msg)));
  }

  // ---- connection A: outbound to the seed ----
  connectOutbound() {
    return new Promise((resolve) => {
      const conn = net.connect(this.seed.port, this.seed.host, () => {
        this.connA = conn;
        log('TCP connected to seed (conn A)');
        conn.write(A.frame(A.constructAssociate(this.self, this.uid)));
        log(`A-> ASSOCIATE (origin=${A.addrString(this.self)} uid=${this.uid})`);
      });
      let associated = false;
      const reader = new A.FrameReader((payload) => {
        let pdu;
        try { pdu = A.parsePdu(payload); } catch { return; }
        if (pdu.control && pdu.commandType === A.CMD.ASSOCIATE && !associated) {
          associated = true;
          log('A<- ASSOCIATE reply from seed');
          resolve();
        }
      });
      conn.on('data', (c) => reader.push(c));
      conn.on('error', (e) => log('conn A error:', e.message));
      // transport keepalive
      setInterval(() => { if (this.connA) this.connA.write(A.frame(A.constructHeartbeat())); }, 1000);
      // proceed even if no reply arrives
      setTimeout(resolve, 5000);
    });
  }

  // ---- connection B: inbound listener (the seed dials back) ----
  listen() {
    return new Promise((resolve, reject) => {
      const server = net.createServer((conn) => this.handleInbound(conn));
      server.on('error', reject);
      server.listen(this.self.port, this.self.host, () => {
        log(`listening on ${this.self.host}:${this.self.port} (conn B)`);
        resolve();
      });
    });
  }

  handleInbound(conn) {
    log(`B<- seed connected (conn B from ${conn.remoteAddress}:${conn.remotePort})`);
    let associated = false;
    const reader = new A.FrameReader((payload) => {
      let pdu;
      try { pdu = A.parsePdu(payload); } catch { return; }
      if (pdu.control) {
        if (pdu.commandType === A.CMD.ASSOCIATE && !associated) {
          associated = true;
          conn.write(A.frame(A.constructAssociate(this.self, this.uid)));
          log('B-> ASSOCIATE reply sent');
        }
        return;
      }
      this.dispatch(pdu);
    });
    conn.on('data', (c) => reader.push(c));
    conn.on('error', () => {});
  }

  // a cluster message arrived on conn B; unwrap ActorSelection (serializer 6) first
  dispatch(pdu) {
    let { manifest, message, serializerId } = pdu;
    let selPath = [];
    if (pdu.serializerId === A.MESSAGE_CONTAINER_SERIALIZER_ID) {
      const sel = A.parseSelectionEnvelope(pdu.message);
      manifest = sel.manifest;
      message = sel.message;
      serializerId = sel.serializerId;
      selPath = sel.path;
    }

    // A cluster broadcast router targets /user/echo on each node; echo the message back to the sender.
    if (selPath.length && selPath[selPath.length - 1] === 'echo') {
      this.sendRaw(pdu.senderPath, A.actorPath(this.self, '/user/echo'), serializerId, manifest, message);
      if (!this.echoLogged) {
        this.echoLogged = true;
        log('A-> Echo reply to broadcast at /user/echo (further ones silent)');
      }
      return;
    }

    switch (manifest) {
      case A.MANIFEST.InitJoinAck:
        log('B<- InitJoinAck');
        break;
      case A.MANIFEST.Welcome: {
        const { fromUA, gossip } = A.parseWelcome(message);
        log(`B<- Welcome (gossip ${gossip.length} bytes)`);
        this.onGossip(gossip, fromUA);
        break;
      }
      case A.MANIFEST.GossipEnvelope: {
        const { fromUA, gossip } = A.parseGossipEnvelope(message);
        this.onGossip(gossip, fromUA);
        break;
      }
      case A.MANIFEST.Heartbeat:
      case 'Akka.Cluster.ClusterHeartbeatSender+Heartbeat, Akka.Cluster': {
        const { seq, creationTime } = A.parseHeartbeat(message);
        this.send(pdu.senderPath, A.actorPath(this.self, HB_RECEIVER), A.MANIFEST.HeartbeatRsp,
          A.buildHeartbeatRsp(this.selfUA(), seq, creationTime));
        if (!this.hbLogged) {
          this.hbLogged = true;
          log('A-> HeartbeatRsp (answering cluster heartbeats; further ones silent)');
        }
        break;
      }
      default:
        break;
    }
  }

  onGossip(gossip, fromUA) {
    if (!gossip || gossip.length === 0) return;
    if (fromUA) this.seedUA = fromUA;
    if (this.workerIndex < 0) {
      this.workerIndex = A.gossipAddressIndex(gossip, this.self.host, this.self.port, this.uid);
    }
    const idx = this.workerIndex;
    const patched = idx >= 0 ? A.patchGossipSeen(gossip, idx) : gossip;
    if (this.seedUA) {
      this.send(A.actorPath(this.seed, DAEMON), A.actorPath(this.self, DAEMON), A.MANIFEST.GossipEnvelope,
        A.buildGossipEnvelope(this.selfUA(), this.seedUA, patched));
      this.gossipSent++;
      if (this.gossipSent <= 3 || this.gossipSent % 10 === 0) {
        log(`A-> Gossip echoed (seen+=index ${idx}, #${this.gossipSent})`);
      }
    }
    if (idx >= 0) {
      const st = A.gossipMemberStatus(gossip, idx);
      if (st !== null) this.onStatus(st);
    }
  }

  onStatus(st) {
    if (st !== this.selfStatus) {
      this.selfStatus = st;
      log(`observed self status = ${A.statusName(st)}`);
    }
    if (st === A.STATUS.Exiting && !this.exitingConfirmedSent) {
      this.exitingConfirmedSent = true;
      this.send(A.actorPath(this.seed, DAEMON), A.actorPath(this.self, DAEMON), A.MANIFEST.ExitingConfirmed, this.selfUA());
      log('A-> ExitingConfirmed');
    }
  }

  sendInitJoin() {
    this.send(A.actorPath(this.seed, DAEMON), A.actorPath(this.self, DAEMON), A.MANIFEST.InitJoin, Buffer.alloc(0));
    log('A-> InitJoin');
  }
  sendJoin() {
    const roles = ['worker'];
    this.send(A.actorPath(this.seed, DAEMON), A.actorPath(this.self, DAEMON), A.MANIFEST.Join,
      A.constructJoin(this.self, this.uid, roles, '1.5.60'));
    log(`A-> Join (roles=${JSON.stringify(roles)} version=1.5.60 uid=${this.uid})`);
  }
  sendLeave() {
    this.send(A.actorPath(this.seed, DAEMON), A.actorPath(this.self, DAEMON), A.MANIFEST.Leave, A.addressData(this.self));
    log('A-> Leave(self)');
  }
}

async function main() {
  const args = parseArgs();
  if (!args.seed) { console.error('missing --seed'); process.exit(2); }
  const seed = A.parseAddress(args.seed);
  const self = { protocol: 'akka.tcp', system: seed.system, host: args.host, port: args.port };
  const node = new ClusterNode(self, seed, randUid());
  log(`self=${A.addrString(self)} uid=${node.uid}  seed=${A.addrString(seed)}`);

  await node.listen();          // listen first so the seed can dial back
  await node.connectOutbound(); // associate with the seed

  node.sendInitJoin();          // step 1
  await sleep(300);
  node.sendJoin();              // step 2

  // step 5: wait for real convergence to Up
  if (await waitUntil(() => node.selfStatus === A.STATUS.Up, 20000)) {
    log('*** worker is UP and a full member of the cluster ***');
  } else {
    log('WARNING: never observed self = Up');
  }

  if (args.leave !== 'true') {
    await sleep(args.run * 1000);
    log('run window elapsed; exiting (no graceful leave requested)');
    process.exit(0);
  }

  // steps 6-9: graceful leave
  await sleep(2000);
  log('--- initiating graceful leave ---');
  node.sendLeave();
  if (!(await waitUntil(() => node.exitingConfirmedSent, 20000))) {
    log('WARNING: never observed Exiting; sending ExitingConfirmed as fallback');
    node.send(A.actorPath(seed, DAEMON), A.actorPath(self, DAEMON), A.MANIFEST.ExitingConfirmed, node.selfUA());
  }
  await sleep(5000); // linger so the leader records our clean removal
  log('--- graceful leave complete; exiting ---');
  process.exit(0);
}

main().catch((e) => { console.error(e); process.exit(1); });

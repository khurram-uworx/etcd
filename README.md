# etcd: Distributed Coordination in Modern .NET Applications

## The Importance of Coordination in Distributed Systems

In containerized and clustered environments, multiple processes running across different machines must coordinate their work reliably. Without a centralized coordination service, distributed systems face challenges such as:

- **Split-brain scenarios** where independent processes make conflicting decisions
- **Race conditions** when multiple nodes attempt the same operation simultaneously
- **Inability to reach consensus** on system state or leadership

**Zookeeper** and **etcd** are widely adopted solutions that provide atomic operations, leases, watch streams, and linearizable reads—essential primitives for building reliable distributed coordination.

## etcd: Why .NET Developers Are Adopting It

**etcd** is a distributed, reliable key-value store that powers cloud-native infrastructure. Originally built by CoreOS, it became the backbone of **Kubernetes** and is now the de facto standard for distributed coordination.

### Why etcd Appeals to .NET Developers

1. **Cloud-Native Foundation**: etcd is integral to Kubernetes, making it essential for containerized .NET microservices
2. **Simple but Powerful API**: gRPC-based API with straightforward operations (Get, Put, Delete, Watch, Transactions)
3. **Linearizable Consistency**: Guarantees strong consistency across all operations—critical for mission-critical .NET applications
4. **Production-Ready**: Used by Kubernetes, CoreOS, and thousands of enterprises; battle-tested under scale
5. **Easy .NET Integration**: Libraries like `dotnet-etcd` provide idiomatic C# bindings
6. **Cost-Effective**: Open source; can run on standard infrastructure without specialized hardware
7. **Built-in Mechanisms**: Leases for auto-cleanup, transactions for atomic multi-key operations, and watches for reactive patterns

## Higher-Order Constructs: Building Barriers with etcd

Using etcd's atomic operations, leases, and watch functionality, we can implement sophisticated synchronization primitives. A **barrier** is one such construct—a synchronization mechanism where multiple distributed processes **must wait until all participants arrive**, then proceed together.

### Barrier Concept

A barrier ensures coordinated execution across distributed workers:

```
N workers → Barrier → All proceed together
```

### Design Pattern

#### Key Structure in etcd
```
/barriers/<barrier_id>/
  ├─ workers/
  │  ├─ node1
  │  ├─ node2
  │  └─ node3
  ├─ release
  └─ timeout (optional)
```

#### Workflow

1. **Worker Registration**: Each worker creates a key under the barrier path, typically attached to a lease
   ```
   PUT /barriers/job1/workers/node-1 → "ready"
   ```

2. **Participant Counting**: Workers check how many nodes are registered
   ```
   GET /barriers/job1/workers/ --prefix
   ```

3. **Barrier Release**: When count reaches N, create a release signal (using atomic transactions to prevent race conditions)
   ```
   PUT /barriers/job1/release → "go"
   ```

4. **Proceed**: All watching workers detect the release and continue execution

### Two Barrier Approaches

#### Approach 1: Count-Based Barrier
Wait until exactly N workers arrive, then release.
- **Best for**: Fixed-size groups, job parallelization
- **Atomic Release**: Use etcd transactions to prevent multiple simultaneous releases
  ```
  IF count(/barriers/job1/workers/*) == N
  THEN PUT /barriers/job1/release
  ```

#### Approach 2: Time-Based Barrier
Workers can enroll only before a deadline. Once the deadline passes, the barrier automatically closes and processing begins with available nodes.

**Key Structure**:
```
/barriers/{barrier_id}/
  ├─ meta (creation time, timeout)
  ├─ deadline
  ├─ state (open | closed)
  └─ members/{node_id}
```

**Enrollment Transaction**:
```
IF state == "open" AND current_time < deadline
THEN PUT /barriers/B/members/{node_id}
ELSE fail
```

**Advantages**:
- No deadlock if workers crash or never arrive
- Non-blocking; proceeds after timeout regardless
- Prevents late joiners automatically
- Ideal for elastically scaled systems where node count varies

### Production Enhancements

1. **Leases**: Attach worker keys to leases for automatic cleanup on failure
   ```csharp
   lease = client.Lease(ttl: 10);
   client.Put(key, value, lease: lease);
   ```

2. **Timeout Protection**: Avoid indefinite waits with deadline-based barriers

3. **Cleanup**: Delete barrier keys after completion to reclaim resources
   ```
   DELETE /barriers/{barrier_id}/
   ```

4. **Version Fencing**: Use `mod_revision` in transactions to prevent race conditions

## Implementation

The `Barrier/Etcd.cs` class provides a time-based barrier implementation using the dotnet-etcd client. Key methods include:

- `CreateBarrierAsync()`: Initializes a barrier with deadline and state
- `EnrollAsync()`: Registers a node if the barrier is open and deadline hasn't passed
- `GetMemberCountAsync()`: Returns current enrollment count
- `CloseBarrierAsync()`: Marks the barrier as closed
- `WaitForBarrierAsync()`: Polls for barrier state changes with timeout

See the code for details on deadline handling and state management.

---

**References**:
- [etcd Official Documentation](https://etcd.io)
- [dotnet-etcd GitHub](https://github.com/shubhamrathod/dotnet-etcd)
- [Kubernetes etcd Architecture](https://kubernetes.io/docs/tasks/administer-cluster/configure-upgrade-etcd/)
- [etcd with .NET](https://astordev.github.io/articles/dotnet-etcd)
- [Barrier class in BCL](https://learn.microsoft.com/en-us/dotnet/api/system.threading.barrier)


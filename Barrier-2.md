We can also have **time-bounded barrier** instead of a **count-based barrier**. With etcd you can implement this using a **barrier key + enrollment keys + a deadline**.

Idea:

* Nodes can **enroll only before a deadline**
* After the deadline, the barrier **closes automatically**
* The system proceeds with **whatever nodes enrolled**
* Late nodes **cannot enroll**

Below is a typical design pattern.

---

# 1. Barrier Data Layout in etcd

Example keys:

```
/barriers/{barrier_id}/meta
/barriers/{barrier_id}/deadline
/barriers/{barrier_id}/members/{node_id}
/barriers/{barrier_id}/state
```

Example values:

```
meta:
{
  "created_at": 1710000000,
  "timeout_sec": 30
}

deadline:
1710000030

members/node-1
members/node-2

state: open | closed
```

---

# 2. Barrier Creation

A coordinator (or first node) creates the barrier.

Pseudo-code:

```python
deadline = now() + TIMEOUT

txn:
  if key "/barriers/B/meta" does not exist
  then
     put "/barriers/B/meta" = {...}
     put "/barriers/B/deadline" = deadline
     put "/barriers/B/state" = "open"
```

Important:
Use **etcd transactions** so only **one creator wins**.

---

# 3. Node Enrollment

Each worker attempts to enroll.

Steps:

1. Read deadline
2. Check current time
3. Add membership only if barrier still open

Transaction:

```
IF
  state == "open"
  AND current_time < deadline
THEN
  put /barriers/B/members/{node_id}
ELSE
  fail
```

If the deadline has passed, the node **must not enroll**.

---

# 4. Closing the Barrier (Time Based)

A watcher or leader closes the barrier once deadline passes.

Options:

### Option A — Timer loop

Leader periodically checks:

```
if now() >= deadline:
   set state = "closed"
```

Transaction:

```
IF state == "open"
THEN put state = "closed"
```

---

### Option B — Watch deadline

Workers watch:

```
/barriers/B/state
```

When it changes to `closed`, they proceed.

---

# 5. Worker Wait Logic

Workers:

```
1 enroll
2 watch barrier state
3 when state == closed → proceed
```

Pseudo-code:

```python
enrolled = try_enroll()

if not enrolled:
    exit("missed barrier")

watch("/barriers/B/state")

wait until state == "closed"

continue_with_job()
```

---

# 6. Prevent Late Enrollment

This is guaranteed because:

Enrollment transaction checks both:

```
state == open
AND
now < deadline
```

Once either condition fails → enrollment rejected.

---

# 7. Example Timeline

```
t=0   barrier created
t=1   node1 enrolls
t=2   node2 enrolls
t=3   node3 enrolls
t=5   deadline reached
t=5   barrier closed

node4 tries enroll → rejected
```

Active nodes = `node1,node2,node3`.

---

# 8. Production Improvements

Common improvements used with etcd:

### 1. Use leases

Membership keys:

```
/members/{node_id}
```

attached to a **lease**.

If node dies → automatically removed.

---

### 2. Store member count

Optional:

```
/barriers/B/member_count
```

Updated via transaction.

---

### 3. Version fencing

Use **mod_revision** in transaction to avoid races.

---

# 9. Simpler Key Layout (Recommended)

Many systems use this simpler structure:

```
/barrier/B/start_time
/barrier/B/timeout
/barrier/B/state
/barrier/B/members/<node>
```

Workers:

```
if now < start_time + timeout:
    join
else:
    fail
```

---

# 10. Why Time-Based Barrier Works Well in etcd

Because etcd provides:

* **linearizable reads**
* **transactions**
* **watch streams**
* **leases**

Which make distributed coordination reliable.

---

✅ **Result:**
You get a **non-blocking distributed barrier** that:

* waits **T seconds**
* proceeds with **available nodes**
* rejects **late joiners**

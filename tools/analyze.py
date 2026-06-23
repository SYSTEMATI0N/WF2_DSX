import csv
import sys
import math
from collections import Counter

path = sys.argv[1]

rows = []
with open(path, newline="") as f:
    reader = csv.DictReader(f)
    for r in reader:
        rows.append(r)

n = len(rows)
print(f"rows={n}")


def f(r, k):
    try:
        return float(r[k])
    except (ValueError, KeyError):
        return 0.0


def i(r, k):
    try:
        return int(float(r[k]))
    except (ValueError, KeyError):
        return 0


wheels = ["fl", "fr", "rl", "rr"]
slip_keys = [f"{w}_slip_ratio" for w in wheels]

# Duration
elapsed = [i(r, "elapsed_ms") for r in rows]
print(f"duration_s={(elapsed[-1]-elapsed[0])/1000:.1f}")

# Speed buckets
def bucket(spd):
    if spd < 1: return "stationary(<1)"
    if spd < 5: return "crawl(1-5)"
    if spd < 20: return "low(5-20)"
    if spd < 60: return "mid(20-60)"
    return "high(>60)"

# Analyze slip == -1 saturation vs speed
print("\n=== slip ratio == -1 (or <=-0.99) frequency by speed bucket ===")
buckets = Counter()
buckets_neg1 = Counter()
for r in rows:
    spd = f(r, "speed_kmh")
    b = bucket(spd)
    buckets[b] += 1
    for k in slip_keys:
        if f(r, k) <= -0.99:
            buckets_neg1[(b, k)] += 1
for b in ["stationary(<1)","crawl(1-5)","low(5-20)","mid(20-60)","high(>60)"]:
    cnt = buckets[b]
    if cnt == 0: continue
    parts = []
    for k in slip_keys:
        pct = 100*buckets_neg1[(b,k)]/cnt
        parts.append(f"{k.split('_')[0]}={pct:.0f}%")
    print(f"{b:18s} frames={cnt:6d}  slip<=-0.99: {' '.join(parts)}")

# When is brake applied AND slip<=-0.99 AND moving? (false lockup risk while moving)
print("\n=== braking analysis ===")
brake_frames = [r for r in rows if f(r,"brake") > 0.05]
print(f"frames with brake>0.05: {len(brake_frames)}")
# current lockup metric: max(0, -slip)
def lockup(r):
    return max(0.0, max(-f(r,k) for k in slip_keys))
def lockup_moving(r):
    # only wheels, but consider speed
    return lockup(r)

# How many stationary frames would trigger lockup>0.1 under current logic regardless of brake
false_lockup_stationary = 0
stationary = 0
for r in rows:
    if f(r,"speed_kmh") < 1:
        stationary += 1
        if lockup(r) > 0.1:
            false_lockup_stationary += 1
print(f"stationary frames={stationary}, of which lockup>0.1 (current false trigger)={false_lockup_stationary} ({100*false_lockup_stationary/max(1,stationary):.0f}%)")

# ABS active distribution
abs_on = sum(1 for r in rows if i(r,"abs")==1)
tcs_on = sum(1 for r in rows if i(r,"tcs")==1)
print(f"\nABS active frames={abs_on} ({100*abs_on/n:.1f}%)  TCS active frames={tcs_on} ({100*tcs_on/n:.1f}%)")

# slip distribution while ABS active and moving
abs_moving = [r for r in rows if i(r,"abs")==1 and f(r,"speed_kmh")>5]
print(f"ABS active & speed>5: {len(abs_moving)}")
if abs_moving:
    vals = sorted(lockup(r) for r in abs_moving)
    print(f"  lockup percentiles: p50={vals[len(vals)//2]:.3f} p90={vals[int(len(vals)*0.9)]:.3f} max={vals[-1]:.3f}")

# Real lockup: braking, moving, no ABS, slip negative
print("\n=== potential REAL lockup (brake>0.2, speed>5, abs=0) ===")
real_lock_cand = [r for r in rows if f(r,"brake")>0.2 and f(r,"speed_kmh")>5 and i(r,"abs")==0]
print(f"candidates={len(real_lock_cand)}")
if real_lock_cand:
    vals = sorted(lockup(r) for r in real_lock_cand)
    print(f"  lockup percentiles: p50={vals[len(vals)//2]:.3f} p75={vals[int(len(vals)*0.75)]:.3f} p90={vals[int(len(vals)*0.9)]:.3f} p99={vals[int(len(vals)*0.99)]:.3f} max={vals[-1]:.3f}")
    over01 = sum(1 for v in vals if v>0.1)
    print(f"  lockup>0.1: {over01} ({100*over01/len(vals):.0f}%)")

# Wheelspin analysis: throttle>0.2, driven wheels positive slip
print("\n=== wheelspin (throttle>0.3, speed>2) ===")
spin_cand = [r for r in rows if f(r,"throttle")>0.3 and f(r,"speed_kmh")>2]
print(f"candidates={len(spin_cand)}")
def wheelspin_all(r):
    return max(0.0, max(f(r,k) for k in slip_keys))
if spin_cand:
    vals = sorted(wheelspin_all(r) for r in spin_cand)
    print(f"  wheelspin percentiles: p50={vals[len(vals)//2]:.3f} p90={vals[int(len(vals)*0.9)]:.3f} p99={vals[int(len(vals)*0.99)]:.3f} max={vals[-1]:.3f}")

# wheelspin at standstill / low throttle false positives
spin_stationary = sum(1 for r in rows if f(r,"speed_kmh")<1 and wheelspin_all(r)>0.1)
print(f"stationary frames with positive slip>0.1 (false wheelspin): {spin_stationary}")

# positive slip saturating at +1 when stationary
buckets_pos1 = Counter()
for r in rows:
    if f(r,"speed_kmh")<1:
        for k in slip_keys:
            if f(r,k)>=0.99:
                buckets_pos1[k]+=1
print(f"stationary slip>=0.99 counts: {dict(buckets_pos1)}")

# Collisions
print("\n=== collisions ===")
collision_times = sorted(set(i(r,"collision_ms") for r in rows if i(r,"collision_ms")>0))
print(f"distinct collision_ms values: {len(collision_times)}")
# detect changes in collision_ms
changes = 0
prev = None
events = []
for r in rows:
    c = i(r,"collision_ms")
    if prev is not None and c != prev and c>0:
        changes += 1
        events.append((i(r,"elapsed_ms"), c, f(r,"speed_kmh"), i(r,"health")))
    prev = c
print(f"collision_ms changes (events) = {changes}")
for e in events[:20]:
    print(f"  elapsed={e[0]} collision_ms={e[1]} speed={e[2]:.0f} health={e[3]}")

# health over time
healths = [i(r,"health") for r in rows]
print(f"\nhealth: start={healths[0]} min={min(healths)} max={max(healths)} end={healths[-1]}")

# accel magnitude distribution (for collision impact detection alternative)
def accel_mag(r):
    return math.sqrt(f(r,"accel_x")**2+f(r,"accel_y")**2+f(r,"accel_z")**2)
accels = sorted(accel_mag(r) for r in rows)
print(f"accel_mag p50={accels[len(accels)//2]:.1f} p90={accels[int(len(accels)*0.9)]:.1f} p99={accels[int(len(accels)*0.99)]:.1f} max={accels[-1]:.1f}")

# longitudinal accel (accel_z?) spikes
# engine running distribution
eng_off = sum(1 for r in rows if i(r,"engine_running")==0)
print(f"\nengine_running=0 frames={eng_off} ({100*eng_off/n:.1f}%)")

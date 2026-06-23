import csv, sys
from collections import Counter

path = sys.argv[1]
rows = list(csv.DictReader(open(path, newline="")))

def f(r,k):
    try: return float(r[k])
    except: return 0.0
def I(r,k):
    try: return int(float(r[k]))
    except: return 0

wheels = ["fl","fr","rl","rr"]

# surface type distribution overall and while moving
print("=== surface types (all / moving>20kmh) ===")
allc = Counter(); movc = Counter()
for r in rows:
    mv = f(r,"speed_kmh")>20
    for w in wheels:
        s = I(r,f"{w}_surface")
        allc[s]+=1
        if mv: movc[s]+=1
print("all:   ", dict(sorted(allc.items())))
print("moving:", dict(sorted(movc.items())))

def susp_rough(r):
    return max(abs(f(r,f"{w}_susp_vel_mps")) for w in wheels)
def susp_sum(r):
    return sum(abs(f(r,f"{w}_susp_vel_mps")) for w in wheels)

# roughness percentiles by speed bucket
print("\n=== suspension velocity roughness (max abs over wheels), moving frames ===")
def bucket(s):
    if s<5: return "0-5"
    if s<20: return "5-20"
    if s<40: return "20-40"
    if s<70: return "40-70"
    return "70+"
buckets={}
for r in rows:
    b=bucket(f(r,"speed_kmh"))
    buckets.setdefault(b,[]).append(susp_rough(r))
for b in ["0-5","5-20","20-40","40-70","70+"]:
    if b in buckets:
        v=sorted(buckets[b]); n=len(v)
        print(f"  {b:6s} n={n:5d} p50={v[n//2]:.3f} p75={v[int(n*0.75)]:.3f} p90={v[int(n*0.9)]:.3f} p99={v[int(n*0.99)]:.3f} max={v[-1]:.3f}")

# roughness by dominant surface type (moving)
print("\n=== roughness by surface (speed>20) ===")
bysurf={}
for r in rows:
    if f(r,"speed_kmh")<=20: continue
    # dominant surface = mode of 4 wheels
    s = Counter(I(r,f"{w}_surface") for w in wheels).most_common(1)[0][0]
    bysurf.setdefault(s,[]).append(susp_rough(r))
for s in sorted(bysurf):
    v=sorted(bysurf[s]); n=len(v)
    print(f"  surface {s}: n={n:5d} p50={v[n//2]:.3f} p90={v[int(n*0.9)]:.3f} p99={v[int(n*0.99)]:.3f} max={v[-1]:.3f}")

# vertical accel (accel_y is usually vertical? check magnitudes). Print accel component spreads
print("\n=== accel components (abs) percentiles, moving ===")
for axis in ["accel_x","accel_y","accel_z"]:
    v=sorted(abs(f(r,axis)) for r in rows if f(r,"speed_kmh")>20); n=len(v)
    print(f"  {axis}: p50={v[n//2]:.2f} p90={v[int(n*0.9)]:.2f} p99={v[int(n*0.99)]:.2f} max={v[-1]:.2f}")

# engine running prevalence while in motion (proxy: rpm>0 and speed>5)
eng_on = sum(1 for r in rows if I(r,"engine_running")==1)
moving = [r for r in rows if f(r,"speed_kmh")>5]
eng_on_moving = sum(1 for r in moving if I(r,"engine_running")==1)
print(f"\nengine_running: all={100*eng_on/len(rows):.0f}%  while moving>5kmh={100*eng_on_moving/max(1,len(moving)):.0f}%")

# brake usage while engine running (to confirm soft-brake-always case)
brk = [r for r in rows if f(r,"brake")>0.2]
brk_eng = sum(1 for r in brk if I(r,"engine_running")==1)
print(f"braking>0.2 frames={len(brk)}, of which engine_running={100*brk_eng/max(1,len(brk)):.0f}%")

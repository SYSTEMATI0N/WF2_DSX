import csv, sys

path = sys.argv[1]
rows = list(csv.DictReader(open(path, newline="")))

def f(r,k):
    try: return float(r[k])
    except: return 0.0
def I(r,k):
    try: return int(float(r[k]))
    except: return 0

# Rear-drive assumption for "driven" spin (matches most WF2 cars); show both.
def rear_spin(r):
    return max(0.0, max(f(r,"rl_slip_ratio"), f(r,"rr_slip_ratio")))
def front_spin(r):
    return max(0.0, max(f(r,"fl_slip_ratio"), f(r,"fr_slip_ratio")))

# 1) Parked-with-throttle artifact check: speed<1, throttle low vs high
print("=== stationary (speed<1) breakdown ===")
st = [r for r in rows if f(r,"speed_kmh")<1]
st_no_thr = [r for r in st if f(r,"throttle")<=0.12]
st_thr    = [r for r in st if f(r,"throttle")>0.12]
print(f"stationary total={len(st)}  throttle<=0.12={len(st_no_thr)}  throttle>0.12={len(st_thr)}")
print(f"  of throttle<=0.12: rear_spin>0.15 = {sum(1 for r in st_no_thr if rear_spin(r)>0.15)}")
print(f"  of throttle>0.12 : rear_spin>0.15 = {sum(1 for r in st_thr if rear_spin(r)>0.15)}  tcs_on={sum(1 for r in st_thr if I(r,'tcs'))}")

# 2) Launch windows: low speed (0-15 km/h) WITH throttle>0.4
print("\n=== launch-ish frames (speed 0-15 km/h, throttle>0.4, clutch<0.1) ===")
launch = [r for r in rows if f(r,"speed_kmh")<15 and f(r,"throttle")>0.4 and f(r,"clutch")<0.1]
print(f"count={len(launch)}")
if launch:
    rs = sorted(rear_spin(r) for r in launch)
    print(f"  rear_spin p50={rs[len(rs)//2]:.2f} p90={rs[int(len(rs)*0.9)]:.2f} max={rs[-1]:.2f}")
    print(f"  rear_spin>0.15 frames={sum(1 for r in launch if rear_spin(r)>0.15)} ({100*sum(1 for r in launch if rear_spin(r)>0.15)/len(launch):.0f}%)")
    print(f"  tcs active here={sum(1 for r in launch if I(r,'tcs'))} ({100*sum(1 for r in launch if I(r,'tcs'))/len(launch):.0f}%)")
    print(f"  speed<2 km/h (would be killed by 7km/h gate)={sum(1 for r in launch if f(r,'speed_kmh')<2)}")

# 3) Show an actual launch sequence: first time speed rises from ~0 with throttle
print("\n=== sample launch sequence ===")
prev_speed = None
shown = 0
for idx in range(1,len(rows)):
    r=rows[idx]; p=rows[idx-1]
    if f(p,"speed_kmh")<1 and f(r,"speed_kmh")>=1 and f(r,"throttle")>0.4:
        # print a window
        print(f"-- launch at elapsed={I(r,'elapsed_ms')} --")
        print("  ms     spd  thr  brk  gear tcs rearSpin frontSpin")
        for j in range(max(0,idx-2), min(len(rows), idx+10)):
            rr=rows[j]
            print(f"  {I(rr,'elapsed_ms'):7d} {f(rr,'speed_kmh'):4.0f} {f(rr,'throttle'):.2f} {f(rr,'brake'):.2f}  {I(rr,'gear'):>2} {I(rr,'tcs')}   {rear_spin(rr):.2f}    {front_spin(rr):.2f}")
        shown+=1
        if shown>=4: break

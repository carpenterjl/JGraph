"""Time import + initialize of jgprobe, optionally with runtime options."""
import sys
import time
t0 = time.time()
import jgprobe
if len(sys.argv) > 1:
    jgprobe.initialize_runtime(sys.argv[1:])
t1 = time.time()
h = jgprobe.initialize()
t2 = time.time()
r = h.addtwo(1.0, 2.0, nargout=1)
t3 = time.time()
print('opts=%r import=%.2f init=%.2f firstcall=%.2f result=%r' % (sys.argv[1:], t1 - t0, t2 - t1, t3 - t2, r))
h.terminate()

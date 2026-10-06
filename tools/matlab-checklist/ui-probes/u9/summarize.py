"""Prints, for one kind of u9_matrix.out (e.g. U.Knob), each property with the values it kept and
the refusals it gave, grouped: a reading aid for writing the property surface."""
import sys, re, collections

kind = sys.argv[1]
only = sys.argv[2:]
rows = collections.OrderedDict()
for line in open('u9_matrix.out', encoding='utf-8', errors='replace'):
    line = line.rstrip('\n')
    m = re.match(r'^(\S+?)\.(\w+) <- (.*?) : (.*)$', line)
    if not m or m.group(1) != kind:
        continue
    prop, val, res = m.group(2), m.group(3), m.group(4)
    if only and prop not in only:
        continue
    rows.setdefault(prop, []).append((val, res))
for prop, items in rows.items():
    print('== %s' % prop)
    groups = collections.OrderedDict()
    for val, res in items:
        key = res if res.startswith('ERR') else 'OK'
        groups.setdefault(key, []).append((val, res))
    for key, members in groups.items():
        if key == 'OK':
            for val, res in members:
                print('   %s -> %s' % (val[:60], res[:150]))
        else:
            print('   %s' % key[:230])
            print('        <= %s' % ' ; '.join(v[:28] for v, _ in members)[:600])

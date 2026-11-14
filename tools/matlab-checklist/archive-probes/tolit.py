import sys
for path in sys.argv[1:]:
    b = open(path, 'rb').read()
    nums = [str(x) for x in b]
    lines = []
    cur = ''
    for n in nums:
        if len(cur) + len(n) + 1 > 120:
            lines.append(cur.rstrip())
            cur = ''
        cur += n + ' '
    lines.append(cur.rstrip())
    print(f'% {path}: {len(b)} bytes')
    print('bytes = [' + ' ...\n    '.join(lines) + '];')

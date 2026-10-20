"""DS7b (designer plan): records the base of a custom component's saved object - what a
ComponentContainer subclass stores in a saved tree besides its own properties - from the
SpinnerGauge object the probe ds7_uac saved plain (rt/uac/out/tree_uac.mat, save -v7 of a tree
App Designer's DesignTimeProperties were added to), the way ds1b_base.py records each built-in
class's base. The class's own stored properties (Value, SetupCalls, UpdateCalls) and the struct of
its callback properties (SerializableCallbackPropertyFcns) are left out: the tree writer adds them
per instance from the design and the running class. Written to ds7_uac_base.json, which the writer
embeds under the class name matlab.ui.componentcontainer.ComponentContainer.
Usage: python ds7_base.py   (after ds7_uac has run)"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'ds1b'))
from ds1b_base import record  # noqa: E402
from ds1b_mcos import MatFile  # noqa: E402

OWN = {'Value', 'SetupCalls', 'UpdateCalls', 'SerializableCallbackPropertyFcns'}


def main():
    m = MatFile(os.path.join(HERE, 'rt', 'uac', 'out', 'tree_uac.mat'))
    oid = m.objects_of('SpinnerGauge')[0]
    base = record(m, oid)
    base['class'] = 'matlab.ui.componentcontainer.ComponentContainer'
    base['entries'] = [e for e in base['entries'] if e['n'].split('.')[-1] not in OWN and e['n'] not in OWN]
    base.pop('dynamic', None)
    out = os.path.join(HERE, 'ds7_uac_base.json')
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'release': '2025b', 'classes': [base]}, f, separators=(',', ':'))
    print(len(base['entries']), 'entries:', ', '.join(e['n'] for e in base['entries']))
    for e in base['entries']:
        print(' ', json.dumps(e)[:300])
    print(os.path.getsize(out), 'bytes')


if __name__ == '__main__':
    main()

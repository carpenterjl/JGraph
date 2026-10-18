"""DS1b (designer plan): from each rt/fresh/<Class>__ref.mat (ds1b_fresh.m) writes <Class>__blank.mat,
the same tree with every stored name of that class's component left out (its children kept), so
ds1b_compare('fresh') lists what a component loaded with nothing stored lacks. Usage: python ds1b_blank.py"""
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ds1b_mcos import MatFile  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
CHILDREN = 'matlab.ui.container.Container.SerializableChildren'

for path in sorted(glob.glob(os.path.join(HERE, 'rt', 'fresh', '*__ref.mat'))):
    short = os.path.basename(path).split('__')[0]
    m = MatFile(path)
    targets = [i for i, o in enumerate(m.table.objects) if i and m.table.class_name(o[0]).split('.')[-1] == short]
    lst = m.props(targets[-1])
    lst[:] = [t for t in lst if m.table.names[t[0]] == CHILDREN]
    m.write(path.replace('__ref.mat', '__blank.mat'))
    print(short, len(targets))

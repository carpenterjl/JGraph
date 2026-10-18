"""DS1b (designer plan): component trees written with stored names left out, for ds1b_reduced.m.
Reads the trees ds0_tree saved (../ds0/rt/tree) and writes each variant to rt/reduced as
<source>__<variant>.mat, beside <source>__id.mat, the tree rewritten whole (byte for byte the same).
Usage: python ds1b_reduce.py"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ds1b_mcos import MatFile  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', 'ds0', 'rt', 'tree')
OUT = os.path.join(HERE, 'rt', 'reduced')
os.makedirs(OUT, exist_ok=True)

# names every component saves that are no part of what the designer sets
PLUMBING = {'Internal', 'SerializableApplicationData', 'Copyable', 'CustomThemePropertyMappings',
            'appdesservices.internal.interfaces.model.AbstractModel.DirtyPropertyStrategy',
            'appdesservices.internal.interfaces.model.AbstractModel.DirtyProperties',
            'matlab.graphics.internal.GraphicsCoreProperties.TopLevelSerializedObject', 'BeingDeleted'}
FIGURE_KEEP = {'StoragePosition', 'StorageOuterPosition', 'PositionMode', 'NameMode', 'Name_I',
               'matlab.ui.container.Container.SerializableChildren'}


def keep(m, oid, wanted):
    """Keeps only the names of object oid for which wanted(name) is true."""
    o = m.table.objects[oid]
    lst = m.props(oid)
    lst[:] = [t for t in lst if wanted(m.table.names[t[0]])]


def variants(source, edit_sets):
    for variant, edit in edit_sets:
        m = MatFile(os.path.join(SRC, source + '.mat'))
        edit(m)
        m.write(os.path.join(OUT, f'{source}__{variant}.mat'))


def first(m, cls):
    return m.objects_of(cls)[0]


def main():
    sets = {}
    m = MatFile(os.path.join(SRC, 'one_button.mat'))
    button_names = m.prop_names(first(m, 'matlab.ui.control.Button'))
    sets['one_button'] = [('id', lambda m: None)]
    for n in button_names:
        short = n.split('.')[-1]
        sets['one_button'].append((f'button_drop_{short}', lambda m, n=n: keep(m, first(m, 'matlab.ui.control.Button'), lambda x: x != n)))
    sets['one_button'] += [
        ('button_none', lambda m: keep(m, first(m, 'matlab.ui.control.Button'), lambda x: False)),
        ('button_no_plumbing', lambda m: keep(m, first(m, 'matlab.ui.control.Button'), lambda x: x not in PLUMBING)),
        ('both_min', lambda m: (keep(m, first(m, 'matlab.ui.control.Button'), lambda x: x not in PLUMBING),
                                keep(m, first(m, 'matlab.ui.Figure'), lambda x: x in FIGURE_KEEP))),
    ]

    m = MatFile(os.path.join(SRC, 'empty_fig.mat'))
    figure_names = m.prop_names(first(m, 'matlab.ui.Figure'))
    sets['empty_fig'] = [('id', lambda m: None)]
    for n in figure_names:
        short = n.split('.')[-1]
        sets['empty_fig'].append((f'figure_drop_{short}', lambda m, n=n: keep(m, first(m, 'matlab.ui.Figure'), lambda x: x != n)))
    sets['empty_fig'] += [
        ('figure_none', lambda m: keep(m, first(m, 'matlab.ui.Figure'), lambda x: False)),
        ('figure_min', lambda m: keep(m, first(m, 'matlab.ui.Figure'), lambda x: x in FIGURE_KEEP)),
    ]

    def u7b_min(m):
        for oid, o in enumerate(m.table.objects):
            if not oid:
                continue
            cls = m.table.class_name(o[0])
            if cls == 'matlab.ui.Figure':
                keep(m, oid, lambda x: x in FIGURE_KEEP)
            elif cls.startswith('matlab.ui.control.') or cls.startswith('matlab.ui.container.'):
                keep(m, oid, lambda x: x not in PLUMBING)

    sets['u7b_dtp'] = [('id', lambda m: None), ('all_min', u7b_min)]

    for source, edits in sets.items():
        variants(source, edits)
        print(source, len(edits), 'files')


main()

"""DS1b (designer plan): the trees behind the rules R2025b's load follows for a stored name, written for
ds1b_rules.m into rt/rules as <source>__<variant>.mat beside <source>__ref.mat, the tree it is compared
with. Each variant starts from a tree R2025b saved (ds0_tree, ds1b_fresh, ds1b_setsaves) and
changes the stored names of one component:
  public_*      a public name stored in place of, or beside, the private one MATLAB saves;
  colour_*      a colour with and without its mode, against a button whose colour was set in code;
  order_*       Items and Value in either order, and a name stored twice;
  defaults_*    the same trees with every class's saved-time defaults (the last subsystem cell) empty;
  ticks_*       a gauge's MinorTicks and its mode, with the defaults empty.
Usage: python ds1b_rules.py"""
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ds1b_mcos import (MatFile, char_node, double_node, drop, empty_defaults, set_name,  # noqa: E402
                       set_word, value_node, blank)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'rt', 'rules')
TREE = os.path.join(HERE, '..', 'ds0', 'rt', 'tree')
FRESH = os.path.join(HERE, 'rt', 'fresh')
SAVES = os.path.join(HERE, 'rt', 'setsaves')
TEXT = 'matlab.ui.control.internal.model.mixin.MultilineTextComponent.PrivateText'


def make(source_path, source, variant, edit, cls):
    m = MatFile(source_path)
    oid = [i for i, o in enumerate(m.table.objects) if i and m.table.class_name(o[0]) == cls][-1]
    edit(m, oid)
    m.write(os.path.join(OUT, f'{source}__{variant}.mat'))


def main():
    os.makedirs(OUT, exist_ok=True)
    button = 'matlab.ui.control.Button'

    # public names, against the button as saved
    src = os.path.join(TREE, 'one_button.mat')
    shutil.copy(src, os.path.join(OUT, 'public__ref.mat'))
    for variant, edit in [
        ('text_in_place', lambda m, o: set_name(m, o, 'Text', char_node('Hello'), replace=TEXT)),
        ('text_beside', lambda m, o: set_name(m, o, 'Text', char_node('Hello'))),
        ('position', lambda m, o: set_name(m, o, 'Position', double_node([10, 20, 150, 30]))),
        ('fontsize', lambda m, o: set_name(m, o, 'FontSize', double_node([20]))),
        ('callback', lambda m, o: set_name(m, o, 'ButtonPushedFcn', char_node('GoPushed'))),
        ('unknown', lambda m, o: set_name(m, o, 'NoSuchName', char_node('x'))),
    ]:
        make(src, 'public', variant, edit, button)

    # a colour: the reference is a button whose BackgroundColor was set to red in code
    src = os.path.join(SAVES, 'button__fresh.mat')
    shutil.copy(os.path.join(SAVES, 'button__bg.mat'), os.path.join(OUT, 'colour__ref.mat'))
    red = lambda: double_node([1, 0, 0])
    for variant, edit in [
        ('value_in_place', lambda m, o: set_name(m, o, 'BackgroundColor', red(), replace='BackgroundColor')),
        ('value_and_word_mode', lambda m, o: (set_name(m, o, 'BackgroundColor', red(), replace='BackgroundColor'),
                                              set_word(m, o, 'BackgroundColorMode', 'manual'))),
        ('value_and_char_mode', lambda m, o: (set_name(m, o, 'BackgroundColor', red(), replace='BackgroundColor'),
                                              set_name(m, o, 'BackgroundColorMode', char_node('manual')))),
        ('second_value', lambda m, o: set_name(m, o, 'BackgroundColor', red())),
        ('defaults_value', lambda m, o: (empty_defaults(m), set_name(m, o, 'BackgroundColor', red(), replace='BackgroundColor'))),
        ('defaults_value_then_auto', lambda m, o: (empty_defaults(m), set_name(m, o, 'BackgroundColor', red(), replace='BackgroundColor'),
                                                   set_word(m, o, 'BackgroundColorMode', 'auto'))),
    ]:
        make(src, 'colour', variant, edit, button)

    # order and a name stored twice, against the fresh drop-down
    src = os.path.join(FRESH, 'DropDown__ref.mat')
    shutil.copy(src, os.path.join(OUT, 'order__ref.mat'))
    dd = 'matlab.ui.control.DropDown'
    for variant, entries in [
        ('items_then_value', [('Items', ['A', 'B']), ('Value', 'B')]),
        ('value_then_items', [('Value', 'B'), ('Items', ['A', 'B'])]),
        ('value_twice', [('Value', 'Option 2'), ('Items', ['A', 'B']), ('Value', 'B')]),
    ]:
        def edit(m, o, entries=entries):
            empty_defaults(m)
            blank(m, o)
            set_name(m, o, 'Position', double_node([100, 100, 100, 22]))
            for name, v in entries:
                lst = m.props(o)
                from ds1b_mcos import add_value
                lst.append([m.table.name_index(name), 1, add_value(m, value_node(v))])
        make(src, 'order', variant, edit, dd)

    # every class's saved-time defaults left out, the trees otherwise whole
    for name, path, cls in [('defaults_u7b', os.path.join(TREE, 'u7b_dtp.mat'), 'matlab.ui.Figure'),
                            ('defaults_hyperlink', os.path.join(FRESH, 'Hyperlink__ref.mat'), 'matlab.ui.control.Hyperlink')]:
        shutil.copy(path, os.path.join(OUT, f'{name}__ref.mat'))
        make(path, name, 'empty', lambda m, o: empty_defaults(m), cls)

    # a gauge's MinorTicks and its mode, defaults empty
    src = os.path.join(FRESH, 'SemicircularGauge__ref.mat')
    shutil.copy(src, os.path.join(OUT, 'ticks__ref.mat'))
    g = 'matlab.ui.control.SemicircularGauge'
    for variant, edit in [
        ('none', lambda m, o: None),
        ('empty', lambda m, o: set_name(m, o, 'MinorTicks', double_node([]))),
        ('values', lambda m, o: set_name(m, o, 'MinorTicks', double_node([5, 15]))),
        ('private_mode', lambda m, o: set_word(m, o, 'PrivateMinorTicksMode', 'manual')),
    ]:
        make(src, 'ticks', variant, lambda m, o, edit=edit: (empty_defaults(m), edit(m, o)), g)
    print('written to', OUT)


if __name__ == '__main__':
    main()

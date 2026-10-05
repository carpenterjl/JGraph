% U6 of the app-building plan (ADR 0203): a class that inherits from another class. A value
% hierarchy three deep, a handle hierarchy three deep, two superclasses at once, and the class
% definitions R2025b refuses.
global vlog_text
vlog_text = '';

% --- a value hierarchy ---------------------------------------------------------------------------
s = U6Shape;
u2_chk('shape_ctor_log', @() u6_log());
sq = U6Square(3);
u2_chk('square_ctor_log', @() u6_log());
u2_chk('square_class', @() class(sq));
u2_chk('square_name', @() sq.Name);
u2_chk('square_sides', @() sq.Sides);
u2_chk('square_side', @() sq.Side);
u2_chk('square_area_fn', @() area(sq));
u2_chk('square_area_dot', @() sq.area);
u2_chk('square_area_call', @() sq.area());
u2_chk('square_describe', @() describe(sq));
u2_chk('square_via_dot', @() sq.viaDot());
u2_chk('square_whoami', @() whoami(sq));
u2_chk('shape_describe', @() describe(s));
u2_chk('square_static', @() U6Square.make());
u2_chk('square_const', @() U6Square.Kind);
u2_chk('square_const_instance', @() sq.Kind);
u2_chk('square_isa_self', @() isa(sq, 'U6Square'));
u2_chk('square_isa_super', @() isa(sq, 'U6Shape'));
u2_chk('square_isa_handle', @() isa(sq, 'handle'));
u2_chk('square_isa_other', @() isa(sq, 'U6Base'));
u2_chk('shape_isa_sub', @() isa(s, 'U6Square'));
u2_chk('square_isobject', @() isobject(sq));
u2_chk('square_supers', @() superclasses(sq)');
u2_chk('square_supers_name', @() superclasses('U6Square')');
u2_chk('shape_supers', @() superclasses(s)');
u2_chk('square_props', @() properties(sq)');
u2_chk('square_props_name', @() properties('U6Square')');
u2_chk('square_methods', @() sort(methods(sq))');
u2_chk('square_fieldnames', @() fieldnames(sq)');
u2_chk('square_isprop_inherited', @() isprop(sq, 'Name'));
u2_chk('square_isprop_own', @() isprop(sq, 'Side'));
u2_chk('square_isprop_constant', @() isprop(sq, 'Kind'));
u2_chk('square_isprop_none', @() isprop(sq, 'Nope'));
u2_chk('square_ismethod_inherited', @() ismethod(sq, 'whoami'));
u2_chk('square_ismethod_static', @() ismethod(sq, 'make'));
u2_chk('square_ismethod_ctor', @() ismethod(sq, 'U6Square'));
u2_chk('square_ismethod_super_ctor', @() ismethod(sq, 'U6Shape'));
shown = evalc('disp(sq)');
u2_chk('square_disp', @() u6_disp(shown));
mc = metaclass(sq);
u2_chk('square_metaclass', @() mc.Name);

sq2 = sq;
sq2.Side = 9;
u2_chk('value_copy_kept', @() sq.Side);
sq2.Name = 'renamed';
u2_chk('inherited_write', @() sq2.Name);
u2_chk('inherited_write_kept', @() sq.Name);
sq3 = sq + sq2;
u2_chk('plus_class', @() class(sq3));
u2_chk('plus_sides', @() sq3.Sides);
u2_chk('isequal_same', @() isequal(U6Square(2), U6Square(2)));
u2_chk('isequal_different', @() isequal(U6Square(2), U6Square(3)));
u2_chk('square_too_many', @() U6Square(1, 2));
u2_chk('unknown_read', @() sq.Nope);
u2_chk('unknown_write', @() u6_setp(sq, 'Nope', 1));
u2_chk('unknown_method', @() nope(sq));

% --- a subclass with no constructor of its own ---------------------------------------------------
vlog_text = '';
c = U6Cube;
u2_chk('cube_ctor_log', @() u6_log());
u2_chk('cube_class', @() class(c));
u2_chk('cube_area', @() area(c));
u2_chk('cube_describe', @() describe(c));
u2_chk('cube_whoami', @() whoami(c));
u2_chk('cube_supers', @() superclasses(c)');
u2_chk('cube_props', @() properties(c)');
u2_chk('cube_isa_root', @() isa(c, 'U6Shape'));
u2_chk('cube_with_argument', @() u6_getp(U6Cube(5), 'Side'));
u2_chk('cube_with_argument_log', @() u6_log());
u2_chk('cube_with_two', @() U6Cube(5, 6));
vlog_text = '';

% --- a property typed with a class takes a subclass ----------------------------------------------
h = U6Holder;
h.Item = U6Square(2);
u2_chk('typed_takes_subclass', @() class(h.Item));
u2_chk('typed_refuses_other', @() u6_setp(h, 'Item', U6Mid));
u2_chk('typed_refuses_number', @() u6_setp(h, 'Item', 5));

% --- a handle hierarchy --------------------------------------------------------------------------
vlog_text = '';
b = U6Base(7);
u2_chk('base_log', @() u6_log());
m = U6Mid;
u2_chk('mid_log', @() u6_log());
l = U6Leaf('x');
u2_chk('leaf_log', @() u6_log());
u2_chk('leaf_tag', @() tag(l));
u2_chk('leaf_l', @() l.L);
u2_chk('leaf_isa_handle', @() isa(l, 'handle'));
u2_chk('leaf_isa_root', @() isa(l, 'U6Base'));
u2_chk('leaf_supers', @() superclasses(l)');
u2_chk('leaf_props', @() properties(l)');
u2_chk('leaf_events', @() events(l)');
u2_chk('leaf_ismethod_delete', @() ismethod(l, 'delete'));
u2_chk('leaf_ismethod_isvalid', @() ismethod(l, 'isvalid'));
l.bump();
bump(l);
u2_chk('leaf_inherited_mutator', @() l.Id);
l2 = l;
l2.M = 'changed';
u2_chk('leaf_alias', @() l.M);
u2_chk('leaf_eq_alias', @() l == l2);
u2_chk('leaf_eq_other', @() l == m);
u2_chk('leaf_ne_other', @() l ~= m);
lh = addlistener(l, 'Ping', @(~, ~) vlog('ping'));
fire(l);
l.fireFromLeaf();
u2_chk('leaf_event_log', @() u6_log());
delete(l);
u2_chk('leaf_delete_log', @() u6_log());
u2_chk('leaf_valid', @() isvalid(l));
u2_chk('leaf_dead_read', @() l.M);
delete(m);
u2_chk('mid_delete_log', @() u6_log());
delete(b);
u2_chk('base_delete_log', @() u6_log());
a = U6Arg(4);
u2_chk('arg_log', @() u6_log());
u2_chk('arg_id', @() a.Id);
u2_chk('arg_tag', @() a.tag());
delete(a);
vlog_text = '';
u6_scope();
u2_chk('scope_log', @() u6_log());

% --- two superclasses ----------------------------------------------------------------------------
bo = U6Both;
u2_chk('both_log', @() u6_log());
u2_chk('both_from_a', @() fromA(bo));
u2_chk('both_from_b', @() bo.fromB());
u2_chk('both_supers', @() superclasses(bo)');
u2_chk('both_props', @() properties(bo)');
u2_chk('both_isa_a', @() isa(bo, 'U6MixA'));
u2_chk('both_isa_b', @() isa(bo, 'U6MixB'));
u2_chk('both_isa_handle', @() isa(bo, 'handle'));

% --- definitions that are refused ----------------------------------------------------------------
u2_chk('bad_mix', @() U6BadMix);
u2_chk('sealed_base_itself', @() class(U6SealedBase));
u2_chk('sub_of_sealed', @() U6SubOfSealed);
u2_chk('duplicate_property', @() U6DupProp);
u2_chk('missing_superclass', @() U6NoSuper);
u2_chk('sealed_method_inherited', @() locked(U6SealUse));
u2_chk('sealed_method_overridden', @() U6SealOver);
u2_chk('method_clash', @() U6Clash);

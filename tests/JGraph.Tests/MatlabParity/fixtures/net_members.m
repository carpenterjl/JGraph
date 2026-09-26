% net_members.m -- .NET members in full (interop plan, stage 2): ref, out and params parameters,
% optional arguments, void against value returns, the indexer, writes to read-only and static
% members, NET.setStaticProperty, and the writes MATLAB refuses.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

% ---- ref / out: the modified arguments come back as extra outputs, after the return value
ix_chk('ref_one_output', JGTest.Modifiers.Double(2));
ix_chk('ref_two_outputs', ix_id(@() two(@() JGTest.Modifiers.Double(2))));
[w, f] = JGTest.Modifiers.Split(2.75);
ix_chk('out_split', [w f]);
w1 = JGTest.Modifiers.Split(2.75);
ix_chk('out_split_one', w1);
[ok, h] = JGTest.Modifiers.TryHalf(int32(8));
ix_chk('out_tryhalf_ok', ok);
ix_chk('out_tryhalf_half', h);
[ok, h] = JGTest.Modifiers.TryHalf(int32(7));
ix_chk('out_tryhalf_odd', {ok, h});
[r, a1, lbl] = JGTest.Modifiers.RefOutReturn(1);
ix_chk('refoutret', {r, a1, lbl});
g = JGTest.Modifiers.Grow([1 2 3]);
ix_chk('ref_array_class', class(g));
ix_chk('ref_array_values', double(g));
ix_chk('out_only', JGTest.Modifiers.MakeName());

% ---- params: an array, never loose arguments
ix_chk('params_array', JGTest.Modifiers.Sum([1 2 3]));
ix_chk('params_netarray', JGTest.Modifiers.Sum(NET.convertArray([1 2 3])));
ix_chk('params_scalars_refused', ix_id(@() JGTest.Modifiers.Sum(1, 2, 3)));
ix_chk('params_none_refused', ix_id(@() JGTest.Modifiers.Sum()));
ix_chk('params_cellstr', JGTest.Modifiers.Join('-', {'a', 'b'}));
ix_chk('params_loose_strings_refused', ix_id(@() JGTest.Modifiers.Join('-', 'a', 'b')));
ix_chk('params_ctor_array', JGTest.Members(int32([1 2 3])).Name);
ix_chk('params_ctor_loose_refused', ix_id(@() JGTest.Members(int32(1), int32(2), int32(3))));

% ---- optional parameters
ix_chk('optional_1', JGTest.Modifiers.Optional(int32(1)));
ix_chk('optional_2', JGTest.Modifiers.Optional(int32(1), int32(2)));
ix_chk('optional_3', JGTest.Modifiers.Optional(int32(1), int32(2), int32(3)));
ix_chk('optional_Missing', JGTest.Modifiers.Optional(int32(1), System.Reflection.Missing.Value, int32(3)));
ix_chk('optional_ctor_1', JGTest.OptionalCtor(1).B);
ix_chk('optional_ctor_2', JGTest.OptionalCtor(1, 9).B);

% ---- void
ix_chk('void_output_refused', ix_id(@() one(@() JGTest.Modifiers.Void())));
ix_chk('void_call', ix_id(@() JGTest.Modifiers.Void()));
ix_chk('void_instance_output_refused', ix_id(@() one(@() JGTest.Members().Bump())));

% ---- the indexer
m = JGTest.Members(2, 'm');
ix_chk('Item_get', m.Item(0));
ix_chk('Item_int32', m.Item(int32(1)));
ix_chk('Item_out_of_range', ix_id(@() m.Item(5)));
ix_chk('Item_set_refused', ix_msg(@() evalin_item_set(m)));

% ---- refused writes
ix_chk('set_readonly_prop', ix_id(@() setprop(m, 'ReadOnly', 1)));
ix_chk('set_readonly_prop_message', ix_msg(@() setprop(m, 'ReadOnly', 1)));
ix_chk('set_readonly_field', ix_id(@() setprop(m, 'ReadOnlyField', 'x')));
ix_chk('set_wrong_type_char', ix_id(@() setprop(m, 'Value', 'text')));
ix_chk('set_wrong_type_string', ix_id(@() setprop(m, 'Value', "7")));
ix_chk('get_writeonly', ix_id(@() m.WriteOnly));
m.WriteOnly = 11;
ix_chk('writeonly_effect', m.Value);
ix_chk('set_nosuch', ix_id(@() setprop(m, 'NoSuch', 1)));
m.Field = 9;
ix_chk('set_field', m.Field);

% ---- statics: read through the type, call through an instance, write only through the function
JGTest.Members.Reset();
ix_chk('static_read', JGTest.Members.Counter);
ix_chk('static_call', JGTest.Members.Increment());
ix_chk('static_call_noparens', JGTest.Members.Increment);
ix_chk('static_via_instance', m.Increment());
NET.setStaticProperty('JGTest.Members.StaticField', 8);
ix_chk('setStaticProperty_field', JGTest.Members.StaticField);
NET.setStaticProperty('JGTest.Members.Counter', int32(9));
ix_chk('setStaticProperty_prop', JGTest.Members.Counter);
ix_chk('setStaticProperty_readonly', ix_id(@() NET.setStaticProperty('JGTest.Members.StaticReadOnly', 'x')));
ix_chk('setStaticProperty_nosuch', ix_id(@() NET.setStaticProperty('JGTest.Members.NoSuch', 1)));
ix_chk('static_void_output_refused', ix_id(@() one(@() JGTest.Statics.Nothing())));
ix_chk('static_fn_syntax_refused', ix_id(@() Twice(JGTest.Statics, 4)));
ix_chk('static_display_refused', ix_id(@() evalc('JGTest.Statics')));

% ---- A.B.C = v with no variable A makes a struct variable A, which then hides the namespace
ix_chk('static_assign_makes_struct', ix_show(static_assign()));

% ---- interfaces, nested types, operators, IDisposable
gr = JGTest.Greeter();
ix_chk('iface_public', gr.Public());
ix_chk('iface_explicit_hidden', ix_id(@() gr.Greet()));
ix_chk('iface_cast', NET.explicitCast(gr, 'JGTest.IGreeter').Greet());
ix_chk('iface_cast_class', class(NET.explicitCast(gr, 'JGTest.IGreeter')));
ix_chk('nested_class', class(JGTest.Outer.MakeInner()));
ix_chk('nested_call', JGTest.Outer.MakeInner().Where());
ix_chk('nested_ctor_refused', ix_id(@() JGTest.Outer.Inner()));
v1 = JGTest.Vector2(1, 2);
v2 = JGTest.Vector2(3, 4);
ix_chk('op_plus', v1 + v2);
ix_chk('op_minus', v1 - v2);
ix_chk('op_times', v1 * 2);
ix_chk('op_times_reversed_refused', ix_id(@() 2 * v1));
ix_chk('op_eq', v1 == JGTest.Vector2(1, 2));
ix_chk('op_ne', v1 ~= v2);
ix_chk('op_Addition', JGTest.Vector2.op_Addition(v1, v2));
JGTest.Resource.ResetCount();
rs = JGTest.Resource();
rs.Dispose();
ix_chk('dispose_explicit', JGTest.Resource.Disposed);
rs2 = JGTest.Resource();
delete(rs2);
ix_chk('delete_does_not_dispose', JGTest.Resource.Disposed);
ix_chk('delete_keeps_variable', exist('rs2', 'var'));
sq = JGTest.Sequence(int32(3));
ix_chk('ienumerable_for_refused', ix_id(@() forloop(sq)));

function two(f)
[~, ~] = f();
end

function one(f)
x = f(); %#ok<NASGU>
end

function setprop(obj, name, v)
obj.(name) = v;
end

function evalin_item_set(m)
m.Item(0) = 99;
end

function s = static_assign()
JGTest.Members.StaticField = 4;
s = JGTest;
end

function forloop(sq)
for x = sq
    disp(x);
end
end

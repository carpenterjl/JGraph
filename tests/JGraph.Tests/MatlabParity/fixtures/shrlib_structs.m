% shrlib_structs.m -- C structs, enums and unsupported declarations through calllib (interop plan,
% stage 9): a MATLAB struct or a libstruct passed by value and by pointer, libstruct defaults,
% fields, sizes and copies, nested and packed structs, enum arguments by name and by value, and what
% R2025b does with a union, a bitfield, a function pointer, varargs and double***.
%
% R2025b passes NULL for a libstruct whose type has an array field (jg_mixed) or non-default packing
% (jg_packed); the test library answers NaN or leaves the struct untouched for NULL rather than
% crash. JGraph passes the struct and reads its array fields in their own class (ADR 0182): those
% rows are div=ADR0182, and so is the echo, whose layout is JGraph's.

p = interop_paths();
addpath(p.root);
lib = 'jgtestlib';
if libisloaded(lib), unloadlibrary(lib); end
loadlibrary(p.lib, @jgtestlib_proto);

% ---- by value
ix_chk('byval_struct', calllib(lib, 'jg_point_len', struct('x', 3, 'y', 4)));
ix_chk('byval_libstruct', calllib(lib, 'jg_point_len', libstruct('jg_point', struct('x', 3, 'y', 4))));
ix_chk('byval_partial_struct', calllib(lib, 'jg_point_len', struct('x', 3)));
ix_chk('byval_extra_field', ix_id(@() calllib(lib, 'jg_point_len', struct('x', 3, 'y', 4, 'z', 5))));
ix_chk('byval_extra_field_message', ix_msg(@() calllib(lib, 'jg_point_len', struct('x', 3, 'y', 4, 'z', 5))));
ix_chk('byval_wrong_case', ix_id(@() calllib(lib, 'jg_point_len', struct('X', 3, 'y', 4))));

% ---- by pointer
ix_chk('byptr_struct_output', ix_show(calllib(lib, 'jg_point_scale', struct('x', 1, 'y', 2), 10)));
r = calllib(lib, 'jg_point_scale', struct('x', 1, 'y', 2), 10);
ix_chk('byptr_struct_output_values', [r.x r.y]);
ls = libstruct('jg_point');
ix_chk('libstruct_class', class(ls));
ix_chk('libstruct_isa_handle', isa(ls, 'handle'));
ix_chk('libstruct_echo', ix_flat(evalc('ls')), 'div=ADR0182'); % JGraph's display layout
g = get(ls);
ix_chk('libstruct_default', [g.x g.y]);
ls.x = 1;
ls.y = 2;
calllib(lib, 'jg_point_scale', ls, 3);
ix_chk('libstruct_written_by_call', [ls.x ls.y]);
ix_chk('libstruct_structsize', ls.structsize);
ix_chk('libstruct_fieldnames', strjoin(fieldnames(ls)', ','));
ix_chk('libstruct_bad_field', ix_id(@() setfield_(ls, 'z', 1)));
ix_chk('libstruct_set_char', ix_id(@() setfield_(ls, 'x', 'a')));
ix_chk('libstruct_set_vector', ix_id(@() setfield_(ls, 'x', [1 2])));
ix_chk('libstruct_nosuch', ix_id(@() libstruct('jg_nosuch')));
gi = get(libstruct('jg_point', struct('x', 9)));
ix_chk('libstruct_initializer', [gi.x gi.y]);
l1 = libstruct('jg_point');
l2 = l1;
l2.x = 5;
ix_chk('libstruct_is_a_handle_copy', l1.x);

% ---- array fields, nesting, packing
m = libstruct('jg_mixed');
ix_chk('mixed_structsize', m.structsize);
ix_chk('mixed_default_d', ix_show(m.d), 'div=ADR0182'); % JGraph: int32 [0 0 0]
ix_chk('mixed_default_name', ix_show(m.name), 'div=ADR0182'); % JGraph: int8 zeros(1, 8)
calllib(lib, 'jg_mixed_fill', m);
ix_chk('mixed_fill_libstruct', ix_show(m.b), 'div=ADR0182'); % JGraph passes the struct
ix_chk('mixed_sum_libstruct', calllib(lib, 'jg_mixed_sum', m), 'div=ADR0182');
ix_chk('mixed_sum_struct', calllib(lib, 'jg_mixed_sum', struct('a', 1, 'b', 2, 'c', 3, 'd', [1 2 3], 'name', int8('ab'))));
ix_chk('mixed_name_char_refused', ix_id(@() calllib(lib, 'jg_mixed_sum', struct('a', 1, 'b', 2, 'c', 3, 'd', [1 2 3], 'name', 'ab'))));
n = libstruct('jg_nested');
ix_chk('nested_p_class', class(n.p));
ix_chk('nested_sum_struct', calllib(lib, 'jg_nested_sum', struct('p', struct('x', 1, 'y', 2), 'q', struct('x', 3, 'y', 4), 'id', 5)));
pk = libstruct('jg_packed');
ix_chk('packed_structsize', pk.structsize);
calllib(lib, 'jg_packed_fill', pk);
gp = get(pk);
ix_chk('packed_fill_libstruct', [gp.a gp.b gp.c], 'div=ADR0182'); % JGraph passes the struct
ix_chk('packed_sum_libstruct', calllib(lib, 'jg_packed_sum', pk), 'div=ADR0182');
ix_chk('packed_sum_struct', calllib(lib, 'jg_packed_sum', struct('a', 1, 'b', 2.5, 'c', 3)));
ppt = libpointer('jg_pointPtr');
calllib(lib, 'jg_point_alloc', ppt);
v = ppt.Value;
ix_chk('struct_out_ptr', [v.x v.y]);
ix_chk('struct_out_ptrptr_refused', ix_id(@() ptrptr_value(lib)));
ix_chk('layout', arrayfun(@(k) double(calllib(lib, 'jg_layout', k)), 0:13));

% ---- enums
ix_chk('enum_by_name', calllib(lib, 'jg_color_name', 'JG_GREEN'));
ix_chk('enum_by_value', calllib(lib, 'jg_color_name', 4));
ix_chk('enum_by_int32', calllib(lib, 'jg_color_name', int32(1)));
ix_chk('enum_by_string', calllib(lib, 'jg_color_name', "JG_BLUE"));
ix_chk('enum_bad_name', ix_id(@() calllib(lib, 'jg_color_name', 'JG_PURPLE')));
ix_chk('enum_bad_name_message', ix_msg(@() calllib(lib, 'jg_color_name', 'JG_PURPLE')));
ix_chk('enum_undeclared_value', calllib(lib, 'jg_color_name', 3));
ix_chk('enum_return', calllib(lib, 'jg_color_next', 'JG_RED'));
ix_chk('enum_return_class', class(calllib(lib, 'jg_color_next', 'JG_RED')));
ix_chk('enum_return_from_value', calllib(lib, 'jg_color_next', 2));
ix_chk('enum_to_int', calllib(lib, 'jg_color_value', 'JG_BLUE'));

% ---- unsupported declarations
ix_chk('union', ix_id(@() calllib(lib, 'jg_union_in', 1)));
ix_chk('union_message', ix_msg(@() calllib(lib, 'jg_union_in', 1)));
ix_chk('bitfield_libstruct', ix_id(@() libstruct('jg_bits')));
ix_chk('bitfield_struct', ix_id(@() calllib(lib, 'jg_bits_in', struct('low', 1, 'high', 2))));
ix_chk('fnptr_handle', ix_id(@() calllib(lib, 'jg_apply', @sin, 1)));
ix_chk('fnptr_null', ix_id(@() calllib(lib, 'jg_apply', libpointer, 1)));
ix_chk('varargs', ix_id(@() calllib(lib, 'jg_varsum', 2, 3, 4)));
ix_chk('triple_pointer', ix_id(@() calllib(lib, 'jg_triple', libpointer)));

% No unloadlibrary here: R2025b still counts outstanding libstruct objects after the named ones
% are cleared. Every shrlib fixture unloads the library first, and each recording is its own MATLAB.

function setfield_(s, name, v)
s.(name) = v;
end

function v = ptrptr_value(lib)
pp = libpointer('jg_pointPtrPtr');
calllib(lib, 'jg_point_alloc', pp);
v = pp.Value.Value;
end

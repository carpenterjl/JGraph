% shrlib_types.m -- calllib with every scalar C type (interop plan, stage 9). Each prim_<function>
% row lists what the function (x + 1, or !x for bool) returns for the same ten arguments: 5, 2.7,
% -3, 1e10, int8(5), true, 'A', [], [1 2], NaN. A scalar return comes back double whatever its C
% type (bool comes back logical); an argument rounds and saturates to the parameter's type; char,
% empty and vector arguments are refused. Then the rows past 2^53, mixed arguments, void, and the
% refusals of calllib itself.

p = interop_paths();
addpath(p.root);
lib = 'jgtestlib';
if libisloaded(lib), unloadlibrary(lib); end
loadlibrary(p.lib, @jgtestlib_proto);

args = {5, 2.7, -3, 1e10, int8(5), true, 'A', [], [1 2], NaN};
prims = {'jg_int8','jg_uint8','jg_int16','jg_uint16','jg_int32','jg_uint32','jg_int64','jg_uint64', ...
    'jg_float','jg_double','jg_char','jg_schar','jg_uchar','jg_short','jg_ushort','jg_int', ...
    'jg_uint','jg_long','jg_ulong','jg_longlong','jg_ulonglong','jg_size','jg_bool'};
for k = 1:numel(prims)
    got = cell(1, numel(args));
    for a = 1:numel(args)
        got{a} = ix_try(@() calllib(lib, prims{k}, args{a}));
    end
    ix_chk(['prim_' prims{k}], strjoin(got, ','));
end

ix_chk('int8_max_wraps', calllib(lib, 'jg_int8', int8(127)));
ix_chk('uint64_max_in', ix_show(calllib(lib, 'jg_uint64', intmax('uint64'))));
ix_chk('int64_big_ret', sprintf('%d', calllib(lib, 'jg_int64_big')));
ix_chk('int64_big_ret_class', class(calllib(lib, 'jg_int64_big')));
ix_chk('uint64_max_ret', sprintf('%d', calllib(lib, 'jg_uint64_max')));
ix_chk('mixed_args', calllib(lib, 'jg_mixed_args', 1, 2, 3, 4, 5));
ix_chk('mixed_args_classes', calllib(lib, 'jg_mixed_args', int16(1), int32(2), 3, single(4), int64(5)));
ix_chk('void_call', ix_id(@() calllib(lib, 'jg_void')));
ix_chk('void_output', ix_try(@() calllib(lib, 'jg_void')));
ix_chk('too_few', ix_id(@() calllib(lib, 'jg_double')));
ix_chk('too_few_message', ix_msg(@() calllib(lib, 'jg_double')));
ix_chk('too_many', ix_id(@() calllib(lib, 'jg_double', 1, 2)));
ix_chk('nosuch_function', ix_id(@() calllib(lib, 'jg_nosuch')));
ix_chk('nosuch_function_message', ix_msg(@() calllib(lib, 'jg_nosuch')));
ix_chk('notfound_function', ix_id(@() calllib(lib, 'jg_not_exported', 1)));
ix_chk('struct_return_function', ix_id(@() calllib(lib, 'jg_point_make', 1, 2)), 'div=ADR0181'); % JGraph calls it
ix_chk('version', calllib(lib, 'jg_version'));
ix_chk('exported_variable_invisible', exist('jg_exported_value'));
ix_chk('char_arg_message', ix_msg(@() calllib(lib, 'jg_int8', 'A')));
ix_chk('vector_arg_message', ix_msg(@() calllib(lib, 'jg_int8', [1 2])));
ix_chk('nan_arg_message', ix_msg(@() calllib(lib, 'jg_int8', NaN)));
ix_chk('bool_nan_message', ix_msg(@() calllib(lib, 'jg_bool', NaN)));

unloadlibrary(lib);

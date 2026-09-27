% shrlib_pointers.m -- pointers through calllib (interop plan, stage 9): a MATLAB array passed to a
% T* parameter and returned as an output (the outputs are the return value, if any, then one per
% pointer argument), libpointer and its methods, NULL in its forms, memory the library allocates or
% keeps, pointers into static storage, C strings in and out, void* handles, and unloadlibrary
% refusing while a pointer is alive.

p = interop_paths();
addpath(p.root);
lib = 'jgtestlib';
if libisloaded(lib), unloadlibrary(lib); end
loadlibrary(p.lib, @jgtestlib_proto);

% ---- arrays to T*
ix_chk('void_fn_outputs_are_pointers', ix_show(calllib(lib, 'jg_scale_double', [1 2 3], 3, 2)));
ix_chk('void_fn_two_outputs_refused', ix_id(@() two(@() calllib(lib, 'jg_scale_double', [1 2 3], 3, 2))));
ix_chk('col_keeps_shape', ix_show(calllib(lib, 'jg_scale_double', [1; 2; 3], 3, 2)));
ix_chk('matrix_keeps_shape', ix_show(calllib(lib, 'jg_scale_double', [1 2; 3 4], 4, 2)));
ix_chk('double_to_int32ptr', ix_show(calllib(lib, 'jg_scale_int32', [1 2 3], 3, 2)));
ix_chk('int16_native', ix_show(calllib(lib, 'jg_scale_int16', int16([1 2 3]), 3, 2)));
ix_chk('int64_native', ix_show(calllib(lib, 'jg_scale_int64', int64([1 2 3]), 3, 2)));
ix_chk('single_native', ix_show(calllib(lib, 'jg_scale_float', single([1 2 3]), 3, 2)));
ix_chk('char_to_uint8ptr_refused', ix_id(@() calllib(lib, 'jg_scale_uint8', 'abc', 3, 1)));
ix_chk('logical_to_boolptr', ix_try(@() calllib(lib, 'jg_negate_bool', [true false], 2)));
ix_chk('partial_n', ix_show(calllib(lib, 'jg_scale_double', [1 2 3], 2, 2)));
ix_chk('const_ptr_sum', calllib(lib, 'jg_sum', [1 2 3], 3));
ix_chk('const_ptr_scalar', calllib(lib, 'jg_sum', 5, 1));
ix_chk('const_ptr_empty', calllib(lib, 'jg_sum', [], 0));
[r, b] = calllib(lib, 'jg_add_ref', 1.5, 2, 3);
ix_chk('ret_then_pointer', {r, b});
ix_chk('array_2d_param', calllib(lib, 'jg_sum2d', [1 2 3; 4 5 6], 2));
ix_chk('array_2d_param_transposed', calllib(lib, 'jg_sum2d', [1 2 3; 4 5 6]', 2));

% ---- libpointer
lp = libpointer('doublePtr', [1 2 3]);
ix_chk('lp_class', class(lp));
ix_chk('lp_isa_handle', isa(lp, 'handle'));
ix_chk('lp_echo', ix_flat(evalc('lp')), 'div=ADR0182'); % JGraph's display layout
ix_chk('lp_DataType', lp.DataType);
ix_chk('lp_Value', lp.Value);
ix_chk('lp_isNull', isNull(lp));
ix_chk('lp_get', strjoin(fieldnames(get(lp))', ','));
ix_chk('lp_methods', strjoin(sort(methods(lp))', ','));
calllib(lib, 'jg_scale_double', lp, 3, 10);
ix_chk('lp_written_by_call', lp.Value);
lq = lp + 1;
ix_chk('lp_plus_class', class(lq));
ix_chk('lp_plus_Value', lq.Value);
ix_chk('lp_plus_DataType', lq.DataType);
lp.Value = [7 8];
ix_chk('lp_set_shorter', lp.Value);
lp.Value = int8([1 2]);
ix_chk('lp_set_int8_keeps_type', class(lp.Value));
ix_chk('lp_null_isNull', isNull(libpointer));
ix_chk('lp_null_DataType', libpointer().DataType);
ix_chk('lp_null_Value', ix_id(@() libpointer().Value));
ix_chk('lp_typed_null', isNull(libpointer('doublePtr')));
ix_chk('lp_typed_null_Value', ix_id(@() libpointer('doublePtr').Value));
ix_chk('lp_voidPtr', libpointer('voidPtr', [1 2]).Value);
ix_chk('lp_bad_type', ix_id(@() libpointer('nosuchPtr', 1)));
ix_chk('null_from_libpointer', calllib(lib, 'jg_is_null', libpointer));
ix_chk('null_from_empty', calllib(lib, 'jg_is_null', []));
ix_chk('zero_is_not_null', calllib(lib, 'jg_is_null', 0));
ix_chk('value_is_not_null', calllib(lib, 'jg_is_null', 5));

% ---- memory the library owns
ip0 = libpointer('doublePtr');
[n, ip1] = calllib(lib, 'jg_alloc_doubles', ip0, 4);
ix_chk('ptrptr_count', n);
ix_chk('ptrptr_out_class', class(ip1));
ix_chk('ptrptr_out_DataType', ip1.DataType);
setdatatype(ip1, 'doublePtr', 1, 4);
ix_chk('ptrptr_out_Value', ip1.Value);
ix_chk('ptrptr_in_DataType', ip0.DataType);
calllib(lib, 'jg_free', ip1);
pp = libpointer('doublePtrPtr');
[~, pp] = calllib(lib, 'jg_alloc_doubles', pp, 4);
ix_chk('ptrptr_typed_Value_refused', ix_id(@() pp.Value));
rp = calllib(lib, 'jg_alloc_ret', 3);
ix_chk('ret_ptr_class', class(rp));
ix_chk('ret_ptr_Value_before_setdatatype', ix_id(@() rp.Value));
setdatatype(rp, 'doublePtr', 1, 3);
ix_chk('ret_ptr_setdatatype', rp.Value);
reshape(rp, 3, 1);
ix_chk('ret_ptr_reshape', size(rp.Value));
calllib(lib, 'jg_free', rp);
sb = calllib(lib, 'jg_static_block');
setdatatype(sb, 'doublePtr', 2, 3);
ix_chk('static_block', sb.Value);
sb2 = sb + 2;
setdatatype(sb2, 'doublePtr', 1, 2);
ix_chk('static_block_plus_2', sb2.Value);
si = calllib(lib, 'jg_static_ints');
ix_chk('static_ints_DataType', si.DataType);
setdatatype(si, 'int32Ptr', 1, 4);
ix_chk('static_ints', si.Value);
kp = libpointer('doublePtr', zeros(1, 3));
calllib(lib, 'jg_keep', kp, 3);
calllib(lib, 'jg_write_kept', 4.5);
ix_chk('kept_pointer_written_later', kp.Value);
calllib(lib, 'jg_release_kept');

% ---- strings
ix_chk('str_return', calllib(lib, 'jg_greeting'));
ix_chk('str_return_class', class(calllib(lib, 'jg_greeting')));
ix_chk('str_in_char', calllib(lib, 'jg_strlen', 'hello'));
ix_chk('str_in_string_refused', ix_id(@() calllib(lib, 'jg_strlen', "hello")));
ix_chk('str_in_empty', calllib(lib, 'jg_strlen', ''));
ix_chk('str_in_null', calllib(lib, 'jg_strlen', []));
ix_chk('str_in_number_refused', ix_id(@() calllib(lib, 'jg_strlen', 65)));
ix_chk('str_inout', calllib(lib, 'jg_upper', 'abc'));
[s1, s2] = calllib(lib, 'jg_upper_ret', 'abc');
ix_chk('str_inout_and_return', {s1, s2});
ix_chk('str_buffer', calllib(lib, 'jg_fill_name', blanks(20), 20));
ix_chk('str_buffer_short', calllib(lib, 'jg_fill_name', blanks(4), 4));
ix_chk('str_buffer_int8ptr_refused', ix_id(@() calllib(lib, 'jg_fill_name', libpointer('int8Ptr', zeros(1, 20, 'int8')), 20)));
ix_chk('str_array_cellstr', calllib(lib, 'jg_total_len', {'ab', 'cde'}, 2));
ix_chk('str_array_string_refused', ix_id(@() calllib(lib, 'jg_total_len', ["ab" "cde"], 2)));
wp = calllib(lib, 'jg_words');
ix_chk('str_ptrptr_DataType', wp.DataType);
ix_chk('str_ptrptr_Value', wp.Value);
wp1 = wp + 1;
ix_chk('str_ptrptr_plus', wp1.Value);
ix_chk('str_ptrptr_setdatatype_refused', ix_id(@() setdatatype(wp, 'stringPtrPtr', 1, 3)));
ix_chk('str_out', calllib(lib, 'jg_pick_word', 1, libpointer('stringPtrPtr', {''})));
ix_chk('str_out_null', calllib(lib, 'jg_pick_word', 9, libpointer('stringPtrPtr', {''})));
ix_chk('str_out_cell', calllib(lib, 'jg_pick_word', 1, {''}));

% ---- void* handles
h = calllib(lib, 'jg_opaque_new', 2.5);
ix_chk('voidptr_class', class(h));
ix_chk('voidptr_DataType', h.DataType);
ix_chk('voidptr_roundtrip', calllib(lib, 'jg_opaque_get', h));
ix_chk('voidptr_Value_refused', ix_id(@() h.Value));
calllib(lib, 'jg_opaque_free', h);
ix_chk('voidptr_null', calllib(lib, 'jg_opaque_get', libpointer));

% ---- the process a native call runs in
setenv('JG_NATIVE_VAR', 'fromMATLAB');
ix_chk('getenv_sees_setenv', calllib(lib, 'jg_getenv', 'JG_NATIVE_VAR'));
ix_chk('getenv_unset', calllib(lib, 'jg_getenv', 'JG_NO_SUCH_VAR_XYZ'));
here = pwd;
cd(tempdir);
ix_chk('getcwd_follows_cd', strcmp(calllib(lib, 'jg_getcwd'), pwd));
cd(here);
ix_chk('printf_not_captured', strtrim(evalc('calllib(lib, ''jg_printf'', ''to stdout'');')));

% ---- lifetime: a plain libpointer does not hold the library; a libstruct (library-bound) does
live = libpointer('doublePtr', [1 2]);
ix_chk('unload_with_live_libpointer', ix_id(@() unloadlibrary(lib)));
ix_chk('libpointer_survives_unload', live.Value);
ix_chk('unloaded', libisloaded(lib));
loadlibrary(p.lib, @jgtestlib_proto);
st = libstruct('jg_point');
ix_chk('unload_with_live_libstruct', ix_id(@() unloadlibrary(lib)));
ix_chk('unload_with_live_libstruct_message', ix_msg(@() unloadlibrary(lib)));
ix_chk('still_loaded', libisloaded(lib));

function two(f)
[~, ~] = f();
end

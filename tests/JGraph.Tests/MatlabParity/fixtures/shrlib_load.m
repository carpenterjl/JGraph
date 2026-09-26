% shrlib_load.m -- loading a C shared library through its prototype file (interop plan, stage 8):
% loadlibrary(lib, @protofile), libisloaded, libfunctions and its -full listing, a second load, an
% alias, unloadlibrary and its refusals, and the error a bad load raises. The prototype file is the
% one R2025b wrote for tests/Interop/native/jgtestlib.h (committed beside it); loading through it
% needs no compiler, so these rows run in the lanes. The header route is compiler-gated and tested
% outside the parity suite.

p = interop_paths();
addpath(p.root);
lib = 'jgtestlib';
if libisloaded(lib), unloadlibrary(lib); end

ix_chk('isloaded_before', libisloaded(lib));
[nf, w] = loadlibrary(p.lib, @jgtestlib_proto);
ix_chk('proto_notfound', ix_show(nf));
ix_chk('proto_warnings', ix_show(w));
ix_chk('isloaded', libisloaded(lib));
ix_chk('isloaded_string', libisloaded("jgtestlib"));
f = libfunctions(lib);
ix_chk('libfunctions_class', class(f));
ix_chk('libfunctions_count', numel(f));
ix_chk('libfunctions', strjoin(f', ','));
ix_chk('libfunctions_full', ix_flat(evalc('libfunctions(lib, ''-full'')')));
ix_chk('libfunctions_display', ix_flat(evalc('libfunctions(lib)')));
ix_chk('libfunctions_unloaded', ix_show(libfunctions('nosuchlib')));
ix_chk('call', calllib(lib, 'jg_version'));
ix_chk('call_string_names', calllib("jgtestlib", "jg_version"));

lastwarn('');
loadlibrary(p.lib, @jgtestlib_proto);
[msg, id] = lastwarn;
ix_chk('load_twice_warning', msg);
ix_chk('load_twice_warning_id', id);
ix_chk('load_twice_still_loaded', libisloaded(lib));

unloadlibrary(lib);
ix_chk('unloaded', libisloaded(lib));
ix_chk('unload_again', ix_id(@() unloadlibrary(lib)));
ix_chk('unload_again_message', ix_msg(@() unloadlibrary(lib)));
ix_chk('calllib_unloaded', ix_id(@() calllib(lib, 'jg_version')));
ix_chk('calllib_unloaded_message', ix_msg(@() calllib(lib, 'jg_version')));

loadlibrary(p.lib, @jgtestlib_proto, 'alias', 'jgt');
ix_chk('alias_loaded', libisloaded('jgt'));
ix_chk('alias_real_name_not_loaded', libisloaded(lib));
ix_chk('alias_call', calllib('jgt', 'jg_version'));
ix_chk('alias_libfunctions_count', numel(libfunctions('jgt')));
loadlibrary(p.lib, @jgtestlib_proto, 'alias', 'jgt2');
ix_chk('alias_second', calllib('jgt2', 'jg_version'));
unloadlibrary('jgt');
unloadlibrary('jgt2');

ix_chk('err_no_args', ix_id(@() loadlibrary()));
ix_chk('err_no_args_message', ix_msg(@() loadlibrary()));
ix_chk('err_numeric', ix_id(@() loadlibrary(5, @jgtestlib_proto)));
ix_chk('err_bad_option', ix_id(@() loadlibrary(p.lib, @jgtestlib_proto, 'nosuchoption', 1)));
ix_chk('err_bad_option_message', ix_msg(@() loadlibrary(p.lib, @jgtestlib_proto, 'nosuchoption', 1)));
ix_chk('err_missing_lib', ix_id(@() loadlibrary('C:\no\such\lib.dll', @jgtestlib_proto, 'alias', 'nolib')));
ix_chk('err_missing_header', ix_id(@() loadlibrary(p.lib, 'C:\no\such\header.h', 'alias', 'nohdr')));
ix_chk('err_missing_header_message', ix_msg(@() loadlibrary(p.lib, 'C:\no\such\header.h', 'alias', 'nohdr')));
ix_chk('err_unload_never', ix_id(@() unloadlibrary('neverloaded')));
ix_chk('err_calllib_never', ix_id(@() calllib('neverloaded', 'f')));
ix_chk('err_calllib_never_message', ix_msg(@() calllib('neverloaded', 'f')));
ix_chk('libmethods_absent', exist('libmethods'));
ix_chk('libisloaded_numeric', ix_id(@() libisloaded(5)));

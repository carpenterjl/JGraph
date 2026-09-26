% probe_shrlib_calls: calllib for every type row, pointers, strings, structs, enums, and the
% process-level behaviour a native call sees.
a = ip_assets();
addpath(a.here); addpath(a.root);
lib = 'jgtestlib';
loadlibrary(a.lib, @jgtestlib_proto);
c = @(varargin) calllib(lib, varargin{:}); %#ok<NASGU>

% ---- every primitive in and out: argument class, returned class and value, and saturation
prims = {'jg_int8','jg_uint8','jg_int16','jg_uint16','jg_int32','jg_uint32','jg_int64','jg_uint64', ...
    'jg_float','jg_double','jg_char','jg_schar','jg_uchar','jg_short','jg_ushort','jg_int', ...
    'jg_uint','jg_long','jg_ulong','jg_longlong','jg_ulonglong','jg_size','jg_bool'};
for k = 1:numel(prims)
    f = prims{k};
    ip_pr(['prim.' f '.double'], sprintf('calllib(lib, ''%s'', 5)', f));
    ip_pr(['prim.' f '.frac'], sprintf('calllib(lib, ''%s'', 2.7)', f));
    ip_pr(['prim.' f '.neg'], sprintf('calllib(lib, ''%s'', -3)', f));
    ip_pr(['prim.' f '.big'], sprintf('calllib(lib, ''%s'', 1e10)', f));
    ip_pr(['prim.' f '.int8'], sprintf('calllib(lib, ''%s'', int8(5))', f));
    ip_pr(['prim.' f '.logical'], sprintf('calllib(lib, ''%s'', true)', f));
    ip_pr(['prim.' f '.char'], sprintf('calllib(lib, ''%s'', ''A'')', f));
    ip_pr(['prim.' f '.empty'], sprintf('calllib(lib, ''%s'', [])', f));
    ip_pr(['prim.' f '.vector'], sprintf('calllib(lib, ''%s'', [1 2])', f));
    ip_pr(['prim.' f '.nan'], sprintf('calllib(lib, ''%s'', NaN)', f));
end
ip_pr('prim.int8.max', 'calllib(lib, ''jg_int8'', int8(127))');
ip_pr('prim.uint64.max', 'calllib(lib, ''jg_uint64'', intmax(''uint64''))');
ip_pr('prim.int64.big', 'calllib(lib, ''jg_int64_big'')');
ip_pr('prim.int64.big.sprintf', 'sprintf(''%d'', calllib(lib, ''jg_int64_big''))');
ip_pr('prim.uint64.max.ret', 'sprintf(''%d'', calllib(lib, ''jg_uint64_max''))');
ip_pr('prim.mixed', 'calllib(lib, ''jg_mixed_args'', 1, 2, 3, 4, 5)');
ip_pr('prim.void.output', 'calllib(lib, ''jg_void'')');
ip_px('prim.void.call', 'calllib(lib, ''jg_void'')');
ip_pr('prim.too.few', 'calllib(lib, ''jg_double'')');
ip_pr('prim.too.many', 'calllib(lib, ''jg_double'', 1, 2)');
ip_pr('prim.nosuch', 'calllib(lib, ''jg_nosuch'')');
ip_pr('prim.notfound', 'calllib(lib, ''jg_not_exported'', 1)');
ip_pr('prim.string.name', 'calllib(lib, "jg_double", 1)');
ip_pr('prim.version', 'calllib(lib, ''jg_version'')');

% ---- T* in/out: plain arrays get a copy back as an extra output
ip_px('ptr.double.array', '[r1, r2] = calllib(lib, ''jg_scale_double'', [1 2 3], 3, 2)');
ip_px('ptr.double.one.output', 'r = calllib(lib, ''jg_scale_double'', [1 2 3], 3, 2)');
ip_px('ptr.double.col', '[r1] = calllib(lib, ''jg_scale_double'', [1; 2; 3], 3, 2)');
ip_px('ptr.double.mat', '[r1] = calllib(lib, ''jg_scale_double'', [1 2; 3 4], 4, 2)');
ip_px('ptr.int32.from.double', '[r1] = calllib(lib, ''jg_scale_int32'', [1 2 3], 3, 2)');
ip_px('ptr.int16.native', '[r1] = calllib(lib, ''jg_scale_int16'', int16([1 2 3]), 3, 2)');
ip_px('ptr.uint8.char', '[r1] = calllib(lib, ''jg_scale_uint8'', ''abc'', 3, 1)');
ip_px('ptr.int64', '[r1] = calllib(lib, ''jg_scale_int64'', int64([1 2 3]), 3, 2)');
ip_px('ptr.float', '[r1] = calllib(lib, ''jg_scale_float'', single([1 2 3]), 3, 2)');
ip_px('ptr.bool', '[r1] = calllib(lib, ''jg_negate_bool'', [true false], 2)');
ip_px('ptr.short.n', '[r1] = calllib(lib, ''jg_scale_double'', [1 2 3], 2, 2)');
ip_pr('ptr.sum', 'calllib(lib, ''jg_sum'', [1 2 3], 3)');
ip_pr('ptr.sum.scalar', 'calllib(lib, ''jg_sum'', 5, 1)');
ip_pr('ptr.sum.empty', 'calllib(lib, ''jg_sum'', [], 0)');
ip_px('ptr.add_ref', '[r, b] = calllib(lib, ''jg_add_ref'', 1.5, 2, 3)');
ip_px('ptr.sum2d', 'r = calllib(lib, ''jg_sum2d'', [1 2 3; 4 5 6], 2)');
ip_px('ptr.sum2d.t', 'r = calllib(lib, ''jg_sum2d'', [1 2 3; 4 5 6]'', 2)');

% ---- libpointer
p = libpointer('doublePtr', [1 2 3]);
ip_pr('lp.class', 'class(p)');
ip_px('lp.disp', 'p');
ip_pr('lp.DataType', 'p.DataType');
ip_pr('lp.Value', 'p.Value');
ip_pr('lp.isNull', 'isNull(p)');
calllib(lib, 'jg_scale_double', p, 3, 10);
ip_pr('lp.after.call', 'p.Value');
ip_pr('lp.get', 'get(p)');
ip_px('lp.methods', 'methods(p)');
ip_pr('lp.plus', 'class(p + 1)');
q = p + 1;
ip_pr('lp.plus.value', 'q.Value');
ip_pr('lp.plus.DataType', 'q.DataType');
setdatatype(q, 'doublePtr', 1, 2);
ip_pr('lp.plus.setdatatype', 'q.Value');
p.Value = [7 8];
ip_pr('lp.set.Value.shorter', 'p.Value');
ip_px('lp.set.Value.class', 'p.Value = int8([1 2]); disp(class(p.Value))');
ip_pr('lp.null', 'libpointer');
ip_pr('lp.null.isNull', 'isNull(libpointer)');
ip_pr('lp.null.typed', 'isNull(libpointer(''doublePtr''))');
ip_pr('lp.null.typed.Value', 'libpointer(''doublePtr'').Value');
ip_pr('lp.voidPtr', 'libpointer(''voidPtr'', [1 2])');
ip_pr('lp.bad.type', 'libpointer(''nosuchPtr'', 1)');
ip_pr('lp.is_null.null', 'calllib(lib, ''jg_is_null'', libpointer)');
ip_pr('lp.is_null.empty', 'calllib(lib, ''jg_is_null'', [])');
ip_pr('lp.is_null.zero', 'calllib(lib, ''jg_is_null'', 0)');
ip_pr('lp.is_null.value', 'calllib(lib, ''jg_is_null'', 5)');

% ---- memory the library owns
ip_px('own.alloc_doubles', '[n, pp] = calllib(lib, ''jg_alloc_doubles'', libpointer(''doublePtrPtr''), 4)');
ip_px('own.pp.Value', 'pp = libpointer(''doublePtrPtr''); [n, pp] = calllib(lib, ''jg_alloc_doubles'', pp, 4); v = pp.Value; disp(class(v))');
ip_px('own.pp.inner.ptr', 'ip0 = libpointer(''doublePtr''); [n, ip1] = calllib(lib, ''jg_alloc_doubles'', ip0, 4); disp(class(ip1)); disp(ip1.DataType); setdatatype(ip1, ''doublePtr'', 1, 4); disp(ip1.Value); disp(ip0.DataType)');
ip_px('own.pp.wrapped', 'ip0 = libpointer(''doublePtr''); pp = libpointer(''doublePtrPtr'', ip0); [n, pp2] = calllib(lib, ''jg_alloc_doubles'', pp, 4); disp(class(pp2)); v = pp2.Value; disp(class(v))');
ip_px('own.pp.inner.after', 'ip0 = libpointer(''doublePtr''); calllib(lib, ''jg_alloc_doubles'', ip0, 4); setdatatype(ip0, ''doublePtr'', 1, 4); disp(ip0.Value); calllib(lib, ''jg_free'', ip0)');
r = calllib(lib, 'jg_alloc_ret', 3);
ip_pr('own.ret.class', 'class(r)');
ip_pr('own.ret.Value.before', 'r.Value');
setdatatype(r, 'doublePtr', 1, 3);
ip_pr('own.ret.setdatatype', 'r.Value');
reshape(r, 3, 1);
ip_pr('own.ret.reshape', 'size(r.Value)');
calllib(lib, 'jg_free', r);
sb = calllib(lib, 'jg_static_block');
setdatatype(sb, 'doublePtr', 2, 3);
ip_pr('static.block', 'sb.Value');
sb2 = sb + 2;
setdatatype(sb2, 'doublePtr', 1, 2);
ip_pr('static.block.plus2', 'sb2.Value');
si = calllib(lib, 'jg_static_ints');
ip_pr('static.ints.DataType', 'si.DataType');
setdatatype(si, 'int32Ptr', 1, 4);
ip_pr('static.ints', 'si.Value');

% ---- a pointer the library keeps
kp = libpointer('doublePtr', zeros(1, 3));
calllib(lib, 'jg_keep', kp, 3);
calllib(lib, 'jg_write_kept', 4.5);
ip_pr('kept.after.write', 'kp.Value');
calllib(lib, 'jg_release_kept');
ip_px('kept.plain.array', 'calllib(lib, ''jg_keep'', zeros(1, 3), 3); n = calllib(lib, ''jg_write_kept'', 1); calllib(lib, ''jg_release_kept'')');

% ---- strings
ip_pr('str.greeting', 'calllib(lib, ''jg_greeting'')');
ip_pr('str.strlen.char', 'calllib(lib, ''jg_strlen'', ''hello'')');
ip_pr('str.strlen.string', 'calllib(lib, ''jg_strlen'', "hello")');
ip_pr('str.strlen.empty', 'calllib(lib, ''jg_strlen'', '''')');
ip_pr('str.strlen.null', 'calllib(lib, ''jg_strlen'', [])');
ip_pr('str.strlen.number', 'calllib(lib, ''jg_strlen'', 65)');
ip_px('str.upper', '[r1] = calllib(lib, ''jg_upper'', ''abc'')');
ip_px('str.upper.ret', '[r1, r2] = calllib(lib, ''jg_upper_ret'', ''abc'')');
ip_px('str.fill.name', '[r1] = calllib(lib, ''jg_fill_name'', blanks(20), 20)');
ip_px('str.fill.name.short', '[r1] = calllib(lib, ''jg_fill_name'', blanks(4), 4)');
ip_px('str.fill.name.libpointer', 'bp = libpointer(''int8Ptr'', zeros(1, 20, ''int8'')); calllib(lib, ''jg_fill_name'', bp, 20); disp(char(bp.Value))');
ip_px('str.total_len.cellstr', 'r = calllib(lib, ''jg_total_len'', {''ab'', ''cde''}, 2)');
ip_px('str.total_len.string', 'r = calllib(lib, ''jg_total_len'', ["ab" "cde"], 2)');
ip_px('str.words', 'w = calllib(lib, ''jg_words''); disp(class(w)); disp(w.DataType)');
ip_px('str.words.setdatatype', 'w = calllib(lib, ''jg_words''); setdatatype(w, ''stringPtrPtr'', 1, 3); disp(w.Value)');
ip_px('str.words.Value', 'w = calllib(lib, ''jg_words''); v = w.Value; disp(class(v)); disp(v)');
ip_px('str.words.plus', 'w = calllib(lib, ''jg_words''); w1 = w + 1; disp(w1.Value)');
ip_px('str.pick_word', '[r] = calllib(lib, ''jg_pick_word'', 1, libpointer(''stringPtrPtr'', {''''}))');
ip_px('str.pick_word.bad', '[r] = calllib(lib, ''jg_pick_word'', 9, libpointer(''stringPtrPtr'', {''''}))');
ip_px('str.pick_word.cell', '[r] = calllib(lib, ''jg_pick_word'', 1, {''''})');

% ---- void* handles
ip_px('void.class', 'h = calllib(lib, ''jg_opaque_new'', 2.5); disp(class(h)); disp(h.DataType)');
ip_px('void.get', 'h = calllib(lib, ''jg_opaque_new'', 2.5); disp(calllib(lib, ''jg_opaque_get'', h)); calllib(lib, ''jg_opaque_free'', h)');
ip_px('void.Value', 'h = calllib(lib, ''jg_opaque_new'', 2.5); disp(h.Value)');
ip_px('void.null', 'disp(calllib(lib, ''jg_opaque_get'', libpointer))');

% ---- structs
ip_pr('st.point.len.struct', 'calllib(lib, ''jg_point_len'', struct(''x'', 3, ''y'', 4))');
ip_pr('st.point.len.libstruct', 'calllib(lib, ''jg_point_len'', libstruct(''jg_point'', struct(''x'', 3, ''y'', 4)))');
ip_pr('st.point.len.partial', 'calllib(lib, ''jg_point_len'', struct(''x'', 3))');
ip_pr('st.point.len.extra.field', 'calllib(lib, ''jg_point_len'', struct(''x'', 3, ''y'', 4, ''z'', 5))');
ip_pr('st.point.len.wrong.case', 'calllib(lib, ''jg_point_len'', struct(''X'', 3, ''y'', 4))');
ip_px('st.point.scale.struct', '[r] = calllib(lib, ''jg_point_scale'', struct(''x'', 1, ''y'', 2), 10)');
ip_px('st.libstruct.class', 'ls = libstruct(''jg_point''); disp(class(ls))');
ip_px('st.libstruct.disp', 'ls = libstruct(''jg_point'')');
ip_px('st.libstruct.default', 'ls = libstruct(''jg_point''); disp(get(ls))');
ip_px('st.libstruct.after.call', 'ls = libstruct(''jg_point''); ls.x = 1; ls.y = 2; calllib(lib, ''jg_point_scale'', ls, 3); disp(get(ls))');
ip_px('st.libstruct.structsize', 'ls = libstruct(''jg_point''); disp(ls.structsize)');
ip_px('st.libstruct.fields', 'ls = libstruct(''jg_point''); disp(fieldnames(ls))');
ip_px('st.libstruct.bad.field', 'ls = libstruct(''jg_point''); ls.z = 1;');
ip_px('st.libstruct.set.char', 'ls = libstruct(''jg_point''); ls.x = ''a''; disp(ls.x)');
ip_px('st.libstruct.set.vector', 'ls = libstruct(''jg_point''); ls.x = [1 2]; disp(ls.x)');
ip_px('st.libstruct.nosuch', 'libstruct(''jg_nosuch'')');
ip_px('st.libstruct.init', 'disp(get(libstruct(''jg_point'', struct(''x'', 9))))');
ip_px('st.libstruct.copy', 'l1 = libstruct(''jg_point''); l2 = l1; l2.x = 5; disp(l1.x)');
ip_px('st.mixed.get', 'm = libstruct(''jg_mixed''); calllib(lib, ''jg_mixed_fill'', m); disp(get(m))');
ip_px('st.mixed.name', 'm = libstruct(''jg_mixed''); calllib(lib, ''jg_mixed_fill'', m); v = m.name; disp(class(v)); disp(v)');
ip_px('st.mixed.d', 'm = libstruct(''jg_mixed''); calllib(lib, ''jg_mixed_fill'', m); v = m.d; disp(class(v)); disp(v)');
ip_px('st.mixed.structsize', 'm = libstruct(''jg_mixed''); disp(m.structsize)');
ip_px('st.mixed.sum', 'm = libstruct(''jg_mixed''); calllib(lib, ''jg_mixed_fill'', m); disp(calllib(lib, ''jg_mixed_sum'', m))');
ip_pr('st.mixed.sum.struct', 'calllib(lib, ''jg_mixed_sum'', struct(''a'', 1, ''b'', 2, ''c'', 3, ''d'', [1 2 3], ''name'', int8(''ab'')))');
ip_pr('st.mixed.name.char', 'calllib(lib, ''jg_mixed_sum'', struct(''a'', 1, ''b'', 2, ''c'', 3, ''d'', [1 2 3], ''name'', ''ab''))');
ip_px('st.nested.default', 'n = libstruct(''jg_nested''); disp(get(n))');
ip_px('st.nested.p.class', 'n = libstruct(''jg_nested''); disp(class(n.p))');
ip_pr('st.nested.sum', 'calllib(lib, ''jg_nested_sum'', struct(''p'', struct(''x'', 1, ''y'', 2), ''q'', struct(''x'', 3, ''y'', 4), ''id'', 5))');
ip_px('st.packed.structsize', 'pk = libstruct(''jg_packed''); disp(pk.structsize)');
ip_px('st.packed.fill', 'pk = libstruct(''jg_packed''); calllib(lib, ''jg_packed_fill'', pk); disp(get(pk)); disp(calllib(lib, ''jg_packed_sum'', pk))');
ip_px('st.point.alloc', 'pp = libpointer(''jg_pointPtrPtr''); calllib(lib, ''jg_point_alloc'', pp); v = pp.Value; disp(class(v)); disp(v.Value)');
ip_px('st.point.alloc.ptr', 'pp = libpointer(''jg_pointPtr''); calllib(lib, ''jg_point_alloc'', pp); v = pp.Value; disp(class(v)); disp(v)');
ip_pr('st.point.make', 'calllib(lib, ''jg_point_make'', 1, 2)');
ip_pr('layout', 'arrayfun(@(k) double(calllib(lib, ''jg_layout'', k)), 0:13)');

% ---- enums
ip_pr('enum.name.char', 'calllib(lib, ''jg_color_name'', ''JG_GREEN'')');
ip_pr('enum.name.value', 'calllib(lib, ''jg_color_name'', 4)');
ip_pr('enum.name.int32', 'calllib(lib, ''jg_color_name'', int32(1))');
ip_pr('enum.name.bad.char', 'calllib(lib, ''jg_color_name'', ''JG_PURPLE'')');
ip_pr('enum.name.bad.value', 'calllib(lib, ''jg_color_name'', 3)');
ip_pr('enum.name.string', 'calllib(lib, ''jg_color_name'', "JG_BLUE")');
ip_pr('enum.next', 'calllib(lib, ''jg_color_next'', ''JG_RED'')');
ip_pr('enum.next.value', 'calllib(lib, ''jg_color_next'', 2)');
ip_pr('enum.value', 'calllib(lib, ''jg_color_value'', ''JG_BLUE'')');

% ---- unsupported declarations
ip_pr('bad.union', 'calllib(lib, ''jg_union_in'', 1)');
ip_pr('bad.bits', 'calllib(lib, ''jg_bits_in'', libstruct(''jg_bits''))');
ip_pr('bad.bits.struct', 'calllib(lib, ''jg_bits_in'', struct(''low'', 1, ''high'', 2))');
ip_pr('bad.fnptr', 'calllib(lib, ''jg_apply'', @sin, 1)');
ip_pr('bad.fnptr.null', 'calllib(lib, ''jg_apply'', libpointer, 1)');
ip_pr('bad.varargs', 'calllib(lib, ''jg_varsum'', 2, 3, 4)');
ip_pr('bad.triple', 'calllib(lib, ''jg_triple'', libpointer)');
ip_pr('exported.value', 'exist(''jg_exported_value'')');

% ---- process-level behaviour
ip_px('proc.printf', 'n = calllib(lib, ''jg_printf'', ''to stdout'')');
setenv('JG_NATIVE_VAR', 'fromMATLAB');
ip_pr('proc.getenv', 'calllib(lib, ''jg_getenv'', ''JG_NATIVE_VAR'')');
ip_pr('proc.getenv.unset', 'calllib(lib, ''jg_getenv'', ''JG_NO_SUCH_VAR_XYZ'')');
here = pwd;
ip_pr('proc.getcwd', 'strcmp(calllib(lib, ''jg_getcwd''), pwd)');
cd(tempdir);
ip_pr('proc.getcwd.after.cd', 'strcmp(calllib(lib, ''jg_getcwd''), pwd)');
cd(here);
t0 = tic;
ip_px('proc.sleep', 'calllib(lib, ''jg_sleep'', 300)');
fprintf('proc.sleep.seconds\t%.1f\n', toc(t0));

% ---- lifetime: unload with live pointers, then use them
lp = libpointer('doublePtr', [1 2]);
st = libstruct('jg_point');
ip_px('life.unload.with.live', 'unloadlibrary(lib)');
ip_pr('life.pointer.after.unload', 'lp.Value');
ip_pr('life.struct.after.unload', 'get(st)');
ip_pr('life.libstruct.unloaded', 'libstruct(''jg_point'')');
ip_pr('life.libpointer.unloaded', 'class(libpointer(''doublePtr'', 1))');

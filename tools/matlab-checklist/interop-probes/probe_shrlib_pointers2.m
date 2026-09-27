% probe_shrlib_pointers2: what stage 9 still had to measure before building lib.pointer and
% libstruct (interop plan, stage 9): what calllib hands back for a pointer argument given as a
% lib.pointer or a libstruct, the sentences of refusals the step 0 probes recorded only by
% identifier, and lib.pointer's and libstruct's methods at their edges.
a = ip_assets();
addpath(a.here); addpath(a.root);
lib = 'jgtestlib';
loadlibrary(a.lib, @jgtestlib_proto);

% ---- what a pointer argument comes back as
ip_px('out.lp.double', 'lp = libpointer(''doublePtr'', [1 2 3]); r = calllib(lib, ''jg_scale_double'', lp, 3, 10); disp(class(r)); disp(r == lp)');
ip_px('out.lp.double.value', 'lp = libpointer(''doublePtr'', [1 2 3]); r = calllib(lib, ''jg_scale_double'', lp, 3, 10); disp(r)');
ip_px('out.ls', 'ls = libstruct(''jg_point'', struct(''x'', 1, ''y'', 2)); r = calllib(lib, ''jg_point_scale'', ls, 2); disp(class(r)); disp(r == ls)');
ip_px('out.lp.struct', 'pl = libpointer(''jg_pointPtr'', struct(''x'', 1, ''y'', 2)); r = calllib(lib, ''jg_point_scale'', pl, 2); disp(class(r))');
ip_px('out.void.lp', 'h = libpointer(''voidPtr'', [1 2]); [n, b] = calllib(lib, ''jg_is_null'', h); disp(class(b)); disp(b == h)');
ip_px('out.void.array', '[n, b] = calllib(lib, ''jg_is_null'', [1 2 3]); disp(class(b)); disp(size(b)); disp(b)');
ip_px('out.void.int8', '[n, b] = calllib(lib, ''jg_is_null'', int8([1 2 3])); disp(class(b)); disp(b)');
ip_px('out.void.empty', '[n, b] = calllib(lib, ''jg_is_null'', []); disp(class(b)); disp(size(b))');
ip_px('out.cstring.null', '[n, s] = calllib(lib, ''jg_strlen'', []); disp(class(s)); disp(size(s))');
ip_px('out.cstring.value', '[n, s] = calllib(lib, ''jg_strlen'', ''abc''); disp(class(s)); disp(s)');
ip_px('out.double.empty', 'r = calllib(lib, ''jg_scale_double'', [], 0, 2); disp(class(r)); disp(size(r))');
ip_px('out.ptrptr.same', 'ip0 = libpointer(''doublePtr''); [n, ip1] = calllib(lib, ''jg_alloc_doubles'', ip0, 4); disp(ip1 == ip0); disp(isNull(ip0))');
ip_px('out.ptrptr.fresh', 'pp = libpointer(''doublePtrPtr''); [n, q] = calllib(lib, ''jg_alloc_doubles'', pp, 4); disp(q == pp); disp(q.DataType); disp(pp.DataType); disp(isNull(pp))');
ip_px('out.ptrptr.array', '[n, q] = calllib(lib, ''jg_alloc_doubles'', [1 2], 4); disp(class(q))');
ip_px('out.ptrptr.empty', '[n, q] = calllib(lib, ''jg_alloc_doubles'', [], 4); disp(n); disp(class(q))');
ip_px('out.bool.scalar', '[b] = calllib(lib, ''jg_negate_bool'', true, 1); disp(class(b)); disp(b)');
ip_px('out.stringptrptr.lp', 'w = libpointer(''stringPtrPtr'', {''''}); r = calllib(lib, ''jg_pick_word'', 2, w); disp(class(r)); disp(w.Value)');
ip_px('out.struct.lp.ptrptr', 'pt = libpointer(''jg_pointPtr''); r = calllib(lib, ''jg_point_alloc'', pt); disp(class(r)); disp(r == pt); v = pt.Value; disp(v.x)');

% ---- sentences step 0 recorded by identifier only
ip_pr('msg.bool.char', 'calllib(lib, ''jg_bool'', ''A'')');
ip_pr('msg.bool.vector', 'calllib(lib, ''jg_bool'', [true false])');
ip_pr('msg.lp.bad.type', 'libpointer(''nosuchPtr'', 1)');
ip_pr('msg.lp.scalar.type', 'libpointer(''double'', 5)');
ip_pr('msg.lp.cstring.type', 'libpointer(''cstring'', ''abc'')');
ip_pr('msg.lp.char.to.int32', 'libpointer(''int32Ptr'', ''abc'')');
ip_pr('msg.lp.cell.to.double', 'libpointer(''doublePtr'', {1})');
ip_pr('msg.lp.struct.to.double', 'libpointer(''doublePtr'', struct(''x'', 1))');
ip_pr('msg.lp.string.to.stringptrptr', 'libpointer(''stringPtrPtr'', "ab")');
ip_pr('msg.lp.char.to.stringptrptr', 'libpointer(''stringPtrPtr'', ''ab'')');
ip_pr('msg.lp.ptrptr.Value', 'libpointer(''doublePtrPtr'').Value');
ip_pr('msg.ls.nosuch', 'libstruct(''jg_nosuch'')');
ip_pr('msg.ls.extra', 'libstruct(''jg_point'', struct(''z'', 1))');
ip_pr('msg.ls.bad.arg', 'libstruct(''jg_point'', 5)');
ip_pr('msg.ls.no.args', 'libstruct()');
ip_pr('msg.lp.too.many', 'libpointer(''doublePtr'', 1, 2)');
ip_pr('msg.str.array.to.voidptr', 'calllib(lib, ''jg_is_null'', ''abc'')');
ip_pr('msg.struct.to.voidptr', 'calllib(lib, ''jg_is_null'', struct(''x'', 1))');
ip_pr('msg.cell.to.doubleptr', 'calllib(lib, ''jg_sum'', {1, 2}, 2)');
ip_pr('msg.int8ptr.to.doubleptr', 'calllib(lib, ''jg_sum'', libpointer(''int8Ptr'', int8([1 2])), 2)');
ip_pr('msg.voidptr.to.doubleptr', 'calllib(lib, ''jg_sum'', libpointer(''voidPtr'', [1 2]), 2)');
ip_pr('msg.int32ptr.to.voidptr', 'calllib(lib, ''jg_is_null'', libpointer(''int32Ptr'', int32([1 2])))');
ip_pr('msg.struct.to.doubleptr', 'calllib(lib, ''jg_sum'', struct(''x'', 1), 1)');
ip_pr('msg.libstruct.to.doubleptr', 'calllib(lib, ''jg_sum'', libstruct(''jg_point''), 1)');
ip_pr('msg.double.to.structptr', 'calllib(lib, ''jg_point_scale'', 5, 2)');
ip_pr('msg.double.to.struct', 'calllib(lib, ''jg_point_len'', 5)');
ip_pr('msg.lp.to.struct', 'calllib(lib, ''jg_point_len'', libpointer(''doublePtr'', [1 2]))');
ip_pr('msg.lp.to.scalar', 'calllib(lib, ''jg_double'', libpointer(''doublePtr'', 1))');
ip_pr('msg.cell.to.scalar', 'calllib(lib, ''jg_double'', {1})');
ip_pr('msg.struct.to.scalar', 'calllib(lib, ''jg_double'', struct(''x'', 1))');
ip_pr('msg.string.to.scalar', 'calllib(lib, ''jg_double'', "5")');
ip_pr('msg.complex.to.scalar', 'calllib(lib, ''jg_double'', 1 + 2i)');
ip_pr('msg.enum.vector', 'calllib(lib, ''jg_color_name'', [1 2])');
ip_pr('msg.enum.fraction', 'calllib(lib, ''jg_color_name'', 2.6)');
ip_pr('msg.enum.logical', 'calllib(lib, ''jg_color_name'', true)');
ip_pr('msg.enum.cell', 'calllib(lib, ''jg_color_name'', {''JG_RED''})');
ip_pr('msg.enum.nan', 'calllib(lib, ''jg_color_name'', NaN)');
ip_pr('msg.data.export', 'calllib(lib, ''jg_exported_value'')');
ip_pr('msg.mixed.long.array', 'calllib(lib, ''jg_mixed_sum'', struct(''d'', [1 2 3 4]))');
ip_pr('msg.mixed.short.array', 'calllib(lib, ''jg_mixed_sum'', struct(''d'', [1 2]))');
ip_pr('msg.mixed.long.name', 'calllib(lib, ''jg_mixed_sum'', struct(''name'', int8(''abcdefghij'')))');
ip_pr('msg.mixed.vector.scalar', 'calllib(lib, ''jg_mixed_sum'', struct(''a'', [1 2]))');
ip_pr('msg.mixed.char.scalar', 'calllib(lib, ''jg_mixed_sum'', struct(''a'', ''x''))');
ip_pr('msg.nested.bad', 'calllib(lib, ''jg_nested_sum'', struct(''p'', 5))');
ip_pr('msg.nested.extra', 'calllib(lib, ''jg_nested_sum'', struct(''p'', struct(''z'', 1)))');
ip_pr('msg.struct.array', 'calllib(lib, ''jg_point_len'', struct(''x'', {1, 2}))');

% ---- lib.pointer at its edges
lp = libpointer('doublePtr', [1 2 3]);
ip_px('lp.disp', 'disp(lp)');
ip_px('lp.display', 'lp');
ip_pr('lp.set.char', 'setv(lp, ''ab'')');
ip_pr('lp.set.cell', 'setv(lp, {1})');
ip_pr('lp.set.DataType', 'setp(lp, ''DataType'', ''int32Ptr'')');
ip_pr('lp.read.nope', 'lp.Nope');
ip_pr('lp.set.nope', 'setp(lp, ''Nope'', 1)');
ip_pr('lp.get.Value', 'get(lp, ''Value'')');
ip_pr('lp.get.DataType', 'get(lp, ''DataType'')');
ip_px('lp.set.fn', 'set(lp, ''Value'', [4 5]); disp(lp.Value)');
ip_pr('lp.plus.fn', 'plus(lp, 1).Value');
ip_pr('lp.plus.left', '(1 + lp).Value');
ip_pr('lp.plus.frac', '(lp + 1.5).Value');
ip_pr('lp.plus.neg', '(lp + -1).Value');
ip_pr('lp.plus.past.end', 'size((lp + 3).Value)');
ip_pr('lp.plus.two', '(lp + 1 + 1).Value');
ip_pr('lp.minus', 'lp - 1');
ip_pr('lp.times', 'lp * 2');
ip_pr('lp.plus.vector', 'lp + [1 2]');
ip_pr('lp.plus.lp', 'lp + lp');
lp = libpointer('doublePtr', [1 2 3 4 5 6]);
ip_px('lp.reshape', 'reshape(lp, 3, 2); disp(lp.Value)');
ip_pr('lp.reshape.bigger', 'reshape(lp, 4, 4)');
ip_px('lp.reshape.bigger.value', 'reshape(lp, 4, 4); disp(size(lp.Value))');
ip_pr('lp.reshape.one.arg', 'reshape(lp, 6)');
ip_pr('lp.reshape.three.args', 'reshape(lp, 1, 2, 3)');
ip_px('lp.reshape.out', 'q = reshape(lp, 2, 3); disp(class(q))');
ip_px('lp.setdatatype.int32', 'q = libpointer(''doublePtr'', [1 2]); setdatatype(q, ''int32Ptr'', 1, 4); disp(q.Value); disp(q.DataType)');
ip_pr('lp.setdatatype.bad', 'setdatatype(libpointer(''doublePtr'', [1 2]), ''nosuchPtr'', 1, 1)');
ip_px('lp.setdatatype.nosize', 'q = libpointer(''doublePtr'', [1 2]); setdatatype(q, ''int16Ptr''); disp(q.DataType); disp(q.Value)');
ip_px('lp.setdatatype.void', 'q = libpointer(''doublePtr'', [1 2]); setdatatype(q, ''voidPtr''); disp(q.DataType)');
ip_px('lp.setdatatype.struct', 'q = libpointer(''doublePtr'', [3 4]); setdatatype(q, ''jg_pointPtr''); v = q.Value; disp(v.x); disp(v.y)');
ip_pr('lp.setdatatype.scalar', 'setdatatype(libpointer(''doublePtr'', [1 2]), ''double'', 1, 1)');
ip_pr('lp.setdatatype.null', 'setdatatype(libpointer, ''doublePtr'', 1, 1)');
ip_px('lp.null.typed.set', 'q = libpointer(''doublePtr''); q.Value = [5 6]; disp(q.Value); disp(isNull(q))');
ip_px('lp.null.untyped.set', 'q = libpointer; q.Value = [5 6]');
ip_px('lp.alias', 'q = libpointer(''doublePtr'', [1 2 3]); q2 = q; q2.Value = [9 9 9]; disp(q.Value)');
ip_px('lp.set.longer', 'q = libpointer(''doublePtr'', [1 2]); q.Value = [1 2 3 4 5]; disp(q.Value)');
ip_px('lp.set.matrix', 'q = libpointer(''doublePtr'', [1 2]); q.Value = [1 2; 3 4]; disp(q.Value)');
ip_px('lp.set.empty', 'q = libpointer(''doublePtr'', [1 2]); q.Value = []; disp(size(q.Value)); disp(isNull(q))');
ip_px('lp.int32.round', 'q = libpointer(''int32Ptr'', [2.5 -2.5 1e10]); disp(q.Value)');
ip_px('lp.matrix.shape', 'q = libpointer(''doublePtr'', [1 2; 3 4]); disp(q.Value)');
ip_px('lp.bool', 'q = libpointer(''boolPtr'', [true false]); disp(class(q.Value)); disp(q.Value)');
ip_px('lp.int8.from.char', 'q = libpointer(''int8Ptr'', int8(''ab'')); disp(q.Value)');
ip_px('lp.uint16.from.logical', 'q = libpointer(''uint16Ptr'', [true false]); disp(class(q.Value)); disp(q.Value)');
ip_px('lp.voidPtr.int16', 'q = libpointer(''voidPtr'', int16([1 2])); disp(class(q.Value)); disp(q.Value)');
ip_px('lp.voidPtr.empty', 'q = libpointer(''voidPtr''); disp(isNull(q)); disp(q.DataType)');
ip_px('lp.struct', 'q = libpointer(''jg_pointPtr'', struct(''x'', 1, ''y'', 2)); v = q.Value; disp(class(v)); disp([v.x v.y])');
ip_px('lp.struct.null', 'q = libpointer(''jg_pointPtr''); disp(isNull(q)); v = q.Value');
ip_px('lp.struct.from.libstruct', 'q = libpointer(''jg_pointPtr'', libstruct(''jg_point'', struct(''x'', 7))); v = q.Value; disp(v.x)');
ip_px('lp.stringptrptr', 'q = libpointer(''stringPtrPtr'', {''ab'', ''cde''}); v = q.Value; disp(size(v)); disp(v)');
ip_px('lp.stringptrptr.col', 'q = libpointer(''stringPtrPtr'', {''ab''; ''cde''}); v = q.Value; disp(size(v))');
ip_px('lp.ptrptr.isNull', 'q = libpointer(''doublePtrPtr''); disp(isNull(q))');
ip_px('lp.ptrptr.init', 'q = libpointer(''doublePtrPtr'', libpointer(''doublePtr'', [1 2])); disp(isNull(q))');
ip_px('lp.isvalid', 'q = libpointer(''doublePtr'', 1); disp(isvalid(q)); delete(q); disp(isvalid(q))');
ip_px('lp.after.delete', 'q = libpointer(''doublePtr'', 1); delete(q); v = q.Value');
ip_px('lp.size', 'disp(size(libpointer(''doublePtr'', [1 2 3])))');
ip_px('lp.isequal', 'q = libpointer(''doublePtr'', 1); disp(isequal(q, q)); disp(q == q); disp(q == libpointer(''doublePtr'', 1))');
ip_px('lp.ret.reshape.only', 'r = calllib(lib, ''jg_alloc_ret'', 3); reshape(r, 1, 3); disp(r.Value); calllib(lib, ''jg_free'', r)');
ip_px('lp.ret.plus', 'r = calllib(lib, ''jg_static_block''); q = r + 1; setdatatype(q, ''doublePtr'', 1, 2); disp(q.Value)');
ip_px('lp.ret.write', 'r = calllib(lib, ''jg_static_block''); setdatatype(r, ''doublePtr'', 1, 6); r.Value = [9 8]; disp(r.Value); setdatatype(r, ''doublePtr'', 1, 6); disp(r.Value); r.Value = [1 2 3 4 5 6]');
ip_px('lp.methods', 'disp(methods(lp))');
ip_px('lp.fieldnames', 'disp(fieldnames(lp))');
ip_px('lp.properties', 'disp(properties(lp))');
ip_px('lp.struct.of', 's = struct(lp)');
ip_px('lp.numel', 'disp(numel(lp))');
ip_px('lp.double', 'double(lp)');
ip_px('lp.in.cell', 'c = {lp}; disp(class(c{1}))');
ip_px('lp.isNull.fn.name', 'disp(isNull(libpointer(''voidPtr'')))');

% ---- libstruct at its edges
ls = libstruct('jg_point');
ip_px('ls.disp', 'disp(ls)');
ip_pr('ls.read.nope', 'ls.nope');
ip_pr('ls.get.x', 'get(ls, ''x'')');
ip_px('ls.set.fn', 'set(ls, ''x'', 5); disp(ls.x)');
ip_px('ls.set.int8', 'ls.x = int8(3); disp(class(ls.x)); disp(ls.x)');
ip_px('ls.set.true', 'ls.x = true; disp(class(ls.x)); disp(ls.x)');
ip_px('ls.set.nan', 'ls.x = NaN; disp(ls.x)');
ip_pr('ls.set.empty', 'setp(ls, ''x'', [])');
ip_pr('ls.set.struct', 'setp(ls, ''x'', struct(''a'', 1))');
ip_px('ls.methods', 'disp(methods(ls))');
ip_px('ls.properties', 'disp(properties(ls))');
ip_px('ls.isNull', 'disp(isNull(ls))');
ip_px('ls.size', 'disp(size(ls))');
ip_px('ls.structsize.fn', 'disp(structsize(ls))');
ip_px('ls.isvalid', 'disp(isvalid(ls))');
ip_px('ls.eq', 'q = libstruct(''jg_point''); disp(q == q); disp(q == ls)');
ip_px('ls.int.fields', 'm = libstruct(''jg_mixed''); m.a = 300; m.c = 2.5; disp(m.a); disp(m.c); disp(class(m.a))');
ip_px('ls.array.set', 'm = libstruct(''jg_mixed''); m.d = [1 2 3]; disp(class(m.d)); disp(m.d)');
ip_px('ls.nested.set', 'n = libstruct(''jg_nested''); n.p = struct(''x'', 1, ''y'', 2); v = n.p; disp(class(v)); disp(size(v))');
ip_px('ls.nested.read', 'n = libstruct(''jg_nested'', struct(''p'', struct(''x'', 1, ''y'', 2))); v = n.p; disp(class(v)); disp(size(v))');
ip_px('ls.echo.mixed', 'm = libstruct(''jg_mixed'')');
ip_px('ls.echo.nested', 'n = libstruct(''jg_nested'')');
ip_px('ls.struct.of', 's = struct(ls); disp(class(s))');
ip_px('ls.class.packed', 'disp(class(libstruct(''jg_packed'')))');
ip_px('ls.pass.twice', 'q = libstruct(''jg_point'', struct(''x'', 3, ''y'', 4)); disp(calllib(lib, ''jg_point_len'', q)); disp(calllib(lib, ''jg_point_len'', q))');

% ---- sprintf of values past 2^63, for the uint64 row
ip_pr('sprintf.2p64', 'sprintf(''%d'', 2^64)');
ip_pr('sprintf.uint64max.double', 'sprintf(''%d'', double(intmax(''uint64'')))');
ip_pr('sprintf.1e20', 'sprintf(''%d'', 1e20)');

function setv(p, v)
p.Value = v;
end

function setp(p, name, v)
p.(name) = v;
end

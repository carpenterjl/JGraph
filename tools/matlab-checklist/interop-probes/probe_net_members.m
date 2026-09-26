% probe_net_members: constructors, properties, fields, statics, indexers, methods and display.
dotnetenv("core", Version="8");
a = ip_assets();
asm = NET.addAssembly(a.assembly);

% ---- the assembly object
ip_pr('asm.class', 'asm');
ip_px('asm.disp', 'disp(asm)');
ip_pr('asm.AssemblyHandle', 'class(asm.AssemblyHandle)');
ip_pr('asm.Classes.n', 'numel(asm.Classes)');
ip_pr('asm.Classes.first', 'asm.Classes(1)');
ip_pr('asm.Enums', 'asm.Enums');
ip_pr('asm.Structures', 'asm.Structures');
ip_pr('asm.Delegates', 'asm.Delegates');
ip_pr('asm.GenericTypes', 'asm.GenericTypes');
ip_pr('asm.Interfaces', 'asm.Interfaces');
ip_pr('asm.again', 'NET.addAssembly(a.assembly) == asm');

% ---- constructors
ip_pr('ctor.none', 'JGTest.Members()');
ip_pr('ctor.none.noparens', 'JGTest.Members');
ip_pr('ctor.double', 'JGTest.Members(3).Value');
ip_pr('ctor.double.int32', 'JGTest.Members(int32(3)).Value');
ip_pr('ctor.two', 'JGTest.Members(3, ''abc'').Name');
ip_pr('ctor.two.string', 'JGTest.Members(3, "abc").Name');
ip_pr('ctor.params.int32x3', 'JGTest.Members(int32(1), int32(2), int32(3)).Name');
ip_pr('ctor.params.array', 'JGTest.Members(int32([1 2 3])).Name');
ip_pr('ctor.wrongcount', 'JGTest.Members(1, 2, 3, 4)');
ip_pr('ctor.optional.one', 'JGTest.OptionalCtor(1).B');
ip_pr('ctor.optional.two', 'JGTest.OptionalCtor(1, 9).B');
ip_pr('ctor.private', 'JGTest.Factory(''x'')');
ip_pr('ctor.factory', 'JGTest.Factory.Make(''x'').Tag');
ip_pr('ctor.static.class', 'JGTest.Statics()');
ip_pr('ctor.unknown.type', 'JGTest.NoSuchType()');
ip_pr('ctor.unknown.ns', 'NoSuchNamespace.Thing()');

% ---- properties and fields
m = JGTest.Members(2, 'm');
ip_pr('prop.get', 'm.Value');
ip_pr('prop.name', 'm.Name');
ip_pr('prop.readonly', 'm.ReadOnly');
m.Value = 5;
ip_pr('prop.set', 'm.Value');
m.Value = int8(6);
ip_pr('prop.set.int8', 'm.Value');
ip_px('prop.set.readonly', 'm.ReadOnly = 1;');
ip_px('prop.set.wrongtype', 'm.Value = ''text'';');
ip_px('prop.set.string2double', 'm.Value = "7";');
ip_px('prop.get.writeonly', 'm.WriteOnly');
m.WriteOnly = 11;
ip_pr('prop.writeonly.effect', 'm.Value');
ip_pr('field.get', 'm.Field');
m.Field = 9;
ip_pr('field.set', 'm.Field');
ip_pr('field.readonly', 'm.ReadOnlyField');
ip_px('field.set.readonly', 'm.ReadOnlyField = ''x'';');
ip_pr('field.const', 'JGTest.Members.Constant');
ip_pr('field.const.instance', 'm.Constant');
ip_pr('prop.nosuch', 'm.NoSuch');
ip_px('prop.set.nosuch', 'm.NoSuch = 1;');

% ---- statics
JGTest.Members.Reset();
ip_pr('static.field', 'JGTest.Members.StaticField');
ip_pr('static.prop', 'JGTest.Members.Counter');
ip_pr('static.method', 'JGTest.Members.Increment()');
ip_pr('static.method.noparens', 'JGTest.Members.Increment');
ip_pr('static.via.instance', 'm.Increment()');
ip_pr('static.readonly', 'JGTest.Members.StaticReadOnly');
ip_pr('static.class.prop', 'JGTest.Statics.Version');
ip_pr('static.class.method.int', 'JGTest.Statics.Twice(int32(4))');
ip_pr('static.class.method.double', 'JGTest.Statics.Twice(4)');
ip_pr('static.void.output', 'JGTest.Statics.Nothing()');
ip_px('static.void.call', 'JGTest.Statics.Nothing()');
ip_pr('static.nosuch', 'JGTest.Statics.NoSuch()');
ip_pr('static.fn.syntax', 'Twice(JGTest.Statics, 4)');

% ---- indexer
ip_pr('index.item', 'm.Item(0)');
ip_pr('index.item.int32', 'm.Item(int32(1))');
ip_pr('index.item.oob', 'm.Item(5)');
ip_pr('index.paren', 'm(1)');
ip_px('index.set.item', 'm.Item(0) = 99;');
ip_pr('index.after.set', 'm.Item(0)');
ip_px('index.set.paren', 'm(0) = 98;');

% ---- instance methods
ip_pr('method.describe', 'm.Describe()');
ip_pr('method.describe.noparens', 'm.Describe');
ip_pr('method.fn.syntax', 'Describe(m)');
ip_pr('method.add1', 'm.Add(1)');
ip_pr('method.add2', 'm.Add(1, 2)');
ip_pr('method.add.wrongcount', 'm.Add(1, 2, 3)');
ip_pr('method.add.char', 'm.Add(''x'')');
ip_px('method.void.call', 'm.Bump()');
ip_pr('method.void.output', 'm.Bump()');
ip_pr('method.tostring', 'm.ToString()');
ip_pr('method.char', 'char(m)');
ip_pr('method.string', 'string(m)');
ip_pr('method.gettype', 'm.GetType().FullName');
ip_pr('method.equals', 'm.Equals(m)');
ip_pr('method.hash', 'class(m.GetHashCode())');

% ---- class queries
ip_pr('class', 'class(m)');
ip_pr('isa.self', 'isa(m, ''JGTest.Members'')');
ip_pr('isa.object', 'isa(m, ''System.Object'')');
ip_pr('isa.handle', 'isa(m, ''handle'')');
ip_pr('isobject', 'isobject(m)');
ip_pr('isjava', 'isjava(m)');
ip_pr('isstruct', 'isstruct(m)');
ip_pr('size', 'size(m)');
ip_pr('numel', 'numel(m)');
ip_pr('isempty', 'isempty(m)');
ip_pr('isprop.Value', 'isprop(m, ''Value'')');
ip_pr('isprop.Field', 'isprop(m, ''Field'')');
ip_pr('isprop.nosuch', 'isprop(m, ''NoSuch'')');
ip_pr('ismethod.Describe', 'ismethod(m, ''Describe'')');
ip_pr('ismethod.Increment', 'ismethod(m, ''Increment'')');
ip_pr('properties', 'properties(m)');
ip_pr('fieldnames', 'fieldnames(m)');
ip_pr('methods', 'methods(m)');
ip_pr('methods.class', 'methods(''JGTest.Members'')');
ip_px('methods.full', 'methods(m, ''-full'')');
ip_px('methods.statics', 'methods(''JGTest.Statics'', ''-full'')');
ip_pr('eq.same', 'm == m');
ip_pr('eq.other', 'm == JGTest.Members(2, ''m'')');
ip_pr('isequal.same', 'isequal(m, m)');
ip_pr('isequal.other', 'isequal(m, JGTest.Members(2, ''m''))');
ip_pr('ne', 'm ~= m');
ip_pr('concat', '[m m]');
ip_pr('cell.hold', '{m, 1}');
ip_pr('struct.hold', 'struct(''a'', m)');

% ---- reference semantics
m2 = m;
m2.Value = 123;
ip_pr('ref.shared', 'm.Value');

% ---- display
ip_px('disp.echo', 'm');
ip_px('disp.disp', 'disp(m)');
ip_px('disp.display', 'display(m)');
ip_px('disp.static.class', 'JGTest.Statics');
ip_px('disp.string', 's = System.String(''abc'')');
ip_px('disp.int32.return', 'x = JGTest.Statics.Twice(int32(4))');
ip_px('disp.factory', 'f = JGTest.Factory.Make(''t'')');

% ---- save / load
f = [tempname '.mat'];
ip_px('save', 'save(f, ''m'')');
ip_px('load', 'q = load(f); disp(class(q.m))');

% ---- clear and delete
ip_px('delete', 'mm = JGTest.Members(); delete(mm); disp(exist(''mm'', ''var''))');
ip_px('clear', 'mm = JGTest.Members(); clear mm; disp(exist(''mm'', ''var''))');

% ---- static writes LAST: in R2025b, A.B.C = v with no variable A creates a struct variable A,
% which then shadows the namespace for the rest of the workspace.
ip_px('static.field.set', 'JGTest.Members.StaticField = 4;');
ip_pr('static.field.set.var', 'exist(''JGTest'', ''var'')');
ip_pr('static.field.set.class', 'class(JGTest)');
ip_pr('static.field.after', 'JGTest.Members.StaticField');
clear JGTest
ip_pr('static.field.after.clear', 'JGTest.Members.StaticField');
ip_px('static.field.set.fn', 'JGTest.Members.StaticField = 4');
clear JGTest
ip_px('static.prop.set', 'JGTest.Members.Counter = 4;');
clear JGTest
ip_pr('static.prop.after', 'JGTest.Members.Counter');
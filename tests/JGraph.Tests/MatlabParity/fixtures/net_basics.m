% net_basics.m -- the .NET interface's first steps (interop plan, stage 1): the runtime, a BCL
% constructor, static methods and properties, instance members, the class queries, and a .NET name
% in MATLAB's name resolution. Every row is an R2025b probe answer
% (tools/matlab-checklist/interop-probes). The first statement pins R2025b to .NET 8, the runtime
% JGraph runs on, so both engines call the same base class library.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

ix_chk('isNETSupported', NET.isNETSupported);
ix_chk('runtime_major', System.Environment.Version.Major);
ix_chk('dotnetenv_runtime', char(dotnetenv().Runtime));

% ---- a BCL object
s = System.String('Hello');
ix_chk('string_class', class(s));
ix_chk('string_show', s);
ix_chk('string_char', char(s));
ix_chk('string_string', string(s));
ix_chk('string_Length', s.Length);
ix_chk('string_ToUpper', s.ToUpper());
ix_chk('string_eq_char', s == 'Hello');
ix_chk('string_strcmp', strcmp(s, 'Hello'));
ix_chk('string_isequal', isequal(s, 'Hello'));
ix_chk('string_double_refused', ix_id(@() double(s)));
ix_chk('string_concat_refused', ix_id(@() [s 'x']));
ix_chk('string_Concat', System.String.Concat('a', 'b'));
ix_chk('string_Format', System.String.Format('{0}-{1}', 1, 'x'));
ix_chk('string_IsNullOrEmpty_empty', System.String.IsNullOrEmpty(''));

% ---- static methods: the overload follows the argument's class
ix_chk('Max_int32', System.Math.Max(int32(3), int32(7)));
ix_chk('Max_double', System.Math.Max(3, 7));
ix_chk('Max_mixed', System.Math.Max(int32(3), 7));
ix_chk('Sqrt', System.Math.Sqrt(2), 'rel=1e-15');
ix_chk('PI', System.Math.PI, 'rel=1e-15');
ix_chk('Int32_MaxValue', System.Int32.MaxValue);
ix_chk('Int32_Parse', System.Int32.Parse('42'));
ix_chk('handle_static', feval(@System.Math.Max, 3, 4));
ix_chk('feval_string', feval('System.Math.Max', 3, 4));
ix_chk('func2str', func2str(@System.Math.Max));

% ---- a user type: constructors, properties, fields, methods
m = JGTest.Members(2, 'm');
ix_chk('ctor_none', JGTest.Members().Value);
ix_chk('ctor_noparens', class(JGTest.Members));
ix_chk('ctor_double', JGTest.Members(3).Value);
ix_chk('ctor_int32_to_double', JGTest.Members(int32(3)).Value);
ix_chk('ctor_string_arg', JGTest.Members(3, "abc").Name);
ix_chk('ctor_nomatch', ix_id(@() JGTest.Members(1, 2, 3, 4)));
ix_chk('ctor_static_class', ix_id(@() JGTest.Statics()));
ix_chk('ctor_private', ix_id(@() JGTest.Factory('x')));
ix_chk('ctor_unknown_type', ix_id(@() JGTest.NoSuchType()));
ix_chk('ctor_unknown_ns', ix_id(@() NoSuchNamespace.Thing()));
ix_chk('prop_get', m.Value);
ix_chk('prop_Name', m.Name);
ix_chk('prop_readonly', m.ReadOnly);
m.Value = 5;
ix_chk('prop_set', m.Value);
m.Value = int8(6);
ix_chk('prop_set_int8', m.Value);
ix_chk('field_get', m.Field);
ix_chk('field_readonly', m.ReadOnlyField);
ix_chk('const_static', JGTest.Members.Constant);
ix_chk('const_instance', m.Constant);
ix_chk('static_field', JGTest.Members.StaticField);
ix_chk('static_prop', JGTest.Statics.Version);
ix_chk('static_method_int32', JGTest.Statics.Twice(int32(4)));
ix_chk('static_method_double', JGTest.Statics.Twice(4));
ix_chk('static_nosuch', ix_id(@() JGTest.Statics.NoSuch()));
ix_chk('method', m.Describe());
ix_chk('method_noparens', m.Describe);
ix_chk('method_fn_syntax', Describe(m));
ix_chk('method_overload_1', m.Add(1));
ix_chk('method_overload_2', m.Add(1, 2));
ix_chk('method_nomatch', ix_id(@() m.Add(1, 2, 3)));
ix_chk('method_char_nomatch', ix_id(@() m.Add('x')));
ix_chk('method_ToString', m.ToString());
ix_chk('method_GetType', m.GetType().FullName);
ix_chk('nosuch_member', ix_id(@() m.NoSuch));
ix_chk('char_refused', ix_id(@() char(m)));

% ---- class queries
ix_chk('class', class(m));
ix_chk('isa_self', isa(m, 'JGTest.Members'));
ix_chk('isa_Object', isa(m, 'System.Object'));
ix_chk('isa_handle', isa(m, 'handle'));
ix_chk('isobject', isobject(m));
ix_chk('isjava', isjava(m));
ix_chk('size', size(m));
ix_chk('numel', numel(m));
ix_chk('isempty', isempty(m));
ix_chk('isprop', isprop(m, 'Value'));
ix_chk('isprop_field', isprop(m, 'Field'));
ix_chk('isprop_nosuch', isprop(m, 'NoSuch'));
ix_chk('ismethod', ismethod(m, 'Describe'));
ix_chk('ismethod_static', ismethod(m, 'Increment'));
ix_chk('properties', properties(m));
ix_chk('fieldnames', fieldnames(m));
ix_chk('eq_same', m == m);
ix_chk('eq_other', m == JGTest.Members(5, 'm'));
ix_chk('isequal_other_same_values', isequal(JGTest.Members(1, 'a'), JGTest.Members(1, 'a')));
ix_chk('concat_refused', ix_id(@() [m m]));
ix_chk('paren_index_refused', ix_id(@() m(1)));
ix_chk('cell_holds', class({m, 1}));

% ---- reference semantics
m2 = m;
m2.Value = 123;
ix_chk('reference_shared', m.Value);

% ---- a .NET name in name resolution
ix_chk('which', which('System.String'));
ix_chk('exist', exist('System.String'));
ix_chk('exist_class', exist('System.String', 'class'));
ix_chk('exist_namespace', exist('System'));
ix_chk('exist_nosuch', exist('JGTest.NoSuch'));
ix_chk('namespace_alone', ix_id(@() System));
ix_chk('namespace_partial', ix_id(@() System.Collections));
ix_chk('bare_static', ix_id(@() Max(1, 2)));
ix_chk('metaclass', class(?System.String));
ix_chk('metaclass_Name', meta.class.fromName('JGTest.Members').Name);

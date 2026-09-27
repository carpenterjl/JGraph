% net_generics.m -- generic .NET types and methods (interop plan, stage 4): NET.createGeneric,
% NET.GenericClass for a nested type argument, NET.invokeGenericMethod, constraints, the class name
% MATLAB gives a generic instance, and the BCL collections a user meets first.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

b = NET.createGeneric('JGTest.Box', {'System.Double'}, 2.5);
ix_chk('box_class', class(b));
ix_chk('box_Value', b.Value);
ix_chk('box_TypeName', b.TypeName);
ix_chk('box_echo', ix_flat(evalc('b')), 'div=ADR0174');
b2 = NET.createGeneric('JGTest.Box', {'System.String'});
ix_chk('box_null_Value', b2.Value);
b2.Value = 'hi';
ix_chk('box_set_char', b2.Value);
pr = NET.createGeneric('JGTest.Pair', {'System.Int32', 'System.String'}, int32(1), 'one');
ix_chk('pair_class', class(pr));
ix_chk('pair_First', pr.First);
ix_chk('pair_Second', pr.Second);
ix_chk('typearg_string_form_refused', ix_id(@() NET.createGeneric('JGTest.Box', {'System.Collections.Generic.List<System.Double>'})));
ix_chk('typearg_GenericClass', class(NET.createGeneric('JGTest.Box', {NET.GenericClass('System.Collections.Generic.List', 'System.Double')})));
ix_chk('GenericClass_class', class(NET.GenericClass('System.Collections.Generic.List', 'System.Double')));
ix_chk('constraint_refused', ix_id(@() NET.createGeneric('JGTest.Constrained', {'System.String'})));
ix_chk('constraint_ok', class(NET.createGeneric('JGTest.Constrained', {'System.Int32'})));
ix_chk('open_generic_name_refused', ix_id(@() System.Collections.Generic.List));
ix_chk('invokeGenericMethod', NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'System.Int32'}, int32(5)));
ix_chk('invokeGenericMethod_noargs', NET.invokeGenericMethod('JGTest.GenericTools', 'TypeOf', {'System.String'}));
ix_chk('generic_method_direct_refused', ix_id(@() JGTest.GenericTools.Echo(5)));

l = NET.createGeneric('System.Collections.Generic.List', {'System.Double'}, 3);
l.Add(5);
l.Add(int8(6));
ix_chk('list_class', class(l));
ix_chk('list_Count', l.Count);
ix_chk('list_Capacity', l.Capacity);
ix_chk('list_Item', l.Item(1));
ix_chk('list_ToArray', double(l.ToArray()));
ix_chk('list_paren_refused', ix_id(@() l(1)));
ix_chk('list_echo', ix_flat(evalc('l')), 'div=ADR0174');
ix_chk('list_Add_char_refused', ix_id(@() l.Add('x')));
lr = JGTest.GenericTools.Range(int32(3));
ix_chk('list_returned_class', class(lr));
ix_chk('list_to_IEnumerable', JGTest.GenericTools.Total(lr));
ix_chk('double_to_IEnumerable_refused', ix_id(@() JGTest.GenericTools.Total([1 2 3])));
nb = JGTest.GenericTools.NestedBox(int32(2));
ix_chk('nested_class', class(nb));
ix_chk('nested_Value_class', class(nb.Value));

t = JGTest.GenericTools.Table();
ix_chk('dict_class', class(t));
ix_chk('dict_Item', t.Item('two'));
ix_chk('dict_ContainsKey', t.ContainsKey('one'));
ix_chk('dict_Keys_class', class(t.Keys));
ix_chk('dict_Count', t.Count);
d = dictionary(t);
ix_chk('dict_to_dictionary_class', class(d));
ix_chk('dict_to_dictionary_numEntries', numEntries(d));
ix_chk('dict_to_dictionary_values', values(d)');
ix_chk('dictionary_to_object_refused', ix_id(@() JGTest.Overloads.Only_Object(dictionary(["a" "b"], [1 2]))));

h = System.Collections.Hashtable();
h.Add('k', 1);
ix_chk('hashtable_Item', h.Item('k'));
ix_chk('hashtable_Count', h.Count);

% Stage 4's own probes (probe4, probe4b, probe4c): refusals, the method form, dictionary copies.
ix_chk('GenericClass_bad', ix_id(@() NET.GenericClass('No.Such.Gen', 'System.Double')));
ix_chk('GenericClass_nonstring', ix_id(@() NET.GenericClass(5)));
ix_chk('GenericClass_noargs', ix_id(@() NET.GenericClass('System.Collections.Generic.List')));
ix_chk('GenericClass_properties', numel(properties(NET.GenericClass('System.Collections.Generic.List', 'System.Double'))));
ix_chk('GenericClass_nested', class(NET.createGeneric('System.Collections.Generic.Dictionary', {'System.String', NET.GenericClass('System.Collections.Generic.List', NET.GenericClass('System.Collections.Generic.List', 'System.Int32'))})));
ix_chk('createGeneric_wrong_count', ix_id(@() NET.createGeneric('JGTest.Box', {'System.Double', 'System.Double'})));
ix_chk('createGeneric_not_generic', ix_id(@() NET.createGeneric('System.String', {'System.Double'})));
ix_chk('createGeneric_typeargs_char', ix_id(@() NET.createGeneric('JGTest.Box', 'System.Double')));
ix_chk('createGeneric_bad_typearg', ix_id(@() NET.createGeneric('JGTest.Box', {'No.Such'})));
ix_chk('createGeneric_Nullable', class(NET.createGeneric('System.Nullable', {'System.Int32'}, int32(4))));
ix_chk('invokeGenericMethod_double', NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'System.Double'}, 5));
ix_chk('invokeGenericMethod_class', class(NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'System.String'}, 's')));
ix_chk('invokeGenericMethod_nosuch', ix_id(@() NET.invokeGenericMethod('JGTest.GenericTools', 'Nope', {'System.Int32'}, 1)));
ix_chk('invokeGenericMethod_nongeneric', ix_id(@() NET.invokeGenericMethod(l, 'GetType', {'System.Int32'})));
ix_chk('invokeGenericMethod_badarg', ix_id(@() NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'System.Int32'}, 'x')));
ix_chk('invokeGenericMethod_wrong_count', ix_id(@() NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'System.Int32', 'System.Int32'}, 1)));
ix_chk('invokeGenericMethod_GenericClass_refused', ix_id(@() NET.invokeGenericMethod('JGTest.GenericTools', 'TypeOf', {NET.GenericClass('System.Collections.Generic.List', 'System.Double')})));
ix_chk('invokeGenericMethod_noncell', ix_id(@() NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', 'System.Int32', int32(5))));
ix_chk('invokeGenericMethod_bad_class', ix_id(@() NET.invokeGenericMethod('No.Such.Type', 'Echo', {'System.Int32'}, int32(5))));
ix_chk('invokeGenericMethod_bad_typearg', ix_id(@() NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'No.Such'}, int32(5))));
ix_chk('dict_lookup_char', d('two'));
ix_chk('dict_lookup_string', d("two"));
ix_chk('dict_keys', ix_try(@() keys(d)), 'div=ADR0177');
t.Add('three', 3);
ix_chk('dict_is_a_copy', numEntries(d));
di = NET.createGeneric('System.Collections.Generic.Dictionary', {'System.Int32', 'System.String'});
di.Add(int32(3), 'three');
ix_chk('dict_int_keys', keys(dictionary(di)));
ix_chk('dict_int_values', values(dictionary(di)));
ix_chk('dict_empty', numEntries(dictionary(NET.createGeneric('System.Collections.Generic.Dictionary', {'System.String', 'System.Double'}))));
ix_chk('dict_sorted', class(dictionary(NET.createGeneric('System.Collections.Generic.SortedDictionary', {'System.Int32', 'System.Double'}))));
ix_chk('interfaceView_class', class(NET.interfaceView(JGTest.Greeter(), 'JGTest.IGreeter')));
ix_chk('interfaceView_Greet', NET.interfaceView(JGTest.Greeter(), 'JGTest.IGreeter').Greet());

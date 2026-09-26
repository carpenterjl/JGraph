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
ix_chk('box_echo', ix_flat(evalc('b')));
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
ix_chk('list_echo', ix_flat(evalc('l')));
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

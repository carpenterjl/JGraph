% record: -noFigureWindows
% Open items 196, 309 and 310. A metaclass's member lists as R2025b's matlab.metadata.Method, Property,
% Event and EnumerationMember columns - their order, every attribute, the defining classes, the
% superclasses, handle's 33 methods, an enumeration's, a package's namespace - and read-only members;
% a property's class converted before its size is fitted, the implicit default of a typed property
% with none; validateattributes in R2025b's catalog words; isvalid of a value object; help of several
% names, and a property commented both above and at its end. ADR 0353.

% --- 196: the member lists ----------------------------------------------------------------------
names = {'Mc196Val', 'Mc196Handle', 'Mc196Sub', 'Mc196Enum', 'Mc196Abs', 'Mc196HSub', 'Mc196Named', 'handle'};
for k = 1:numel(names)
    n = names{k};
    mc = meta.class.fromName(n);
    t = lower(n);
    u2_chk([t '_class'], @() class(mc));
    u2_chk([t '_props'], @() strjoin(properties(mc)', ' '));
    u2_chk([t '_name'], @() mc.Name);
    u2_chk([t '_desc'], @() mc.Description);
    u2_chk([t '_detail'], @() strrep(mc.DetailedDescription, newline, '/'));
    u2_chk([t '_flags'], @() [mc.Hidden mc.Sealed mc.Abstract mc.Enumeration mc.ConstructOnLoad mc.HandleCompatible mc.RestrictsSubclassing]);
    u2_chk([t '_inferior'], @() strjoin({mc.InferiorClasses.Name}, ' '));
    u2_chk([t '_inferior_size'], @() size(mc.InferiorClasses));
    u2_chk([t '_namespace'], @() [class(mc.Namespace) ' ' mat2str(size(mc.Namespace))]);
    u2_chk([t '_aliases'], @() [class(mc.Aliases) ' ' mat2str(size(mc.Aliases))]);
    u2_chk([t '_ml_class'], @() class(mc.MethodList));
    u2_chk([t '_ml_size'], @() size(mc.MethodList));
    u2_chk([t '_ml_names'], @() strjoin({mc.MethodList.Name}, ' '));
    u2_chk([t '_ml_defining'], @() strjoin(arrayfun(@(m) m.DefiningClass.Name, mc.MethodList', 'UniformOutput', false), ' '));
    u2_chk([t '_ml_access'], @() strjoin(arrayfun(@(m) accessword(m.Access), mc.MethodList', 'UniformOutput', false), ' '));
    u2_chk([t '_ml_static'], @() [mc.MethodList.Static]);
    u2_chk([t '_ml_abstract'], @() [mc.MethodList.Abstract]);
    u2_chk([t '_ml_sealed'], @() [mc.MethodList.Sealed]);
    u2_chk([t '_ml_hidden'], @() [mc.MethodList.Hidden]);
    u2_chk([t '_ml_inputs'], @() strjoin(arrayfun(@(m) strjoin(m.InputNames', ','), mc.MethodList', 'UniformOutput', false), ' '));
    u2_chk([t '_ml_input_sizes'], @() [cellfun(@(c) size(c, 1), {mc.MethodList.InputNames}); cellfun(@(c) size(c, 2), {mc.MethodList.InputNames})]);
    u2_chk([t '_ml_outputs'], @() strjoin(arrayfun(@(m) strjoin(m.OutputNames', ','), mc.MethodList', 'UniformOutput', false), ' '));
    u2_chk([t '_ml_desc'], @() strjoin(arrayfun(@(m) m.Description, mc.MethodList', 'UniformOutput', false), ';'));
    u2_chk([t '_ml_detail'], @() strrep(strjoin(arrayfun(@(m) m.DetailedDescription, mc.MethodList', 'UniformOutput', false), ';'), newline, '/'));
    u2_chk([t '_pl_class'], @() class(mc.PropertyList));
    u2_chk([t '_pl_size'], @() size(mc.PropertyList));
    u2_chk([t '_pl_names'], @() strjoin({mc.PropertyList.Name}, ' '));
    u2_chk([t '_pl_defining'], @() strjoin(arrayfun(@(p) p.DefiningClass.Name, mc.PropertyList', 'UniformOutput', false), ' '));
    u2_chk([t '_pl_get'], @() strjoin(arrayfun(@(p) accessword(p.GetAccess), mc.PropertyList', 'UniformOutput', false), ' '));
    u2_chk([t '_pl_set'], @() strjoin(arrayfun(@(p) accessword(p.SetAccess), mc.PropertyList', 'UniformOutput', false), ' '));
    u2_chk([t '_pl_dependent'], @() [mc.PropertyList.Dependent]);
    u2_chk([t '_pl_constant'], @() [mc.PropertyList.Constant]);
    u2_chk([t '_pl_abstract'], @() [mc.PropertyList.Abstract]);
    u2_chk([t '_pl_transient'], @() [mc.PropertyList.Transient]);
    u2_chk([t '_pl_hidden'], @() [mc.PropertyList.Hidden]);
    u2_chk([t '_pl_observable'], @() [mc.PropertyList.GetObservable; mc.PropertyList.SetObservable]);
    u2_chk([t '_pl_abortset'], @() [mc.PropertyList.AbortSet]);
    u2_chk([t '_pl_noncopyable'], @() [mc.PropertyList.NonCopyable]);
    u2_chk([t '_pl_weak'], @() [mc.PropertyList.WeakHandle]);
    u2_chk([t '_pl_priority'], @() [mc.PropertyList.PartialMatchPriority]);
    u2_chk([t '_pl_hasdefault'], @() [mc.PropertyList.HasDefault]);
    u2_chk([t '_pl_defaults'], @() strjoin(arrayfun(@defaultof, mc.PropertyList', 'UniformOutput', false), ' ; '));
    u2_chk([t '_pl_getmethod'], @() cellfun(@isempty, {mc.PropertyList.GetMethod}));
    u2_chk([t '_pl_setmethod'], @() cellfun(@isempty, {mc.PropertyList.SetMethod}));
    u2_chk([t '_pl_validation'], @() strjoin(arrayfun(@validationof, mc.PropertyList', 'UniformOutput', false), ' ; '));
    u2_chk([t '_pl_desc'], @() strjoin(arrayfun(@(p) p.Description, mc.PropertyList', 'UniformOutput', false), ';'));
    u2_chk([t '_el_class'], @() class(mc.EventList));
    u2_chk([t '_el_size'], @() size(mc.EventList));
    u2_chk([t '_el_names'], @() strjoin({mc.EventList.Name}, ' '));
    u2_chk([t '_el_access'], @() strjoin(arrayfun(@(e) [accessword(e.NotifyAccess) '/' accessword(e.ListenAccess)], mc.EventList', 'UniformOutput', false), ' '));
    u2_chk([t '_el_defining'], @() strjoin(arrayfun(@(e) e.DefiningClass.Name, mc.EventList', 'UniformOutput', false), ' '));
    u2_chk([t '_el_desc'], @() strjoin(arrayfun(@(e) e.Description, mc.EventList', 'UniformOutput', false), ';'));
    u2_chk([t '_el_hidden'], @() [mc.EventList.Hidden]);
    u2_chk([t '_eml_class'], @() class(mc.EnumerationMemberList));
    u2_chk([t '_eml_size'], @() size(mc.EnumerationMemberList));
    u2_chk([t '_eml_names'], @() strjoin({mc.EnumerationMemberList.Name}, ' '));
    u2_chk([t '_eml_defining'], @() strjoin(arrayfun(@(e) e.DefiningClass.Name, mc.EnumerationMemberList', 'UniformOutput', false), ' '));
    u2_chk([t '_sl_class'], @() class(mc.SuperclassList));
    u2_chk([t '_sl_size'], @() size(mc.SuperclassList));
    u2_chk([t '_sl_names'], @() strjoin({mc.SuperclassList.Name}, ' '));
end

% --- 196: the objects ---------------------------------------------------------------------------
mv = ?Mc196Val;
u2_chk('mo_same', @() isequal(mv.Name, metaclass(Mc196Val).Name));
u2_chk('mo_question_class', @() class(?Mc196Val));
u2_chk('mo_method_props', @() strjoin(properties(mv.MethodList(1))', ' '));
u2_chk('mo_property_props', @() strjoin(properties(mv.PropertyList(1))', ' '));
u2_chk('mo_event_props', @() strjoin(properties(meta.class.fromName('Mc196Handle').EventList(1))', ' '));
u2_chk('mo_member_props', @() strjoin(properties(meta.class.fromName('Mc196Enum').EnumerationMemberList(1))', ' '));
u2_chk('mo_isobject', @() isobject(mv.MethodList));
u2_chk('mo_isstruct', @() isstruct(mv.MethodList));
u2_chk('mo_set_name', @() setname(mv));
u2_chk('mo_end', @() mv.MethodList(end).Name);
u2_chk('mo_range', @() size(mv.MethodList(2:3)));
u2_chk('mo_mask', @() strjoin({mv.MethodList([mv.MethodList.Static]).Name}, ' '));
u2_chk('mo_numel', @() numel(mv.MethodList));
u2_chk('mo_isempty_events', @() isempty(mv.EventList));
u2_chk('mo_def_class', @() class(mv.MethodList(1).DefiningClass));
u2_chk('mo_def_methods', @() numel(mv.MethodList(1).DefiningClass.MethodList));
u2_chk('mo_def_def', @() mv.MethodList(1).DefiningClass.MethodList(1).DefiningClass.Name);
u2_chk('mo_super_methods', @() numel(meta.class.fromName('Mc196Sub').SuperclassList(1).MethodList));
u2_chk('mo_super_class', @() class(meta.class.fromName('Mc196Sub').SuperclassList));
u2_chk('mo_getfield', @() getfield(mv.MethodList(1), 'Name'));
u2_chk('mo_val_class', @() class(mv.PropertyList(3).Validation));
u2_chk('mo_val_classname', @() mv.PropertyList(3).Validation.Class.Name);
u2_chk('mo_val_size_class', @() class(mv.PropertyList(3).Validation.Size));
u2_chk('mo_val_size_length', @() mv.PropertyList(3).Validation.Size(1).Length);
u2_chk('mo_val_mixed', @() class(mv.PropertyList(4).Validation.Size));
u2_chk('mo_val_fn', @() func2str(mv.PropertyList(4).Validation.ValidatorFunctions{1}));
u2_chk('mo_val_none', @() size(mv.PropertyList(1).Validation));
u2_chk('mo_getmethod', @() isa(mv.PropertyList(7).GetMethod, 'function_handle'));
u2_chk('mo_access_list', @() class(meta.class.fromName('Mc196Named').PropertyList(1).GetAccess));
u2_chk('mo_access_list_name', @() meta.class.fromName('Mc196Named').PropertyList(1).SetAccess{2}.Name);
u2_chk('mo_double_class', @() class(metaclass(1)));
u2_chk('mo_double_name', @() metaclass(1).Name);
u2_chk('mo_double_ml_class', @() class(metaclass(1).MethodList));
u2_chk('mo_fromname_nosuch', @() size(meta.class.fromName('nosuchclass196')));
d = tempname;
mkdir(fullfile(d, '+mc196pkg'));
put(fullfile(d, '+mc196pkg'), 'Thing.m', {'classdef Thing', '    properties', '        P = 1', '    end', '    methods', '        function r = go(obj)', '            r = 1;', '        end', '    end', 'end'});
addpath(d);
mp = meta.class.fromName('mc196pkg.Thing');
u2_chk('pkg_name', @() mp.Name);
u2_chk('pkg_methods', @() strjoin({mp.MethodList.Name}, ' '));
u2_chk('pkg_defining', @() strjoin(arrayfun(@(m) m.DefiningClass.Name, mp.MethodList', 'UniformOutput', false), ' '));
u2_chk('pkg_namespace_size', @() size(mp.Namespace));
u2_chk('pkg_namespace_name', @() mp.Namespace.Name);

% --- 309: a property's class before its size, and its implicit default ----------------------------
o = Pv309();
props = {'S', 'N', 'V', 'R', 'T', 'L', 'I', 'U', 'C', 'Sv', 'Sd', 'Nd', 'Nm', 'Nc', 'Pos', 'Cx', 'Free'};
for k = 1:numel(props)
    p = props{k};
    u2_chk(['pv_default_' p], @() shown(o.(p)));
end
vals = {
    'S_char', 'S', 'abc'
    'S_num', 'S', 5
    'S_numrow', 'S', [1 2]
    'S_strrow', 'S', ["a" "b"]
    'S_cell', 'S', {'a'}
    'S_cell2', 'S', {'a', 'b'}
    'S_empty', 'S', ''
    'S_logical', 'S', true
    'S_charmat', 'S', ['ab'; 'cd']
    'S_struct', 'S', struct('a', 1)
    'N_char', 'N', 'a'
    'N_charrow', 'N', 'ab'
    'N_row', 'N', [1 2]
    'N_cell', 'N', {1}
    'N_cell2', 'N', {1, 2}
    'N_str', 'N', "5"
    'N_strrow', 'N', ["5" "6"]
    'N_empty', 'N', []
    'N_int', 'N', int8(4)
    'N_logical', 'N', true
    'N_complex', 'N', 1+2i
    'N_struct', 'N', struct('a', 1)
    'V_scalar', 'V', 5
    'V_col', 'V', [1; 2; 3]
    'V_row4', 'V', [1 2 3 4]
    'V_char', 'V', 'abc'
    'V_cell', 'V', {1, 2, 3}
    'R_col', 'R', [1; 2]
    'R_empty', 'R', []
    'R_zeros', 'R', zeros(0, 3)
    'T_num', 'T', 65
    'T_str', 'T', "hi"
    'T_col', 'T', ['a'; 'b']
    'L_num', 'L', 2
    'L_char', 'L', 'a'
    'L_nan', 'L', NaN
    'I_big', 'I', 300
    'I_frac', 'I', 2.6
    'I_char', 'I', 'a'
    'U_neg', 'U', -1
    'C_num', 'C', 5
    'C_row', 'C', {1, 2}
    'Sv_char', 'Sv', 'abc'
    'Sv_col', 'Sv', ["a"; "b"]
    'Nm_scalar', 'Nm', 3
    'Nm_row', 'Nm', [1 2 3 4]
    'Pos_neg', 'Pos', -1
    'Pos_row', 'Pos', [1 2]
    'Pos_char', 'Pos', 'a'
    'Cx_str', 'Cx', "b"
    'Cx_two', 'Cx', 'ab'
    'Cx_num', 'Cx', 66
    'F_char', 'F', 'sin'
    'St_num', 'St', 1
    };
for k = 1:size(vals, 1)
    u2_chk(['pv_set_' vals{k, 1}], @() setp(vals{k, 2}, vals{k, 3}));
end
decl = {
    'Pa', 'X (1,1) char'
    'Pb', 'X (1,1) cell'
    'Pc', 'X (1,1) struct'
    'Pf', 'X double'
    'Pg', 'X string'
    'Ph', 'X (2,1) string'
    'Pi', 'X (1,1) single'
    'Pj', 'X (1,2) logical'
    'Pk', 'X (1,1) Vh309'
    'Pl', 'X (1,1) Mc196Enum'
    'Pm', 'X (2,:) double'
    'Pq', 'X (1,1) int64'
    'Pr', 'X (1,1) double {mustBePositive}'
    'Pt', 'X (1,1,2) double'
    'Pw', 'X (:,:) cell'
    'Px', 'X (1,1) uint8 = 300'
    };
for k = 1:size(decl, 1)
    put(d, ['G309' decl{k, 1} '.m'], {['classdef G309' decl{k, 1}], '    properties', ['        ' decl{k, 2}], '    end', 'end'});
end
for k = 1:size(decl, 1)
    u2_chk(['pd_' lower(decl{k, 1})], @() implicitof(['G309' decl{k, 1}]));
end

% --- 309: validateattributes --------------------------------------------------------------------
va = {
    'cls_num_char', {5, {'char'}, {}}
    'cls_num_two', {5, {'char', 'string'}, {}}
    'cls_strlist', {5, ["string", "char"], {}}
    'cls_strlist_ok', {'a', ["string", "char"], {}}
    'cls_numeric_ok', {int8(5), {'numeric'}, {}}
    'cls_numeric_char', {'a', {'numeric'}, {}}
    'cls_float_int', {int8(5), {'float'}, {}}
    'cls_integer_dbl', {5, {'integer'}, {}}
    'cls_empty_list', {5, {}, {}}
    'cls_charlist', {5, 'double', {}}
    'cls_numlist', {5, 5, {}}
    'cls_cellnum', {5, {5}, {}}
    'cls_logical', {true, {'numeric'}, {}}
    'cls_cellstr_val', {{'a'}, {'char'}, {}}
    'attr_strarray', {[1 2], {'numeric'}, ["scalar", "positive"]}
    'attr_charlist', {[1 2], {'numeric'}, 'scalar'}
    'scalartext_row', {'ab', {'char'}, {'scalartext'}}
    'scalartext_mat', {['ab'; 'cd'], {'char'}, {'scalartext'}}
    'scalartext_str2', {["a" "b"], {'string'}, {'scalartext'}}
    'scalartext_missing', {string(missing), {'string'}, {'scalartext'}}
    'scalartext_col', {['a'; 'b'], {'char'}, {'scalartext'}}
    'scalartext_empty', {'', {'char'}, {'scalartext'}}
    'scalartext_num', {5, {'numeric'}, {'scalartext'}}
    'scalartext_cell', {{'a'}, {'cell'}, {'scalartext'}}
    'scalar', {[1 2], {'numeric'}, {'scalar'}}
    'vector', {ones(2), {'numeric'}, {'vector'}}
    'row', {[1; 2], {'numeric'}, {'row'}}
    'column', {[1 2], {'numeric'}, {'column'}}
    'square', {ones(2, 3), {'numeric'}, {'square'}}
    'twod', {ones(2, 2, 2), {'numeric'}, {'2d'}}
    'threed', {ones(2, 2, 2, 2), {'numeric'}, {'3d'}}
    'nonempty', {[], {'numeric'}, {'nonempty'}}
    'nonsparse', {sparse(1), {'numeric'}, {'nonsparse'}}
    'real', {1i, {'numeric'}, {'real'}}
    'positive', {[1 -1], {'numeric'}, {'positive'}}
    'positive_scalar', {-1, {'numeric'}, {'positive'}}
    'nonnegative', {-1, {'numeric'}, {'nonnegative'}}
    'nonzero', {0, {'numeric'}, {'nonzero'}}
    'integer', {1.5, {'numeric'}, {'integer'}}
    'finite', {Inf, {'numeric'}, {'finite'}}
    'nonnan', {NaN, {'numeric'}, {'nonnan'}}
    'even', {3, {'numeric'}, {'even'}}
    'odd', {2, {'numeric'}, {'odd'}}
    'binary', {2, {'numeric'}, {'binary'}}
    'diag', {ones(2), {'numeric'}, {'diag'}}
    'increasing', {[2 1], {'numeric'}, {'increasing'}}
    'decreasing', {[1 2], {'numeric'}, {'decreasing'}}
    'nondecreasing', {[2 1], {'numeric'}, {'nondecreasing'}}
    'nonincreasing', {[1 2], {'numeric'}, {'nonincreasing'}}
    'gt_scalar', {3, {'numeric'}, {'>', 5}}
    'gt_array', {[3 6], {'numeric'}, {'>', 5}}
    'ge', {3, {'numeric'}, {'>=', 5}}
    'lt', {7, {'numeric'}, {'<', 5}}
    'le', {7, {'numeric'}, {'<=', 5.5}}
    'gt_noarg', {7, {'numeric'}, {'>'}}
    'gt_badarg', {7, {'numeric'}, {'>', 'a'}}
    'gt_char', {'a', {'char'}, {'>', 5}}
    'size', {[1 2], {'numeric'}, {'size', [1 3]}}
    'size_nan', {ones(2, 3), {'numeric'}, {'size', [NaN 2]}}
    'size_3d', {ones(2, 3), {'numeric'}, {'size', [2 3 2]}}
    'size_bad', {ones(2, 3), {'numeric'}, {'size', 'a'}}
    'numel', {[1 2], {'numeric'}, {'numel', 3}}
    'ncols', {[1 2], {'numeric'}, {'ncols', 3}}
    'nrows', {[1 2], {'numeric'}, {'nrows', 3}}
    'ndims', {[1 2], {'numeric'}, {'ndims', 3}}
    'numel_bad', {[1 2], {'numeric'}, {'numel', -1}}
    'positive_char', {'a', {'char'}, {'positive'}}
    'positive_cell', {{1}, {'cell'}, {'positive'}}
    'positive_logical', {false, {'logical'}, {'positive'}}
    'nonempty_cell', {{}, {'cell'}, {'nonempty'}}
    'nonempty_str', {"", {'string'}, {'nonempty'}}
    'func', {-1, {'numeric'}, {'positive'}, 'myfun'}
    'func_var', {-1, {'numeric'}, {'positive'}, 'myfun', 'x'}
    'func_var_idx', {-1, {'numeric'}, {'positive'}, 'myfun', 'x', 2}
    'idx', {-1, {'numeric'}, {'positive'}, 3}
    'func_empty_var', {-1, {'numeric'}, {'positive'}, '', 'x'}
    'func_var_cls', {-1, {'char'}, {}, 'myfun', 'x'}
    'func_var_idx_cls', {-1, {'char'}, {}, 'myfun', 'x', 2}
    'idx_cls', {-1, {'char'}, {}, 2}
    'func_cls', {-1, {'char'}, {}, 'myfun'}
    'var_only', {-1, {'numeric'}, {'positive'}, '', 'x', []}
    'size_func_var', {[1 2], {'numeric'}, {'size', [1 3]}, 'f', 'x', 1}
    'gt_func_var', {3, {'numeric'}, {'>', 5}, 'f', 'x', 1}
    'numel_func_var', {[1 2], {'numeric'}, {'numel', 3}, 'f', 'x', 1}
    'bad_func', {1, {'numeric'}, {}, 5.5}
    'bad_func2', {1, {'numeric'}, {}, {}}
    'bad_var', {1, {'numeric'}, {}, 'f', 5}
    'bad_idx', {1, {'numeric'}, {}, 'f', 'x', 0}
    'bad_idx2', {1, {'numeric'}, {}, 'f', 'x', 'a'}
    'toofew', {1, {'numeric'}}
    'toomany', {1, {'numeric'}, {}, 'f', 'x', 1, 2}
    'ok', {5, {'numeric'}, {'scalar', 'positive', 'integer'}}
    'order_class_first', {'a', {'numeric'}, {'scalar'}}
    'order_attrs', {[-1 2], {'numeric'}, {'scalar', 'positive'}}
    'order_attrs2', {[-1 2], {'numeric'}, {'positive', 'scalar'}}
    'handle_cls', {@sin, {'function_handle'}, {'scalar'}}
    'obj_cls', {Vh309(), {'Vh309'}, {}}
    'obj_cls_handle', {Vh309(), {'handle'}, {}}
    'obj_cls_wrong', {Vh309(), {'double'}, {}}
    'struct_cls', {struct('a', 1), {'double'}, {}}
    'multi_cls_msg', {5, {'char', 'string', 'cell'}, {}}
    'membrane_size', {ones(2, 3, 4, 5), {'numeric'}, {'size', [2 4; 3 5]}}
    'size_column', {ones(1, 3), {'numeric'}, {'size', [1; 3; 1]}}
    'size_inf', {ones(2, 3), {'numeric'}, {'size', [2 Inf]}}
    };
for k = 1:size(va, 1)
    u2_chk(['va_' va{k, 1}], @() vaout(va{k, 2}));
end

% --- 309: isvalid -------------------------------------------------------------------------------
u2_chk('isvalid_value', @() isvalid(Pv309()));
u2_chk('isvalid_handle', @() isvalid(Vh309()));
u2_chk('isvalid_struct', @() isvalid(struct('a', 1)));
u2_chk('isvalid_secret', @() isvalid(secretID('a')));
u2_chk('isvalid_deleted', @() deltest());
u2_chk('isvalid_array', @() isvalid([Vh309() Vh309()]));

% --- 310: secretID's answers and help -----------------------------------------------------------
u2_chk('sid_nargin', @() nargin('secretID'));
u2_chk('sid_nargout', @() nargout('secretID'));
u2_chk('sid_exist_class', @() exist('secretID', 'class'));
chkdiv('sid_exist', @() exist('secretID'));
u2_chk('help_two', @() size(help('Hp310', 'Hp310')));
u2_chk('help_two_builtins', @() size(help('sin', 'cos')));
u2_chk('help_three', @() size(help('Hp310', 'a', 'b')));
u2_chk('help_both', @() helpflat('Hp310.Both'));
u2_chk('help_tail', @() helpflat('Hp310.Tail'));
u2_chk('help_above', @() helpflat('Hp310.Above'));
u2_chk('help_above_two', @() helpflat('Hp310.AboveTwo'));
u2_chk('help_plain', @() helpflat('Hp310.Plain'));
u2_chk('help_slash_both', @() helpflat('Hp310/Both'));
rmpath(d);
rmdir(d, 's');

function r = accessword(a)
if ischar(a)
    r = a;
else
    r = ['{' strjoin(cellfun(@(c) c.Name, a, 'UniformOutput', false), ',') '}'];
end
end

function r = defaultof(p)
if p.HasDefault
    r = shown(p.DefaultValue);
else
    r = '-';
end
end

function r = validationof(p)
v = p.Validation;
if isempty(v)
    r = '-';
    return
end
if isempty(v.Class)
    c = '[]';
else
    c = v.Class.Name;
end
r = sprintf('%s %s %d %d', c, class(v.Size), numel(v.Size), numel(v.ValidatorFunctions));
end

function r = shown(v)
if ischar(v)
    r = ['char ' mat2str(size(v)) ' ' mat2str(double(v(:)'))];
elseif isstring(v)
    r = ['string ' mat2str(size(v)) ' ' char(strjoin(v(:)', ','))];
elseif isnumeric(v) || islogical(v)
    r = [class(v) ' ' mat2str(size(v)) ' ' mat2str(v)];
else
    r = [class(v) ' ' mat2str(size(v))];
end
end

function r = setname(mc)
m = mc.MethodList(1);
m.Name = 'zz';
r = m.Name;
end

function r = setp(p, v)
o = Pv309();
o.(p) = v;
r = shown(o.(p));
end

function r = implicitof(name)
o = feval(name);
r = shown(o.X);
end

function r = vaout(args)
validateattributes(args{:});
r = 'ok';
end

function r = deltest()
h = Vh309();
delete(h);
r = isvalid(h);
end

function r = helpflat(name)
r = strrep(help(name), newline, '/');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function chkdiv(name, fn)
% A u2_chk line whose rule is a recorded divergence: the stamp adds what JGraph prints.
fprintf('CHK|%s|%s|div=ADR0353\n', name, u2_text(fn()));
end

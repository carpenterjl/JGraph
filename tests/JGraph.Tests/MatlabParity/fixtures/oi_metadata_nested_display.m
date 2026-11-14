% Open item 319 (ADR 0358). A metaproperty with no default raises on DefaultValue
% (MATLAB:class:NoDefaultDefined, NoDefaultPermitted for an abstract one); a metaclass's member lists
% reach as deep as R2025b's handles do; an object inside a struct field or a cell shows its size and
% class name ([1×1 Mc196Val]), a member of an enumeration its name; arrayfun over a column of structs
% answers a column; mustBeNonzeroLengthText is a builtin. Probes p319, p319b (agent X's scratch).

mc = ?Mc196Val;
u9b_chk('dv_has', @() prop_of(mc, 'A', 'DefaultValue'));
u9b_chk('dv_none', @() prop_of(mc, 'B', 'DefaultValue'));
u9b_chk('dv_none_has', @() prop_of(mc, 'B', 'HasDefault'));
u9b_chk('dv_typed', @() prop_of(mc, 'C', 'DefaultValue'));
u9b_chk('dv_dependent', @() prop_of(mc, 'Dep', 'DefaultValue'));
u9b_chk('dv_constant', @() prop_of(mc, 'K', 'DefaultValue'));
u9b_chk('dv_abstract', @() prop_of(?Mc196Abs, 'Shape', 'DefaultValue'));
u9b_chk('dv_abstract_has', @() prop_of(?Mc196Abs, 'Shape', 'HasDefault'));
u9b_chk('dv_handle_none', @() prop_of(?Mc196Handle, 'Tag', 'DefaultValue'));
u9b_chk('dv_getfield', @() getfield(mp_of(mc, 'B'), 'DefaultValue'));
u9b_chk('dv_list', @() dv_list(mc));
u9b_chk('deep3_name', @() mc.PropertyList(1).DefiningClass.MethodList(1).DefiningClass.Name);
u9b_chk('deep3_count', @() numel(mc.PropertyList(1).DefiningClass.MethodList(1).DefiningClass.MethodList));
u9b_chk('deep4_name', @() mc.MethodList(1).DefiningClass.PropertyList(1).DefiningClass.MethodList(2).Name);
u9b_chk('deep5_name', @() mc.MethodList(1).DefiningClass.PropertyList(1).DefiningClass.MethodList(1).DefiningClass.PropertyList(2).Name);
u9b_chk('deep_super', @() numel(metaclass(Mc196Sub).SuperclassList(1).MethodList));
u9b_chk('deep_super_props', @() numel(metaclass(Mc196Sub).SuperclassList(1).PropertyList(1).DefiningClass.PropertyList));
v = Mc196Val(3);
h = Mc196Handle();
low = Mc196Enum.(first_enum());
u9b_chk('obj_struct', @() field_shown(struct('o', v), 'o'));
u9b_chk('obj_struct_handle', @() field_shown(struct('o', h), 'o'));
u9b_chk('obj_struct_second', @() field_shown(struct('n', 1, 'o', v), 'o'));
u9b_chk('obj_cell', @() cell_shown({v}));
u9b_chk('obj_cell_handle', @() cell_shown({h}));
u9b_chk('objarr_struct', @() field_shown(struct('o', [v v]), 'o'));
u9b_chk('objarr_cell', @() cell_shown({[v; v; v]}));
u9b_chk('objempty_struct', @() field_shown(struct('o', Mc196Val.empty), 'o'));
u9b_chk('objempty_cell', @() cell_shown({Mc196Val.empty(0, 3)}));
u9b_chk('enum_struct', @() field_shown(struct('o', low), 'o'));
u9b_chk('enum_cell', @() cell_shown({low}));
u9b_chk('enumarr_cell', @() cell_shown({[low low]}));
u9b_chk('enumarr_struct', @() field_shown(struct('o', [low low]), 'o'));
u9b_chk('meta_struct', @() field_shown(struct('o', mc), 'o'));
u9b_chk('meta_cell', @() cell_shown({mc}));
u9b_chk('metalist_struct', @() field_shown(struct('o', mc.PropertyList), 'o'));
u9b_chk('deleted_struct', @() deleted_shown());
u9b_chk('af_column', @() size(arrayfun(@(s) s.a, struct('a', {1; 2; 3}))));
u9b_chk('af_column_values', @() arrayfun(@(s) s.a, struct('a', {1; 2; 3})));
u9b_chk('af_row', @() size(arrayfun(@(s) s.a, struct('a', {1, 2, 3}))));
u9b_chk('af_matrix', @() size(arrayfun(@(s) s.a, repmat(struct('a', 1), 2, 3))));
u9b_chk('af_nd', @() size(arrayfun(@(s) s.a, repmat(struct('a', 1), [2 1 2]))));
u9b_chk('af_column_cells', @() size(arrayfun(@(s) s.a, struct('a', {1; 2; 3}), 'UniformOutput', false)));
u9b_chk('af_empty_column', @() size(arrayfun(@(s) s.a, struct('a', cell(0, 1)))));
u9b_chk('af_empty', @() size(arrayfun(@(s) s.a, struct('a', {}))));
u9b_chk('af_column_two_outputs', @() af_two(struct('a', {1; 2})));
u9b_chk('af_object_column', @() size(arrayfun(@(o) o.A, [v; v])));
u9b_chk('nzlt_char', @() nz('a'));
u9b_chk('nzlt_empty_char', @() nz(''));
u9b_chk('nzlt_string', @() nz("abc"));
u9b_chk('nzlt_empty_string', @() nz(""));
u9b_chk('nzlt_strings', @() nz(["a" "b"]));
u9b_chk('nzlt_strings_empty_element', @() nz(["a" ""]));
u9b_chk('nzlt_missing', @() nz(string(missing)));
u9b_chk('nzlt_strings_missing', @() nz(["a" missing]));
u9b_chk('nzlt_cellstr', @() nz({'a', 'b'}));
u9b_chk('nzlt_cellstr_empty', @() nz({'a', ''}));
u9b_chk('nzlt_cell_mixed', @() nz({'a', 1}));
u9b_chk('nzlt_number', @() nz(5));
u9b_chk('nzlt_logical', @() nz(true));
u9b_chk('nzlt_empty_cell', @() nz({}));
u9b_chk('nzlt_empty_strings', @() nz(strings(0)));
u9b_chk('nzlt_char_matrix', @() nz(['ab'; 'cd']));
u9b_chk('nzlt_char_column', @() nz(['a'; 'b']));
u9b_chk('nzlt_empty_double', @() nz([]));
u9b_chk('nzlt_cell_column_empty', @() nz({''; 'a'}));
u9b_chk('nzlt_blank', @() nz(' '));
u9b_chk('nzlt_cell_string', @() nz({"a"}));
u9b_chk('nzlt_cell_char_matrix', @() nz({['ab'; 'cd']}));
u9b_chk('nzlt_cell_nested', @() nz({{'a'}}));
u9b_chk('nzlt_struct', @() nz(struct('a', 'b')));
u9b_chk('nzlt_char_1x0', @() nz(blanks(0)));
u9b_chk('nzlt_char_0x1', @() nz(char(zeros(0, 1))));
u9b_chk('nzlt_cell_char_0x1', @() nz({char(zeros(0, 1))}));
u9b_chk('nzlt_none', @() nz0());
u9b_chk('nzlt_two', @() nz2());
u9b_chk('nzlt_output', @() mustBeNonzeroLengthText('a'));
u9b_chk('nzlt_argument', @() arg_check(''));
u9b_chk('nzlt_argument_number', @() arg_check(3));
u9b_chk('nzlt_argument_ok', @() arg_check('ok'));
u9b_chk('nzlt_nargin', @() nargin('mustBeNonzeroLengthText'));
u9b_chk('nzlt_nargout', @() nargout('mustBeNonzeroLengthText'));
u9b_chk('nzlt_handle', @() func2str(@mustBeNonzeroLengthText));

function r = first_enum()
m = enumeration('Mc196Enum');
r = char(m(1));
end

function r = prop_of(mc, name, field)
r = mp_of(mc, name).(field);
end

function p = mp_of(mc, name)
p = mc.PropertyList(strcmp({mc.PropertyList.Name}, name));
end

function r = dv_list(mc)
r = {mc.PropertyList.DefaultValue};
end

function r = field_shown(s, name)
% The fragment a struct's display writes after the field's name (R2025b's own line layout aside).
t = evalc('disp(s)');
r = regexp(t, [name ': ([^\n,)]*)'], 'tokens', 'once');
r = r{1};
end

function r = cell_shown(c)
% A one-element cell's display, trimmed.
r = strtrim(evalc('disp(c)'));
end

function r = deleted_shown()
h = Mc196Handle();
delete(h);
r = field_shown(struct('o', h), 'o');
end

function r = af_two(s)
[a, b] = arrayfun(@(e) deal(e.a, e.a * 2), s);
r = {size(a), size(b)};
end

function r = nz(v)
mustBeNonzeroLengthText(v);
r = 'ok';
end

function r = nz0()
mustBeNonzeroLengthText();
r = 'ok';
end

function r = nz2()
mustBeNonzeroLengthText('a', 'b');
r = 'ok';
end

function r = arg_check(x)
arguments
    x {mustBeNonzeroLengthText}
end
r = x;
end

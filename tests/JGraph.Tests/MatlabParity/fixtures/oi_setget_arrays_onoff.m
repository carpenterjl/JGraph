% record: -noFigureWindows
% Open item 129 (ADR 0234): set and get over arrays of SetGet objects and graphics handles, a plain
% class's set and get refused over arrays, addlistener, listener and notify over handle arrays,
% categorical products, struct of a categorical, isspace, isletter and isstrprop over things that are
% not text, rows of function handles refused, and matlab.lang.OnOffSwitchState's values, conversions,
% arrays and operators, in the MATLAB dialect. Lines marked div=ADR0234 are the display of get and set
% with no output, of a struct and of a cell, OnOffSwitchState's 153 methods, and where exist says a
% name lives; ADR 0235 made a graphics property's on and off read as an OnOffSwitchState exact.
global OBS129_COUNT
h1 = Hnd116(1); h2 = Hnd116(2); ha = [h1 h2];
u9b_chk('set_scalar', @() msgof(@() set(h1, 'p', 5))); u9b_chk('get_scalar', @() msgof(@() get(h1, 'p')));
u9b_chk('set_arr', @() msgof(@() set(ha, 'p', 5))); u9b_chk('get_arr', @() msgof(@() get(ha, 'p')));
u9b_chk('get_arr_all', @() msgof(@() get(ha))); u9b_chk('set_arr_all', @() msgof(@() set(ha)));
u9b_chk('get_scalar_all', @() msgof(@() get(h1))); u9b_chk('set_val', @() msgof(@() set(Val116(1), 'p', 1)));
u9b_chk('get_val', @() msgof(@() get(Val116(1), 'p'))); u9b_chk('get_val_arr', @() msgof(@() get([Val116(1) Val116(2)], 'p')));
u9b_chk('set_empty_h', @() msgof(@() set(Hnd116.empty, 'p', 1)));
g1 = SG129(1); g2 = SG129(2); ga = [g1 g2];
u9b_chk('sg_set_arr', @() sgset(ga)); u9b_chk('sg_get_arr', @() get(ga, 'p')); u9b_chk('sg_get_two', @() get(ga, {'p', 'name'}));
u9b_chk('sg_get_all', @() get(ga)); u9b_chk('sg_get_all_size', @() size(get(ga))); u9b_chk('sg_set_cells', @() sgset2(ga)); u9b_chk('sg_set_struct', @() sgset3(ga));
u9b_chk('sg_get_scalar', @() get(g1, 'p')); u9b_chk('sg_set_out', @() class(set(g1))); u9b_chk('sg_set_out_f', @() fieldnames(set(g1)));
u9b_chk('sg_set_cells_bad', @() msgof(@() set(ga, {'p'}, {3; 4; 5}))); u9b_chk('sg_get_bad', @() msgof(@() get(ga, 'zz'))); u9b_chk('sg_set_bad', @() msgof(@() set(ga, 'zz', 1)));
u9b_chk('sg_set_pairs', @() sgset4(ga)); u9b_chk('sg_get_col', @() size(get([g1; g2], 'p'))); u9b_chk('sg_get_matrix', @() size(get([g1 g2; g2 g1], 'p')));
u9b_chkdiv('sg_get_disp', @() oi_try( @() sgdisp(g1)), 'ADR0234'); u9b_chkdiv('sg_set_disp', @() oi_try( @() sgsetdisp(g1)), 'ADR0234'); u9b_chk('sg_get_empty', @() size(get(SG129.empty, 'p')));
u9b_chk('sg_partial', @() get(g1, 'na'));
u9b_chk('al_arr_nonobs', @() msgof(@() addlistener(ha, 'p', 'PostSet', @(s, e) 1)));
u9b_chk('al_scalar_nonobs', @() msgof(@() addlistener(h1, 'p', 'PostSet', @(s, e) 1)));
u9b_chk('al_arr_obs', @() alobs()); u9b_chk('al_arr_obs_class', @() alobsclass()); u9b_chk('al_arr_obs_size', @() alobssize());
u9b_chk('al_arr_event', @() alev()); u9b_chk('al_arr_event_size', @() alevsize()); u9b_chk('listener_arr', @() lisev()); u9b_chk('listener_arr_live', @() lisevlive());
u9b_chk('al_arr_nosuch', @() msgof(@() addlistener([Obs129 Obs129], 'Nope', @(s, e) 1)));
u9b_chk('al_arr_q', @() msgof(@() addlistener([Obs129 Obs129], 'q', 'PostSet', @(s, e) 1)));
u9b_chk('notify_arr', @() notarr()); u9b_chk('al_mixed', @() msgof(@() addlistener([Obs129 Obs129], {'p', 'q'}, 'PostSet', @(s, e) 1)));
u9b_chk('al_cell_props', @() alcell());
a = categorical({'a', 'b', 'a', 'c'}); b = categorical({'x', 'y', 'x', 'y'});
u9b_chk('ct_ab', @() a .* b); u9b_chk('ct_ab_cats', @() categories(a .* b)); u9b_chk('ct_aa', @() a .* a); u9b_chk('ct_aa_cats', @() categories(a .* a));
u9b_chk('ct_times', @() categories(times(a, b))); u9b_chk('ct_scalar', @() a .* categorical({'z'})); u9b_chk('ct_scalar_cats', @() categories(a .* categorical({'z'})));
u9b_chk('ct_undef', @() isundefined(categorical({'a', ''}) .* categorical({'x', 'y'})));
u9b_chk('ct_ord', @() isordinal(categorical({'a', 'b'}, 'Ordinal', true) .* categorical({'x', 'y'}, 'Ordinal', true)));
u9b_chk('ct_ord_mix', @() msgof(@() categorical({'a', 'b'}, 'Ordinal', true) .* categorical({'x', 'y'})));
u9b_chk('ct_ord_cats', @() categories(categorical({'a', 'b'}, 'Ordinal', true) .* categorical({'x', 'y'}, 'Ordinal', true)));
u9b_chk('ct_prot', @() isprotected(categorical({'a'}, 'Protected', true) .* categorical({'x'})));
u9b_chk('ct_char', @() msgof(@() a .* 'x')); u9b_chk('ct_cellstr', @() msgof(@() a .* {'x', 'y', 'x', 'y'})); u9b_chk('ct_string', @() msgof(@() a .* "x"));
u9b_chk('ct_num', @() msgof(@() a .* 2)); u9b_chk('ct_num_first', @() msgof(@() 2 .* a)); u9b_chk('ct_size', @() msgof(@() a .* categorical({'x', 'y'})));
u9b_chk('ct_mtimes', @() msgof(@() a * b)); u9b_chk('ct_expand', @() size(a' .* b)); u9b_chk('ct_expand_cats', @() categories(a' .* categorical({'x', 'y'})));
u9b_chk('ct_unused', @() categories(categorical({'a'}, {'a', 'b'}) .* categorical({'x'}, {'x', 'y'}))); u9b_chk('ct_class', @() class(a .* b));
u9b_chk('ct_three', @() categories(a .* b .* categorical({'u', 'u', 'v', 'v'}))); u9b_chk('ct_empty', @() size(categorical({}) .* categorical({})));
u9b_chk('ct_matrix', @() size(categorical([1 2; 3 4]) .* categorical([1 1; 2 2])));
u9b_chk('ct_names_num', @() categories(categorical([1 2]) .* categorical([3 4])));
u9b_chk('cs_fields', @() fieldnames(structq(a))); u9b_chk('cs_warn', @() structwarn(a)); u9b_chk('cs_codes', @() structq(a).codes);
u9b_chk('cs_codes_class', @() class(structq(categorical(1:300)).codes)); u9b_chk('cs_ord', @() structq(categorical({'a'}, 'Ordinal', true)).isOrdinal);
u9b_chk('cs_names', @() structq(a).categoryNames); u9b_chk('cs_undef', @() structq(a).undefCode); u9b_chk('cs_max', @() structq(a).maxNumCategories);
u9b_chk('cs_default', @() structq(a).defaultCodesClass); u9b_chk('cs_missing', @() structq(a).missingLabel); u9b_chk('cs_undeflabel', @() structq(a).undefLabel);
u9b_chk('cs_size', @() size(structq(a))); u9b_chk('cs_undefined', @() structq(categorical({'a', ''})).codes); u9b_chk('cs_codes_big', @() class(structq(categorical(1:70000)).codes));
u9b_chk('is_cat', @() isspace(a)); u9b_chk('is_cat_space', @() isspace(categorical({'a b'}))); u9b_chk('is_num', @() isspace([32 65 9]));
u9b_chk('is_cell', @() isspace({'a b'})); u9b_chk('is_struct', @() isspace(struct('a', 1))); u9b_chk('is_string', @() isspace("a b"));
u9b_chk('is_obj', @() isspace(Val116(1))); u9b_chk('is_objarr', @() isspace([Val116(1) Val116(2)])); u9b_chk('is_logical', @() isspace(true(1, 3)));
u9b_chk('is_int', @() isspace(int8([32 65]))); u9b_chk('is_table', @() isspace(table(1))); u9b_chk('is_handle', @() isspace(ha));
u9b_chk('il_cat', @() isletter(a)); u9b_chk('il_cell', @() isletter({'ab'})); u9b_chk('il_num', @() isletter([65 32])); u9b_chk('il_string', @() isletter("ab"));
u9b_chk('isp_cat', @() msgof(@() isstrprop(a, 'wspace'))); u9b_chk('is_empty_cat', @() isspace(categorical({})));
u9b_chk('fh_row', @() msgof(@() [@sin @cos])); u9b_chk('fh_horzcat', @() msgof(@() horzcat(@sin, @cos))); u9b_chk('fh_vertcat', @() msgof(@() vertcat(@sin, @cos)));
u9b_chk('fh_col', @() msgof(@() [@sin; @cos])); u9b_chk('fh_cat', @() msgof(@() cat(1, @sin, @cos))); u9b_chk('fh_one', @() class([@sin]));
u9b_chk('fh_empty', @() class([@sin []])); u9b_chk('fh_cell', @() class([@sin {}])); u9b_chk('fh_repmat', @() msgof(@() repmat(@sin, 1, 2)));
u9b_chk('fh_anon', @() msgof(@() [@(x) x, @(x) 2 * x])); u9b_chk('fh_num', @() msgof(@() [@sin 1])); u9b_chk('fh_horzcat_one', @() class(horzcat(@sin)));
u9b_chk('fh_cat3', @() msgof(@() cat(3, @sin, @cos))); u9b_chk('fh_char', @() msgof(@() [@sin 'a'])); u9b_chk('fh_struct', @() msgof(@() [@sin struct('a', 1)]));
u9b_chk('fh_obj', @() msgof(@() [@sin Val116(1)])); u9b_chk('fh_empty_first', @() class([[] @sin])); u9b_chk('fh_zeros', @() msgof(@() [zeros(1, 0) @sin]));
try, on = matlab.lang.OnOffSwitchState.on; off = matlab.lang.OnOffSwitchState.off; catch, on = []; off = []; end
u9b_chk('oo_class', @() class(on)); u9b_chk('oo_isenum', @() isenum(on)); u9b_chk('oo_logical', @() logical(on)); u9b_chk('oo_char', @() char(off));
u9b_chk('oo_string', @() string(on)); u9b_chk('oo_double', @() msgof(@() double(on))); u9b_chk('oo_eq_char', @() on == 'on'); u9b_chk('oo_eq_true', @() on == true);
u9b_chk('oo_eq_str', @() on == "off"); u9b_chk('oo_not', @() ~on); u9b_chk('oo_and', @() on & false); u9b_chk('oo_or', @() off | true);
u9b_chk('oo_ctor_char', @() char(matlab.lang.OnOffSwitchState('off'))); u9b_chk('oo_ctor_true', @() char(matlab.lang.OnOffSwitchState(true)));
u9b_chk('oo_ctor_num', @() char(matlab.lang.OnOffSwitchState(0))); u9b_chk('oo_ctor_bad', @() msgof(@() matlab.lang.OnOffSwitchState('maybe')));
u9b_chk('oo_ctor_two', @() msgof(@() matlab.lang.OnOffSwitchState(2))); u9b_chk('oo_ctor_str', @() char(matlab.lang.OnOffSwitchState("on")));
u9b_chk('oo_ctor_vec', @() size(matlab.lang.OnOffSwitchState([true false true])));
u9b_chk('oo_enum', @() enumlist()); u9b_chk('oo_enum_names', @() enumnames());
u9b_chk('oo_isequal', @() isequal(on, 'on')); u9b_chk('oo_isequal2', @() isequal(on, on)); u9b_chk('oo_strcmp', @() strcmp(on, 'on'));
u9b_chk('oo_ismember', @() ismember(on, {'on', 'off'})); u9b_chk('oo_if', @() ooif(on)); u9b_chk('oo_if_off', @() ooif(off)); u9b_chk('oo_switch', @() ooswitch(on));
u9b_chk('oo_row', @() class([on off])); u9b_chk('oo_row_size', @() size([on off])); u9b_chk('oo_row_logical', @() logical([on off on]));
u9b_chk('oo_disp2', @() evalc('disp(matlab.lang.OnOffSwitchState.off)'));
u9b_chkdiv('oo_methods', @() oi_try( @() methods('matlab.lang.OnOffSwitchState')), 'ADR0234'); u9b_chk('oo_superclasses', @() superclasses('matlab.lang.OnOffSwitchState'));
u9b_chk('oo_plus', @() msgof(@() on + 1)); u9b_chk('oo_cellstr', @() cellstr(on)); u9b_chk('oo_isa', @() isa(on, 'logical')); u9b_chk('oo_islogical', @() islogical(on));
u9b_chk('oo_isobject', @() isobject(on)); u9b_chk('oo_isnumeric', @() isnumeric(on)); u9b_chk('oo_mixed_char', @() msgof(@() class([on 'off'])));
u9b_chk('oo_mixed_logical', @() msgof(@() class([on true]))); u9b_chk('oo_sort', @() msgof(@() char(sort([on off]))));
u9b_chk('oo_eq_bad', @() msgof(@() on == 'maybe')); u9b_chk('oo_eq_num', @() on == 1); u9b_chk('oo_ne', @() on ~= 'off');
u9b_chk('oo_fieldnames', @() msgof(@() fieldnames(on))); u9b_chk('oo_properties', @() properties(on)); u9b_chk('oo_struct', @() msgof(@() struct(on)));
u9b_chk('oo_num2str', @() msgof(@() num2str(on))); u9b_chk('oo_sprintf', @() msgof(@() sprintf('%s', on))); u9b_chk('oo_sprintf_d', @() msgof(@() sprintf('%d', on)));
u9b_chk('oo_ax_visible', @() class(axes(figure('Visible', 'off')).Visible)); u9b_chk('oo_ax_visible_eq', @() axes(figure('Visible', 'off')).Visible == 'on');
u9b_chk('oo_ax_isenum', @() isenum(axes(figure('Visible', 'off')).Visible)); u9b_chk('oo_ax_strcmp', @() strcmp(axes(figure('Visible', 'off')).Visible, 'on'));
u9b_chk('oo_ax_set', @() axset(on)); u9b_chk('oo_isenum_char', @() isenum('on')); u9b_chk('oo_isenum_class', @() isenum(Val116(1)));
u9b_chk('oo_enum_char_class', @() msgof(@() enumeration('Val116'))); u9b_chk('oo_matlab_lang', @() exist('matlab.lang.OnOffSwitchState'));
u9b_chk('oo_which', @() class(which('matlab.lang.OnOffSwitchState')));
u9b_chkdiv('ex_istabular', @() oi_try( @() exist('istabular')), 'ADR0234'); u9b_chk('ex_isenum', @() exist('isenum')); u9b_chkdiv('ex_vartype', @() oi_try( @() exist('vartype')), 'ADR0234'); u9b_chkdiv('ex_convertvars', @() oi_try( @() exist('convertvars')), 'ADR0234');
u9b_chkdiv('ex_table2cell', @() oi_try( @() exist('table2cell')), 'ADR0234'); u9b_chkdiv('ex_innerjoin', @() oi_try( @() exist('innerjoin')), 'ADR0234'); u9b_chkdiv('ex_stack', @() oi_try( @() exist('stack')), 'ADR0234'); u9b_chkdiv('ex_join', @() oi_try( @() exist('join')), 'ADR0234');
u9b_chk('ex_repmat', @() exist('repmat')); u9b_chkdiv('ex_fillmissing', @() oi_try( @() exist('fillmissing')), 'ADR0234'); u9b_chkdiv('ex_ismissing', @() oi_try( @() exist('ismissing')), 'ADR0234'); u9b_chk('ex_whos', @() exist('whos'));
u9b_chkdiv('ex_summary', @() oi_try( @() exist('summary')), 'ADR0234'); u9b_chkdiv('ex_categorical', @() oi_try( @() exist('categorical')), 'ADR0234'); u9b_chkdiv('ex_table', @() oi_try( @() exist('table')), 'ADR0234'); u9b_chk('ex_isspace', @() exist('isspace'));


on = matlab.lang.OnOffSwitchState.on; off = matlab.lang.OnOffSwitchState.off;
u9b_chk('d_scalar', @() dsp1()); u9b_chk('d_row', @() dsp2()); u9b_chk('d_col', @() dsp3()); u9b_chk('d_empty', @() dsp4()); u9b_chk('d_disp_row', @() dsp5());
u9b_chk('d_enum_list', @() dsp6()); u9b_chkdiv('d_ax_visible', @() oi_try( @() dsp7()), 'ADR0234'); u9b_chkdiv('d_in_struct', @() oi_try( @() dsp8()), 'ADR0234'); u9b_chkdiv('d_in_cell', @() oi_try( @() dsp9()), 'ADR0234');
u9b_chk('a_mixed_char', @() class([on 'off'])); u9b_chk('a_mixed_char_v', @() char([on 'off'])); u9b_chk('a_mixed_logical', @() class([on true]));
u9b_chk('a_mixed_num', @() class([on 1])); u9b_chk('a_mixed_num2', @() msgof(@() [on 2])); u9b_chk('a_first_char', @() class(['off' on]));
u9b_chk('a_sort', @() cellstr(sort([on off on]))); u9b_chk('a_char_row', @() msgof(@() char([on off]))); u9b_chk('a_string_row', @() string([on off]));
u9b_chk('a_cellstr_row', @() cellstr([on off])); u9b_chk('a_index', @() class(subs1([on off on]))); u9b_chk('a_index_v', @() char(subs1([on off on])));
u9b_chk('a_assign', @() asg1()); u9b_chk('a_assign_bad', @() msgof(@() asg2())); u9b_chk('a_grow', @() asg3()); u9b_chk('a_numel', @() numel([on off on]));
u9b_chk('c_num2str', @() num2str(on)); u9b_chk('c_sprintf_s', @() sprintf('%s', on)); u9b_chk('c_sprintf_d', @() sprintf('%d', on)); u9b_chk('c_struct', @() structq(on));
u9b_chk('c_fieldnames', @() fieldnames(on)); u9b_chk('c_plus', @() on + 1); u9b_chk('c_minus', @() -on); u9b_chk('c_times', @() on * 2); u9b_chk('c_eq2', @() on == 2);
u9b_chk('c_double_row', @() double([on off])); u9b_chk('c_isequal_1', @() isequal(on, 1)); u9b_chk('c_isequal_true', @() isequal(on, true));
u9b_chk('c_not_row', @() cellstr(~[on off])); u9b_chk('c_not_class', @() class(~on)); u9b_chk('c_xor', @() class(xor(on, true))); u9b_chk('c_any', @() any([on off]));
u9b_chk('c_find', @() find([on off on])); u9b_chk('c_sum', @() sum([on off on])); u9b_chk('c_ctor_case', @() msgof(@() matlab.lang.OnOffSwitchState('On')));
u9b_chk('c_ctor_two', @() char(matlab.lang.OnOffSwitchState(2))); u9b_chk('c_ctor_empty', @() size(matlab.lang.OnOffSwitchState([])));
u9b_chk('c_ctor_nan', @() msgof(@() matlab.lang.OnOffSwitchState(NaN))); u9b_chk('c_ctor_cell', @() size(matlab.lang.OnOffSwitchState({'on', 'off'})));
u9b_chk('c_ctor_string_row', @() size(matlab.lang.OnOffSwitchState(["on" "off"]))); u9b_chk('c_ctor_none', @() msgof(@() matlab.lang.OnOffSwitchState()));
u9b_chk('c_isa_handle', @() isa(on, 'handle')); u9b_chk('c_isempty', @() isempty(on)); u9b_chk('c_strcmp_row', @() strcmp([on off], 'on'));
u9b_chk('c_strcmp_str', @() strcmp(on, "on")); u9b_chk('c_strcmpi', @() strcmpi(on, 'ON')); u9b_chk('c_ismember_rev', @() ismember('on', [on off]));
u9b_chk('c_switch_true', @() sw1(on)); u9b_chk('c_switch_cell', @() sw2(off)); u9b_chk('c_switch_logical_value', @() sw3(true));
u9b_chk('c_isequal_str', @() isequal(on, "on")); u9b_chk('c_eq_str_row', @() [on off] == "on"); u9b_chk('c_eq_cell', @() msgof(@() on == {'on'}));
u9b_chk('c_while', @() wh1()); u9b_chk('c_and_class', @() class(on & on)); u9b_chk('c_or_logical', @() off | off); u9b_chk('c_bare_ctor', @() msgof(@() bare()));
u9b_chkdiv('c_methods_n', @() oi_try( @() numel(methods(on))), 'ADR0234'); u9b_chk('c_isprop', @() isprop(on, 'x')); u9b_chk('c_isenum_row', @() isenum([on off]));
u9b_chk('c_class_row', @() class([on off])); u9b_chk('c_logical_class', @() class(logical(on))); u9b_chk('c_abs', @() class(abs(on)));
u9b_chk('c_enum_wrong', @() msgof(@() enumeration('matlab.lang.NoSuchEnum')));



function m = msgof(f)
try
    f();
    m = 'no error';
catch e
    m = [e.identifier ' | ' e.message];
end
end

function out = sgset(ga)
set(ga, 'p', 7);
out = [ga.p];
end

function out = sgset2(ga)
set(ga, {'p'}, {3; 4});
out = [ga.p];
end

function out = sgset3(ga)
set(ga, struct('p', 9, 'name', 'z'));
out = {ga.p, ga.name};
end

function out = sgset4(ga)
set(ga, 'p', 11, 'name', 'q');
out = {ga.p, ga.name};
end

function out = sgdisp(g)
out = evalc('get(g)');
end

function out = sgsetdisp(g)
out = evalc('set(g)');
end

function out = alobs()
global OBS129_COUNT
OBS129_COUNT = 0;
o1 = Obs129; o2 = Obs129; oa = [o1 o2];
lh = addlistener(oa, 'p', 'PostSet', @(s, e) bump129());
o1.p = 5; o2.p = 6; o1.q = 3;
out = OBS129_COUNT;
delete(lh);
end

function out = alobsclass()
oa = [Obs129 Obs129];
lh = addlistener(oa, 'p', 'PostSet', @(s, e) 1);
out = class(lh);
end

function out = alobssize()
oa = [Obs129 Obs129];
lh = addlistener(oa, 'p', 'PostSet', @(s, e) 1);
out = size(lh);
end

function out = alev()
global OBS129_COUNT
OBS129_COUNT = 0;
o1 = Obs129; o2 = Obs129; oa = [o1 o2];
lh = addlistener(oa, 'Changed', @(s, e) bump129());
o1.fire(); o2.fire(); o2.fire();
out = OBS129_COUNT;
delete(lh);
end

function out = alevsize()
oa = [Obs129 Obs129];
lh = addlistener(oa, 'Changed', @(s, e) 1);
out = {class(lh), size(lh), size(lh.Source)};
end

function out = lisev()
global OBS129_COUNT
OBS129_COUNT = 0;
o1 = Obs129; o2 = Obs129; oa = [o1 o2];
lh = listener(oa, 'Changed', @(s, e) bump129());
o1.fire(); o2.fire();
out = {OBS129_COUNT, class(lh)};
end

function out = lisevlive()
% the same with a destructor alive, when a temporary nobody binds is checked at its statement's end
keep = Live129; %#ok<NASGU>
global OBS129_COUNT
OBS129_COUNT = 0;
o1 = Obs129; o2 = Obs129; oa = [o1 o2];
lh = listener(oa, 'Changed', @(s, e) bump129());
o1.fire(); o2.fire();
out = {OBS129_COUNT, all(isvalid(lh))};
end

function out = notarr()
global OBS129_COUNT
OBS129_COUNT = 0;
o1 = Obs129; o2 = Obs129; oa = [o1 o2];
lh = addlistener(oa, 'Changed', @(s, e) bump129());
notify(oa, 'Changed');
out = OBS129_COUNT;
delete(lh);
end

function out = alcell()
global OBS129_COUNT
OBS129_COUNT = 0;
o1 = Obs129; o2 = Obs129; oa = [o1 o2];
lh = addlistener(oa, {'p'}, 'PostSet', @(s, e) bump129());
o1.p = 5; o2.p = 6;
out = OBS129_COUNT;
delete(lh);
end

function bump129()
global OBS129_COUNT
OBS129_COUNT = OBS129_COUNT + 1;
end

function s = structq(v)
w = warning('off', 'MATLAB:structOnObject');
s = struct(v);
warning(w);
end

function out = structwarn(c)
lastwarn('');
s = struct(c); %#ok<NASGU>
[msg, id] = lastwarn;
out = [id ' | ' msg];
end

function out = enumlist()
[m, s] = enumeration('matlab.lang.OnOffSwitchState');
out = {class(m), size(m), s};
end

function out = enumnames()
out = cellstr(enumeration('matlab.lang.OnOffSwitchState'));
end

function out = ooif(v)
if v
    out = 'yes';
else
    out = 'no';
end
end

function out = ooswitch(v)
switch v
    case 'on'
        out = 'was on';
    otherwise
        out = 'other';
end
end

function out = axset(v)
f = figure('Visible', 'off');
ax = axes(f);
ax.Box = v;
out = {class(ax.Box), char(ax.Box)};
close(f);
end

function t = s(v)
if iscategorical(v)
    cs = cellstr(v); t = strjoin(reshape(cs, 1, []), ',');
elseif isnumeric(v) || islogical(v)
    t = mat2str(v, 10);
elseif ischar(v)
    t = strrep(v, newline, '\n');
elseif isstring(v)
    v(ismissing(v)) = "<missing>"; t = strjoin(reshape(v, 1, []), ',');
elseif iscell(v)
    t = strjoin(cellfun(@(x) s(x), reshape(v, 1, []), 'UniformOutput', false), '|');
elseif isstruct(v)
    t = strjoin(fieldnames(v)', ',');
elseif istable(v)
    t = strjoin(v.Properties.VariableNames, ',');
else
    t = class(v);
end
end

function out = dsp1()
x = matlab.lang.OnOffSwitchState.on; %#ok<NASGU>
out = regexprep(evalc('x'), '^\n', ''); % R2025b's leading blank line is a known divergence
end

function out = dsp2()
x = [matlab.lang.OnOffSwitchState.on matlab.lang.OnOffSwitchState.off]; %#ok<NASGU>
out = regexprep(evalc('x'), '^\n', ''); % R2025b's leading blank line is a known divergence
end

function out = dsp3()
x = [matlab.lang.OnOffSwitchState.on; matlab.lang.OnOffSwitchState.off]; %#ok<NASGU>
out = regexprep(evalc('x'), '^\n', ''); % R2025b's leading blank line is a known divergence
end

function out = dsp4()
x = matlab.lang.OnOffSwitchState.empty; %#ok<NASGU>
out = regexprep(evalc('x'), '^\n', ''); % R2025b's leading blank line is a known divergence
end

function out = dsp5()
x = [matlab.lang.OnOffSwitchState.on matlab.lang.OnOffSwitchState.off]; %#ok<NASGU>
out = evalc('disp(x)');
end

function out = dsp6()
out = evalc('enumeration(''matlab.lang.OnOffSwitchState'')');
end

function out = dsp7()
f = figure('Visible', 'off');
ax = axes(f); %#ok<NASGU>
out = evalc('ax.Visible');
close(f);
end

function out = dsp8()
s.v = matlab.lang.OnOffSwitchState.on; %#ok<STRNU>
out = evalc('s');
end

function out = dsp9()
c = {matlab.lang.OnOffSwitchState.on}; %#ok<NASGU>
out = evalc('c');
end

function out = subs1(x)
out = x(2);
end

function out = asg1()
x = [matlab.lang.OnOffSwitchState.on matlab.lang.OnOffSwitchState.off];
x(2) = 'on';
out = cellstr(x);
end

function asg2()
x = [matlab.lang.OnOffSwitchState.on matlab.lang.OnOffSwitchState.off];
x(2) = 'maybe';
end

function out = asg3()
x = matlab.lang.OnOffSwitchState.on;
x(3) = true;
out = cellstr(x);
end

function out = sw1(v)
switch v
    case true
        out = 'true case';
    otherwise
        out = 'other';
end
end

function out = sw2(v)
switch v
    case {'on', 'off'}
        out = 'cell case';
    otherwise
        out = 'other';
end
end

function out = sw3(v)
switch v
    case 'on'
        out = 'text case';
    otherwise
        out = 'other';
end
end

function out = wh1()
n = 0;
v = matlab.lang.OnOffSwitchState.on;
while v
    n = n + 1;
    if n == 3
        v = matlab.lang.OnOffSwitchState.off;
    end
end
out = n;
end

function out = bare()
out = matlab.lang.OnOffSwitchState;
end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end

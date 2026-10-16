% record: -noFigureWindows
% Open item 128 (ADR 0234): the table leftovers, in the MATLAB dialect: vartype's display and answers,
% a table of no variables keeping its rows, a vartype through a dot refused, convertvars to char
% warning, table('Size', ...) with its defaults and refusals, T.Variables read and written, saving and
% loading a table and whos('-file') with R2025b's byte rules, ismissing, anymissing, fillmissing,
% rmmissing and standardizeMissing over a table, and summary's struct. Lines marked div=ADR0234 are
% this build's display of a table with variables and of summary's statistics, whos's byte counts for
% objects, saving a table to -v7.3, logical and complex sparse, and the probe printer's missing
% strings (ADR 0156); ADR 0235 made a duration's text, a bare datetime saved and a number through a
% dot exact.
T = table([1; NaN; 3], categorical({'a'; ''; 'a'}), {'x'; ''; 'z'}, ["p"; missing; "r"], [true; false; true], ...
'VariableNames', {'n', 'kind', 'name', 'str', 'flag'});
u9b_chk('vt_class', @() class(vartype('numeric'))); u9b_chk('vt_isstruct', @() isstruct(vartype('numeric'))); u9b_chk('vt_isobject', @() isobject(vartype('numeric')));
u9b_chk('vt_disp', @() evalc('disp(vartype(''numeric''))'));
u9b_chk('vt_none_arg', @() msgof(@() vartype()));
u9b_chk('vt_bad', @() msgof(@() vartype(5))); u9b_chk('vt_fields', @() msgof(@() fieldnames(vartype('numeric'))));
u9b_chk('vt_size', @() size(vartype('numeric'))); u9b_chk('vt_props', @() properties(vartype('numeric')));
u9b_chk('vt_isequal', @() isequal(vartype('numeric'), vartype('numeric')));
u9b_chk('vt_none', @() size(T(:, vartype('datetime')))); u9b_chk('vt_none_names', @() T(:, vartype('datetime')).Properties.VariableNames);
u9b_chk('vt_none_disp', @() nodisp(T)); u9b_chk('vt_none_height', @() height(T(:, vartype('datetime')))); u9b_chk('vt_none_width', @() width(T(:, vartype('datetime'))));
u9b_chk('vt_none_isempty', @() isempty(T(:, vartype('datetime')))); u9b_chk('t0_empty_idx', @() size(T(:, []))); u9b_chk('t0_logical', @() size(T(:, false(1, 5))));
u9b_chk('t0_cell', @() size(T(:, {}))); u9b_chk('t0_add', @() t0add(T)); u9b_chk('t0_add_scalar', @() t0addscalar(T)); u9b_chk('t0_cat', @() size([T(:, []) T(:, 1)]));
u9b_chk('t0_vcat', @() size([T(:, []); T(:, [])])); u9b_chk('t0_size_ctor', @() size(table('Size', [3 0], 'VariableTypes', {})));
u9b_chk('t0_rows', @() size(T([], :))); u9b_chkdiv('t0_rows_disp', @() oi_try( @() rowsdisp(T)), 'ADR0234'); u9b_chk('t0_brace', @() size(T{:, []})); u9b_chk('t0_brace_class', @() class(T{:, []}));
u9b_chk('t0_table2array', @() size(table2array(T(:, [])))); u9b_chk('t0_rownames', @() t0rownames()); u9b_chk('t0_summary', @() msgof(@() summary(T(:, []))));
u9b_chk('t0_sortrows', @() size(sortrows(T(:, [])))); u9b_chk('t0_index_rows', @() size(T(1:2, []))); u9b_chk('t0_disp_rownames', @() t0rndisp());
u9b_chk('vt_dot', @() msgof(@() T.(vartype('numeric')))); u9b_chk('vt_dot_write', @() msgof(@() vtdotwrite(T)));
u9b_chk('cv_char_warn', @() cvwarn(T)); u9b_chk('cv_char_class', @() class(cvq(T).str));
u9b_chk('cv_char_warn_cellstr', @() cvwarn2(T)); u9b_chk('cv_string_nowarn', @() cvwarn3(T));
u9b_chk('sl_class', @() slclass(T)); u9b_chk('sl_vars', @() slvars(T)); u9b_chk('sl_kind', @() slkind(T)); u9b_chk('sl_equal', @() slequal(T));
u9b_chk('sl_str', @() slstr(T)); u9b_chk('sl_rownames', @() slrownames()); u9b_chk('sl_props', @() slprops()); u9b_chk('sl_whos_name', @() slwhos(T, 'name'));
u9b_chk('sl_whos_size', @() slwhos(T, 'size')); u9b_chk('sl_whos_class', @() slwhos(T, 'class')); u9b_chk('sl_whos_fields', @() slwhosf(T));
u9b_chk('sl_whos_two', @() slwhos2(T)); u9b_chkdiv('sl_whos_disp', @() oi_try( @() slwhosdisp(T)), 'ADR0234'); u9b_chk('sl_cat_alone', @() slcat()); u9b_chk('sl_whos_cat', @() slwhoscat());
u9b_chk('sl_whos_num', @() slwhosnum()); u9b_chk('sl_whos_struct', @() slwhosstruct()); u9b_chk('sl_whos_cell', @() slwhoscell()); u9b_chk('sl_whos_bytes_num', @() slwhosbytes());
u9b_chk('sl_whos_pattern', @() slwhospat()); u9b_chk('sl_whos_missing', @() msgof(@() whos('-file', 'no_such_file_128.mat')));
u9b_chkdiv('sl_whos_v73', @() oi_try( @() msgof(@() slv73(T))), 'ADR0234'); u9b_chk('sl_dates', @() msgof(@() sldates()));
u9b_chk('im_table', @() ismissing(T)); u9b_chk('im_size', @() size(ismissing(T))); u9b_chk('im_two_col', @() ismissing(table([1 NaN; 2 3])));
u9b_chk('im_indicator', @() ismissing(table([1; -99; 3]), -99)); u9b_chk('im_indicator_cell', @() msgof(@() ismissing(T, {''})));
u9b_chk('im_int', @() ismissing(table(int8([1; 2])))); u9b_chk('im_char_var', @() ismissing(table(['ab'; '  '])));
u9b_chk('am_table', @() anymissing(T)); u9b_chk('am_none', @() anymissing(table([1; 2])));
u9b_chk('fm_constant', @() msgof(@() fillmissing(T, 'constant', 0))); u9b_chk('fm_constant_n', @() fillmissing(T, 'constant', 0, 'DataVariables', 'n').n);
u9b_chk('fm_previous', @() fmprev(T)); u9b_chk('fm_next', @() fmnext(T)); u9b_chk('fm_cells', @() fmcells(T)); u9b_chk('fm_linear', @() msgof(@() fillmissing(T, 'linear')));
u9b_chk('fm_linear_n', @() fillmissing(T, 'linear', 'DataVariables', 'n').n); u9b_chk('fm_nearest', @() fmnear(T)); u9b_chk('fm_tf', @() fmtf(T));
u9b_chk('fm_cat_const', @() fmcat(T)); u9b_chk('fm_bad_const', @() msgof(@() fillmissing(T, 'constant', 'q', 'DataVariables', 'n')));
u9b_chk('fm_replace_off', @() msgof(@() fillmissing(T, 'constant', 0, 'DataVariables', 'n', 'ReplaceValues', false)));
u9b_chk('fm_movmean', @() fillmissing(table([1; NaN; 3; NaN; 5]), 'movmean', 3).Var1); u9b_chk('fm_vartype', @() fillmissing(T, 'constant', 7, 'DataVariables', vartype('numeric')).n);
u9b_chk('rm_table', @() size(rmmissing(T))); u9b_chk('rm_table_rows', @() rmmissing(T).n); u9b_chk('rm_min2', @() size(rmmissing(T, 'MinNumMissing', 3)));
u9b_chk('rm_datavars', @() size(rmmissing(T, 'DataVariables', 'n'))); u9b_chk('rm_tf', @() rmtf(T)); u9b_chk('rm_cols', @() rmcols(T));
u9b_chk('sm_table', @() standardizeMissing(table([1; -99; 3], [-99; 2; 2]), -99).Variables); u9b_chk('sm_datavars', @() standardizeMissing(table([1; -99], [-99; 2]), -99, 'DataVariables', 'Var1').Variables);
u9b_chk('sm_cell', @() smcell());
u9b_chk('su_fields', @() fieldnames(summary(T))); u9b_chk('su_n', @() fieldnames(summary(T).n)); u9b_chk('su_n_v', @() sudeep(summary(T).n));
u9b_chk('su_kind', @() fieldnames(summary(T).kind)); u9b_chk('su_kind_v', @() sudeep(summary(T).kind)); u9b_chk('su_name', @() fieldnames(summary(T).name));
u9b_chk('su_name_v', @() sudeep(summary(T).name)); u9b_chk('su_str', @() fieldnames(summary(T).str)); u9b_chk('su_str_v', @() sudeep(summary(T).str));
u9b_chk('su_flag', @() fieldnames(summary(T).flag)); u9b_chk('su_flag_v', @() sudeep(summary(T).flag)); u9b_chk('su_units', @() suunits());
u9b_chkdiv('su_disp', @() oi_try( @() sudisp(T)), 'ADR0234'); u9b_chk('su_int', @() sudeep(summary(table(int8([1; 2; 9]))).Var1)); u9b_chk('su_two_col', @() sudeep(summary(table([1 2; 3 4])).Var1));
u9b_chkdiv('su_rownames', @() oi_try( @() sudisp2()), 'ADR0234'); u9b_chk('su_empty', @() sudeep(summary(table(zeros(0, 1))).Var1));


global VAL116_CTOR
T = table([1; NaN; 3], categorical({'a'; ''; 'a'}), {'x'; ''; 'z'}, 'VariableNames', {'n', 'kind', 'name'});
u9b_chk('vt_display_cellstr', @() vtd('cellstr')); u9b_chk('vt_display_double', @() vtd('double')); u9b_chk('vt_display_noarg', @() vtd0());
u9b_chk('vt_disp_noarg', @() evalc('disp(vartype())')); u9b_chk('vt_fieldnames', @() fieldnames(vartype('numeric')));
u9b_chk('vt_class_noarg', @() class(vartype())); u9b_chk('vt_brace', @() size(T{:, vartype('numeric')}));
u9b_chk('vt_logical', @() size(T(:, vartype('logical')))); u9b_chk('vt_char_table', @() T(:, vartype('cellstr')).Properties.VariableNames);
u9b_chkdiv('wf_disp', @() oi_try( @() whosdisp(T)), 'ADR0234'); u9b_chkdiv('wf_bytes_table', @() oi_try( @() whosbytes(T)), 'ADR0234'); u9b_chkdiv('wf_bytes_cat', @() oi_try( @() whosbytescat()), 'ADR0234');
u9b_chk('wf_struct_disp', @() whosstruct(T)); u9b_chk('wf_nargout0_bytes', @() whosmany());
u9b_chk('t0_disp_table', @() t0disp(T)); u9b_chkdiv('t0_disp_col', @() oi_try( @() t0dispcol(T)), 'ADR0234'); u9b_chk('t0_summary_disp', @() t0sum(T));
u9b_chk('ctor_direct_grow', @() ctorgrow()); u9b_chk('ctor_direct_grow_far', @() ctorgrow2()); u9b_chk('ctor_struct_grow', @() ctorgrow3());
u9b_chk('ctor_cell_grow', @() ctorgrow4());


T = table([1; NaN; 3], categorical({'a'; ''; 'a'}), {'x'; ''; 'z'}, ["p"; missing; "r"], [true; false; true], ...
'VariableNames', {'n', 'kind', 'name', 'str', 'flag'});
u9b_chk('fp_n', @() fv(fillmissing(T, 'previous'), 'n')); u9b_chk('fp_kind', @() fv(fillmissing(T, 'previous'), 'kind'));
u9b_chk('fp_name', @() fv(fillmissing(T, 'previous'), 'name')); u9b_chk('fp_str', @() fv(fillmissing(T, 'previous'), 'str'));
u9b_chk('fp_flag', @() fv(fillmissing(T, 'previous'), 'flag'));
u9b_chk('fn_n', @() fv(fillmissing(T, 'next'), 'n')); u9b_chk('fn_kind', @() fv(fillmissing(T, 'next'), 'kind'));
u9b_chk('fn_name', @() fv(fillmissing(T, 'next'), 'name')); u9b_chk('fn_str', @() fv(fillmissing(T, 'next'), 'str'));
u9b_chk('fc_n', @() fv(fillmissing(T(:, 1:4), 'constant', {0, 'b', 'w', "s"}), 'n'));
u9b_chk('fc_kind', @() fv(fillmissing(T(:, 1:4), 'constant', {0, 'b', 'w', "s"}), 'kind'));
u9b_chk('fc_kind_cats', @() categories(fillmissing(T(:, 1:4), 'constant', {0, 'b', 'w', "s"}).kind)');
u9b_chk('fc_name', @() fv(fillmissing(T(:, 1:4), 'constant', {0, 'b', 'w', "s"}), 'name'));
u9b_chk('fc_str', @() fv(fillmissing(T(:, 1:4), 'constant', {0, 'b', 'w', "s"}), 'str'));
u9b_chk('fc_count', @() msgof(@() fillmissing(T(:, 1:4), 'constant', {0, 'b'})));
u9b_chk('fc_flag_num', @() fv(fillmissing(T, 'constant', 0, 'DataVariables', 'flag'), 'flag'));
u9b_chk('fc_name_char', @() fv(fillmissing(T, 'constant', 'q', 'DataVariables', 'name'), 'name'));
u9b_chk('fc_name_str', @() fv(fillmissing(T, 'constant', "q", 'DataVariables', 'name'), 'name'));
u9b_chk('fc_str_char', @() fv(fillmissing(T, 'constant', 'q', 'DataVariables', 'str'), 'str'));
u9b_chk('fc_str_num', @() msgof(@() fillmissing(T, 'constant', 1, 'DataVariables', 'str')));
u9b_chk('fc_kind_cat', @() fv(fillmissing(T, 'constant', categorical({'a'}), 'DataVariables', 'kind'), 'kind'));
u9b_chk('fc_name_num', @() msgof(@() fillmissing(T, 'constant', 1, 'DataVariables', 'name')));
u9b_chk('fr_names', @() fillmissing(T, 'constant', 0, 'DataVariables', 'n', 'ReplaceValues', false).Properties.VariableNames);
u9b_chk('fr_new', @() fillmissing(T, 'constant', 0, 'DataVariables', 'n', 'ReplaceValues', false).n_filled);
u9b_chk('fr_two', @() fillmissing(T, 'previous', 'DataVariables', {'n', 'kind'}, 'ReplaceValues', false).Properties.VariableNames);
u9b_chk('fr_tf', @() frtf(T));
u9b_chk('fm_movmean_err', @() msgof(@() fillmissing(T, 'movmean', 3)));
u9b_chk('fm_spline_err', @() msgof(@() fillmissing(T, 'spline')));
u9b_chk('fm_endvalues', @() fillmissing(table([NaN; 2; NaN]), 'linear', 'EndValues', 'nearest').Var1);
u9b_chk('fm_two_col', @() fillmissing(table([1 NaN; NaN 4; 5 6]), 'previous').Var1);
u9b_chk('fm_tf_datavars', @() fmtfdv(T));
u9b_chk('fm_badvar', @() msgof(@() fillmissing(T, 'previous', 'DataVariables', 'nope')));
u9b_chk('fm_nomethod', @() msgof(@() fillmissing(T)));
u9b_chk('fm_rownames', @() fillmissing(table([1; NaN], 'RowNames', {'a', 'b'}), 'previous').Properties.RowNames);
u9b_chk('im_cell_ind', @() ismissing(T, {''}));
u9b_chk('im_num_ind', @() ismissing(T, -99));
u9b_chk('im_nan_ind', @() ismissing(T, NaN));
u9b_chk('im_str_ind', @() ismissing(T, "p"));
u9b_chk('im_char_ind', @() ismissing(T, 'x'));
u9b_chk('im_two_ind', @() ismissing(table([1; -99; 3], {'a'; 'NA'; 'b'}), {-99, 'NA'}));
u9b_chk('im_cat_ind', @() ismissing(T, 'a'));
u9b_chk('im_mixed_ind', @() ismissing(T, {NaN, ''}));
u9b_chk('im_nd', @() size(ismissing(table(zeros(2, 0)))));
u9b_chk('am_flag', @() anymissing(T(:, 'flag')));
u9b_chk('am_empty', @() anymissing(T(:, [])));
u9b_chk('rm_min5', @() size(rmmissing(T, 'MinNumMissing', 5)));
u9b_chk('rm_min4', @() size(rmmissing(T, 'MinNumMissing', 4)));
u9b_chk('rm_cols_tf', @() rmcoltf(T));
u9b_chk('rm_names', @() rmmissing(T).Properties.VariableNames);
u9b_chk('rm_two_col', @() size(rmmissing(table([1 NaN; 2 3]))));
u9b_chk('rm_rownames', @() rmmissing(table([1; NaN], 'RowNames', {'a', 'b'})).Properties.RowNames);
u9b_chk('rm_dv_tf', @() rmdvtf(T));
u9b_chk('sm_text', @() fv(standardizeMissing(T, 'x'), 'name'));
u9b_chkdiv('sm_str', @() oi_try( @() fv(standardizeMissing(T, "p"), 'str')), 'ADR0234');
u9b_chk('sm_mixed', @() fv(standardizeMissing(table([1; -99], {'a'; 'NA'}), {-99, 'NA'}), 'Var2'));
u9b_chk('sm_mixed_num', @() fv(standardizeMissing(table([1; -99], {'a'; 'NA'}), {-99, 'NA'}), 'Var1'));
u9b_chk('sm_cat', @() fv(standardizeMissing(T, 'a'), 'kind'));
u9b_chk('tv_num', @() table([1; 2], [3; 4]).Variables);
u9b_chk('tv_mixed', @() msgof(@() T.Variables));
u9b_chk('tv_text', @() table({'a'; 'b'}, {'c'; 'd'}).Variables);
u9b_chk('tv_write', @() tvwrite());
u9b_chk('tv_write_bad', @() msgof(@() tvwritebad()));
u9b_chk('tv_cat', @() table(categorical({'a'; 'b'}), categorical({'c'; 'd'})).Variables);
u9b_chk('tv_empty', @() size(T(:, []).Variables));
u9b_chk('tv_int', @() class(table(int8([1; 2]), [3; 4]).Variables));
u9b_chk('tv_props', @() any(strcmp(properties(T), 'Variables')));
types = {'double', 'single', 'int8', 'uint16', 'logical', 'string', 'cellstr', 'cell', 'categorical', 'char', 'datetime', 'duration', 'struct'};
for k = 1:numel(types)
oi_chk(['ts_' types{k}], @() tsz(types{k}), {'ts_string'});
end
u9b_chk('ts_names', @() table('Size', [1 2], 'VariableTypes', {'double', 'double'}).Properties.VariableNames);
u9b_chk('ts_named', @() table('Size', [1 2], 'VariableTypes', {'double', 'double'}, 'VariableNames', {'a', 'b'}).Properties.VariableNames);
u9b_chk('ts_notypes', @() msgof(@() table('Size', [2 2])));
u9b_chk('ts_mismatch', @() msgof(@() table('Size', [2 2], 'VariableTypes', {'double'})));
u9b_chk('ts_badtype', @() msgof(@() table('Size', [2 1], 'VariableTypes', {'nope'})));
u9b_chk('ts_badsize', @() msgof(@() table('Size', [2 1 1], 'VariableTypes', {'double'})));
u9b_chk('ts_rows_mismatch', @() msgof(@() table('Size', [2 0], 'VariableTypes', {}, 'RowNames', {'a'})));
u9b_chk('ts_string_types', @() table('Size', [1 1], 'VariableTypes', "double").Var1);
u9b_chk('ts_with_data', @() msgof(@() table([1;2], 'Size', [2 1], 'VariableTypes', {'double'})));
u9b_chk('ts_names_count', @() msgof(@() table('Size', [1 2], 'VariableTypes', {'double', 'double'}, 'VariableNames', {'a'})));
u9b_chk('ts_zero_rows', @() size(table('Size', [0 2], 'VariableTypes', {'double', 'string'})));
u9b_chk('ts_zero_rows_cls', @() class(table('Size', [0 2], 'VariableTypes', {'double', 'string'}).Var2));
u9b_chk('t0x_scalar_full', @() t0full());
u9b_chk('t0x_row', @() msgof(@() t0row(T)));
u9b_chk('t0x_col2', @() t0col2(T));
u9b_chk('t0_isempty', @() isempty(T(:, [])));
u9b_chk('t0_numel', @() numel(T(:, [])));
u9b_chk('t0_brace_rows', @() size(T{1:2, []}));
u9b_chk('t0_paren_write', @() t0paren(T));
u9b_chk('t0_delete_all', @() t0del(T));
u9b_chk('t0_vertcat_mixed', @() msgof(@() [T(:, []); T(1, 1)]));
u9b_chk('t0_rows_index', @() t0rowsidx(T));
u9b_chk('t0_row_names', @() t0rn());
u9b_chk('vt_noarg_sel', @() size(T(:, vartype())));
u9b_chk('vt_brace_none', @() size(T{:, vartype('datetime')}));
u9b_chk('vt_isequal_diff', @() isequal(vartype('numeric'), vartype('double')));
u9b_chk('vt_string_arg', @() size(T(:, vartype("string"))));
u9b_chk('vt_methods', @() mlist(vartype('numeric')));
u9b_chk('wh_ws', @() whws());
u9b_chk('wh_ws_fields', @() whwsf());
vals = {struct('ab', 1), struct('a', 1, 'b', 2), struct('abc', 1), struct('a', 1, 'bcd', 2), {}, {{1}}, {'abc'}, struct([]), ...
struct(), struct('a', struct('b', 1)), {int8(1)}, sparse(logical([1 0; 0 1])), 'COMPLEX_SPARSE', [], ['ab'; 'cd'], ...
zeros(2, 3, 4), {1, 2}, cell(2, 2), struct('a', {}), {struct('a', 1)}, true, single(1), int64([1 2 3]), 1:0, ...
struct('a', {1, 2, 3}), {[1 2 3]}, sparse(3, 3), {''}};
for k = 1:numel(vals)
oi_chk(sprintf('wh_file_%02d', k), @() whfb(vals{k}), {'wh_file_12', 'wh_file_13'});
oi_chk(sprintf('wh_mem_%02d', k), @() whm(vals{k}), {'wh_mem_12', 'wh_mem_13'});
end
u9b_chk('wh_disp_order', @() whdisp());
u9b_chk('wh_disp_wide', @() whdispwide());
u9b_chk('wh_noout_none', @() whnone());


T = table([1; NaN; 3], categorical({'a'; ''; 'a'}), {'x'; ''; 'z'}, ["p"; missing; "r"], [true; false; true], ...
'VariableNames', {'n', 'kind', 'name', 'str', 'flag'});
u9b_chk('props_table', @() properties(T));
u9b_chk('props_rownames', @() properties(table([1; 2], 'RowNames', {'a', 'b'})));
u9b_chk('fieldnames_table', @() fieldnames(T));
u9b_chk('prealloc_char_warn', @() prewarn());
u9b_chk('dot_number', @() T.(2));
u9b_chk('dot_number_write', @() dotnumw(T));
u9b_chk('dot_row', @() rowdot());
u9b_chk('su_dt', @() sudeep(summary(table(datetime(2020, 1, [1; 2; 3]))).Var1));
u9b_chk('su_dur', @() sudeep(summary(table(seconds([1; NaN; 3]))).Var1));
u9b_chk('su_char', @() sudeep(summary(table(['ab'; 'cd'])).Var1));
u9b_chk('su_cell', @() sudeep(summary(table({1; 'a'})).Var1));
u9b_chk('su_struct', @() msgof(@() sudeep(summary(table(struct('a', {1; 2}))).Var1)));
u9b_chk('su_single', @() sudeep(summary(table(single([1; NaN; 2]))).Var1));
u9b_chk('su_cat_ord', @() sudeep(summary(table(categorical({'b'; 'a'}, {'b', 'a'}, 'Ordinal', true))).Var1));
u9b_chk('su_logical2', @() sudeep(summary(table([true false; false false])).Var1));
u9b_chk('su_nested', @() msgof(@() summary(table(table([1; 2])))));
u9b_chk('su_rowtimes', @() fieldnames(summary(timetable(seconds([1; 2]), [3; 4]))));
u9b_chk('br_int', @() class(T{:, 'n'}));
u9b_chk('br_int8_double', @() brcls(table(int8([1; 2]), [3.5; 4])));
u9b_chk('br_cat_cat', @() brcls(table(categorical({'a'; 'b'}), categorical({'c'; 'd'}))));
u9b_chk('br_str_str', @() brcls(table(["a"; "b"], ["c"; "d"])));
u9b_chk('br_str_cellstr', @() brcls(table(["a"; "b"], {'c'; 'd'})));
u9b_chk('br_logical_double', @() brcls(table([true; false], [3; 4])));
u9b_chk('br_char_double', @() msgof(@() brcls(table(['ab'; 'cd'], [3; 4]))));
u9b_chk('vw_int8', @() vwint8());
u9b_chk('vw_height', @() msgof(@() vwheight()));
u9b_chk('vw_cat', @() msgof(@() vwcat()));
u9b_chk('vw_text', @() vwtext());
u9b_chk('wh_global', @() whglobal());
u9b_chk('su_noout_ans', @() suans(T));
u9b_chk('vt_disp_cat', @() vtcls());
u9b_chk('table_size_names_str', @() table('Size', [1 1], 'VariableTypes', {'double'}, 'VariableNames', "a").Properties.VariableNames);
u9b_chk('cv_char_cat', @() cvcat(T));
u9b_chk('cv_char_num', @() cvnum(T));



function m = msgof(f)
try
    f();
    m = 'no error';
catch e
    m = [e.identifier ' | ' e.message];
end
end

function out = nodisp(T)
T0 = T(:, vartype('datetime')); %#ok<NASGU>
out = regexprep(evalc('T0'), '^\n', ''); % R2025b's leading blank line is a known divergence
end

function out = rowsdisp(T)
T0 = T([], 1:2); %#ok<NASGU>
out = evalc('T0');
end

function out = t0add(T)
T0 = T(:, []);
T0.x = [7; 8; 9];
out = {size(T0), T0.x};
end

function out = t0addscalar(T)
T0 = T(:, []);
T0.x = 5;
out = {size(T0), T0.x};
end

function out = t0rownames()
T0 = table('Size', [2 0], 'VariableTypes', {}, 'RowNames', {'r1', 'r2'});
out = {size(T0), T0.Properties.RowNames};
end

function out = t0rndisp()
T0 = table('Size', [2 0], 'VariableTypes', {}, 'RowNames', {'r1', 'r2'}); %#ok<NASGU>
out = regexprep(evalc('T0'), '^\n', ''); % R2025b's leading blank line is a known divergence
end

function vtdotwrite(T)
T.(vartype('numeric')) = 5;
end

function out = cvwarn(T)
lastwarn('');
T2 = convertvars(T, 'str', 'char'); %#ok<NASGU>
[msg, id] = lastwarn;
out = [id ' | ' msg];
end

function T2 = cvq(T)
w = warning('off', 'MATLAB:table:convertvars:ConvertCharWarning');
T2 = convertvars(T, 'str', 'char');
warning(w);
end

function out = cvwarn2(T)
lastwarn('');
T2 = convertvars(T, 'name', 'char'); %#ok<NASGU>
[msg, id] = lastwarn;
out = [id ' | ' msg];
end

function out = cvwarn3(T)
lastwarn('');
T2 = convertvars(T, 'name', 'string'); %#ok<NASGU>
[msg, id] = lastwarn;
out = [id ' | ' msg];
end

function f = savet(T)
f = [tempname '.mat'];
save(f, 'T');
end

function out = slclass(T)
f = savet(T); S = load(f); delete(f);
out = class(S.T);
end

function out = slvars(T)
f = savet(T); S = load(f); delete(f);
out = S.T.Properties.VariableNames;
end

function out = slkind(T)
f = savet(T); S = load(f); delete(f);
out = {class(S.T.kind), cellstr(S.T.kind)', class(S.T.name), class(S.T.str), class(S.T.flag)};
end

function out = slequal(T)
f = savet(T); S = load(f); delete(f);
out = isequaln(S.T, T);
end

function out = slstr(T)
f = savet(T); S = load(f); delete(f);
out = S.T.str;
end

function out = slrownames()
T = table([1; 2], 'RowNames', {'a', 'b'});
f = savet(T); S = load(f); delete(f);
out = S.T.Properties.RowNames;
end

function out = slprops()
T = table([1; 2], [3; 4], 'VariableNames', {'u', 'v'});
T.Properties.VariableUnits = {'m', 's'};
T.Properties.Description = 'two';
T.Properties.VariableDescriptions = {'first', ''};
f = savet(T); S = load(f); delete(f);
out = {S.T.Properties.VariableUnits, S.T.Properties.Description, S.T.Properties.VariableDescriptions};
end

function out = slwhos(T, field)
f = savet(T); w = whos('-file', f); delete(f);
out = w.(field);
end

function out = slwhosf(T)
f = savet(T); w = whos('-file', f); delete(f);
out = fieldnames(w);
end

function out = slwhos2(T)
f = [tempname '.mat']; x = 5; c = categorical({'a'}); %#ok<NASGU>
save(f, 'T', 'x', 'c'); w = whos('-file', f); delete(f);
out = {w.name; w.class};
end

function out = slwhosdisp(T)
f = [tempname '.mat']; x = 5; %#ok<NASGU>
save(f, 'T', 'x');
out = evalc('whos(''-file'', f)');
delete(f);
out = regexprep(out, '\d+  ', 'B  ');
end

function out = slcat()
c = categorical({'a', 'b', 'a'}, {'a', 'b', 'c'}, 'Ordinal', true);
f = [tempname '.mat']; save(f, 'c'); S = load(f); delete(f);
out = {class(S.c), categories(S.c)', isordinal(S.c), double(S.c)};
end

function out = slwhoscat()
c = categorical({'a', 'b', 'a'}); %#ok<NASGU>
f = [tempname '.mat']; save(f, 'c'); w = whos('-file', f); delete(f);
out = {w.name, w.size, w.class};
end

function out = slwhosnum()
x = magic(3); %#ok<NASGU>
f = [tempname '.mat']; save(f, 'x'); w = whos('-file', f); delete(f);
out = {w.name, w.size, w.bytes, w.class, w.global, w.sparse, w.complex, w.persistent};
end

function out = slwhosstruct()
s = struct('a', {1, 2}); %#ok<NASGU>
f = [tempname '.mat']; save(f, 's'); w = whos('-file', f); delete(f);
out = {w.name, w.size, w.bytes, w.class};
end

function out = slwhoscell()
c = {1, 'ab'; 2, 3}; %#ok<NASGU>
f = [tempname '.mat']; save(f, 'c'); w = whos('-file', f); delete(f);
out = {w.name, w.size, w.bytes, w.class};
end

function out = slwhosbytes()
x = 1:10; y = 'abc'; z = int16([1 2]); q = 1 + 2i; sp = sparse([1 0; 0 2]); b = true(2); %#ok<NASGU>
f = [tempname '.mat']; save(f, 'x', 'y', 'z', 'q', 'sp', 'b'); w = whos('-file', f); delete(f);
out = {w.name; w.bytes; w.complex; w.sparse};
end

function out = slwhospat()
x = 1; y = 2; xx = 3; %#ok<NASGU>
f = [tempname '.mat']; save(f, 'x', 'y', 'xx'); w = whos('-file', f, 'x*'); delete(f);
out = {w.name};
end

function out = slv73(T)
f = [tempname '.mat']; save(f, 'T', '-v7.3'); w = whos('-file', f); S = load(f); delete(f);
out = {w.class, class(S.T)};
end

function out = sldates()
d = datetime(2020, 1, 1); %#ok<NASGU>
f = [tempname '.mat']; save(f, 'd'); S = load(f); delete(f);
out = class(S.d);
end

function out = fmprev(T)
F = fillmissing(T, 'previous');
out = {F.n', cellstr(F.kind)', F.name', F.str'};
end

function out = fmnext(T)
F = fillmissing(T, 'next');
out = {F.n', cellstr(F.kind)', F.name', F.str'};
end

function out = fmcells(T)
F = fillmissing(T(:, 1:4), 'constant', {0, 'b', 'w', "s"});
out = {F.n', cellstr(F.kind)', F.name', F.str'};
end

function out = fmnear(T)
F = fillmissing(T, 'nearest');
out = {F.n', cellstr(F.kind)'};
end

function out = fmtf(T)
[F, tf] = fillmissing(T, 'previous'); %#ok<ASGLU>
out = tf;
end

function out = fmcat(T)
F = fillmissing(T, 'constant', "a", 'DataVariables', 'kind');
out = cellstr(F.kind)';
end

function out = rmtf(T)
[R, tf] = rmmissing(T); %#ok<ASGLU>
out = tf;
end

function out = rmcols(T)
R = rmmissing(T, 2);
out = R.Properties.VariableNames;
end

function out = smcell()
T = table({'a'; 'NA'}, [1; 2]);
S = standardizeMissing(T, {'NA'});
out = S.Var1;
end

function out = sudeep(st)
f = fieldnames(st);
out = '';
for k = 1:numel(f)
    v = st.(f{k});
    out = [out f{k} '=' class(v) mat2str(size(v)) ':' txt(v) ';']; %#ok<AGROW>
end
end

function t = txt(v)
if iscategorical(v)
    c = cellstr(v); t = strjoin(c(:)', ',');
elseif isnumeric(v) || islogical(v)
    t = mat2str(v, 6);
elseif ischar(v)
    t = mat2str(v);
elseif isstring(v)
    v(ismissing(v)) = "<missing>"; t = strjoin(v(:)', ',');
elseif iscell(v)
    t = ['{' strjoin(cellfun(@txt, v(:)', 'UniformOutput', false), '|') '}'];
elseif isstruct(v)
    t = ['struct:' strjoin(fieldnames(v)', ',')];
elseif isdatetime(v) || isduration(v)
    t = strjoin(cellstr(string(v(:)')), ',');
else
    t = class(v);
end
end

function out = suunits()
T = table([1; 2], 'VariableNames', {'u'});
T.Properties.VariableUnits = {'m'};
T.Properties.VariableDescriptions = {'length'};
out = sudeep(summary(T).u);
end

function out = sudisp(T)
out = evalc('summary(T)');
end

function out = sudisp2()
T = table([1; 2], categorical({'a'; 'b'}), 'RowNames', {'r1', 'r2'}); %#ok<NASGU>
out = evalc('summary(T)');
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
    t = ['struct:' strjoin(fieldnames(v)', ',')];
elseif istable(v)
    t = strjoin(v.Properties.VariableNames, ',');
else
    t = class(v);
end
end

function out = vtd(kind)
v = vartype(kind); %#ok<NASGU>
out = regexprep(evalc('v'), '^\n', ''); % R2025b's leading blank line is a known divergence
end

function out = vtd0()
v = vartype(); %#ok<NASGU>
out = regexprep(evalc('v'), '^\n', ''); % R2025b's leading blank line is a known divergence
end

function out = whosdisp(T)
f = [tempname '.mat']; x = 5; c = categorical({'a'}); s = 'ab'; %#ok<NASGU>
save(f, 'T', 'x', 'c', 's');
out = evalc('whos(''-file'', f)');
delete(f);
end

function out = whosbytes(T)
f = [tempname '.mat']; save(f, 'T'); w = whos('-file', f); delete(f);
out = {w.bytes, w.size, w.class};
end

function out = whosbytescat()
c = categorical({'a', 'b', 'a'}); %#ok<NASGU>
f = [tempname '.mat']; save(f, 'c'); w = whos('-file', f); delete(f);
out = {w.bytes, w.size, w.class};
end

function out = whosstruct(T)
f = [tempname '.mat']; save(f, 'T'); w = whos('-file', f); delete(f);
out = {w.global, w.sparse, w.complex, w.persistent, class(w.nesting), fieldnames(w.nesting)', size(w)};
end

function out = whosmany()
x = 1:3; y = {1}; z = struct('a', 1); %#ok<NASGU>
f = [tempname '.mat']; save(f, 'x', 'y', 'z'); w = whos('-file', f); delete(f);
out = {w.name; w.bytes};
end

function out = t0disp(T)
T0 = T(:, []); %#ok<NASGU>
out = evalc('disp(T0)');
end

function out = t0dispcol(T)
T0 = T([], :); %#ok<NASGU>
out = evalc('disp(T0)');
end

function out = t0sum(T)
T0 = T(:, []); %#ok<NASGU>
out = evalc('summary(T0)');
end

function out = ctorgrow()
global VAL116_CTOR
a = [Val116(2) Val116(3)];
VAL116_CTOR = 0;
a(5).p = 9;
out = {VAL116_CTOR, [a.p]};
end

function out = ctorgrow2()
global VAL116_CTOR
a = [Val116(2) Val116(3)];
VAL116_CTOR = 0;
a(5) = Val116(9);
out = {VAL116_CTOR, [a.p]};
end

function out = ctorgrow3()
global VAL116_CTOR
st.list = [Val116(2) Val116(3)];
VAL116_CTOR = 0;
st.list(5).p = 9;
out = {VAL116_CTOR, [st.list.p]};
end

function out = ctorgrow4()
global VAL116_CTOR
c = {[Val116(2) Val116(3)]};
VAL116_CTOR = 0;
c{1}(4).p = 9;
out = {VAL116_CTOR, [c{1}.p]};
end

function out = fv(F, name)
v = F.(name);
out = sprintf('%s%s:%s', class(v), mat2str(size(v)), txt(v));
end

function out = frtf(T)
[~, tf] = fillmissing(T, 'constant', 0, 'DataVariables', 'n', 'ReplaceValues', false);
out = tf;
end

function out = fmtfdv(T)
[~, tf] = fillmissing(T, 'previous', 'DataVariables', {'n', 'kind'});
out = tf;
end

function out = rmcoltf(T)
[R, tf] = rmmissing(T, 2);
out = {R.Properties.VariableNames, tf};
end

function out = rmdvtf(T)
[~, tf] = rmmissing(T, 'DataVariables', 'flag');
out = tf;
end

function out = tvwrite()
T = table([1; 2], [3; 4]);
T.Variables = [5 6; 7 8];
out = [T.Var1 T.Var2];
end

function tvwritebad()
T = table([1; 2], [3; 4]);
T.Variables = [5 6 7; 7 8 9];
end

function out = tsz(type)
T = table('Size', [2 1], 'VariableTypes', {type});
v = T.Var1;
out = sprintf('%s%s:%s', class(v), mat2str(size(v)), txt(v));
end

function out = t0full()
T = table([1; 2; 3]);
T.x = 5;
out = T.x;
end

function out = t0row(T)
T0 = T(:, []);
T0.x = [1 2 3];
out = size(T0.x);
end

function out = t0col2(T)
T0 = T(:, []);
T0.x = [1 2; 3 4; 5 6];
out = size(T0);
end

function out = t0paren(T)
T0 = T(:, []);
T0(:, 'x') = {7};
out = {size(T0), T0.x'};
end

function out = t0del(T)
T(:, :) = [];
T2 = T;
out = size(T2);
end

function out = t0rowsidx(T)
T0 = T(:, []);
out = size(T0(2, :));
end

function out = t0rn()
T = table([1; 2], 'RowNames', {'a', 'b'});
T0 = T(:, []);
out = {size(T0), T0.Properties.RowNames'};
end

function out = mlist(v)
out = methods(v)';
end

function out = whws()
x = 1:3; c = {1}; s = struct('a', 1); %#ok<NASGU>
w = whos;
out = {w.name; w.bytes};
end

function out = whwsf()
x = 1; %#ok<NASGU>
w = whos;
out = {fieldnames(w)', w.nesting};
end

function out = whfb(v)
if isequal(v, 'COMPLEX_SPARSE'), v = sparse([1i 0; 0 2]); end
x = v; %#ok<NASGU>
f = [tempname '.mat']; save(f, 'x'); w = whos('-file', f); delete(f);
out = {w.size, w.bytes, w.class, w.complex, w.sparse};
end

function out = whm(v)
if isequal(v, 'COMPLEX_SPARSE'), v = sparse([1i 0; 0 2]); end
x = v; %#ok<NASGU>
w = whos('x');
out = {w.size, w.bytes, w.class};
end

function out = whdisp()
f = [tempname '.mat']; zz = 1; Ab = 'xy'; ab = {1}; B = int8([1 2; 3 4]); %#ok<NASGU>
save(f, 'zz', 'Ab', 'ab', 'B');
out = evalc('whos(''-file'', f)');
delete(f);
end

function out = whdispwide()
f = [tempname '.mat']; a_long_variable_name = zeros(100, 2000); s = sparse([1 0; 0 2]); q = 1 + 2i; %#ok<NASGU>
save(f, 'a_long_variable_name', 's', 'q');
out = evalc('whos(''-file'', f)');
delete(f);
end

function out = whnone()
f = [tempname '.mat']; x = 1; %#ok<NASGU>
save(f, 'x');
out = evalc('whos(''-file'', f, ''nothing*'')');
delete(f);
end

function out = prewarn()
lastwarn('');
T = table('Size', [1 1], 'VariableTypes', {'char'}); %#ok<NASGU>
[msg, id] = lastwarn;
out = [id ' | ' msg];
end

function out = dotnumw(T)
T.(1) = [7; 8; 9];
out = T.n';
end

function out = rowdot()
T = table([1; 2], 'RowNames', {'a', 'b'});
out = T.Row';
end

function out = brcls(T)
v = T{:, :};
out = [class(v) mat2str(size(v))];
w = T.Variables;
out = [out ' ' class(w) mat2str(size(w))];
end

function out = vwint8()
T = table(int8([1; 2]), [3; 4]);
T.Variables = [5.6 6; 7 8];
out = {class(T.Var1), T.Var1', class(T.Var2), T.Var2'};
end

function vwheight()
T = table([1; 2], [3; 4]);
T.Variables = [5 6; 7 8; 9 10];
end

function vwcat()
T = table(categorical({'a'; 'b'}), [3; 4]);
T.Variables = [5 6; 7 8];
end

function out = vwtext()
T = table({'a'; 'b'}, {'c'; 'd'});
T.Variables = {'e', 'f'; 'g', 'h'};
out = [T.Var1' T.Var2'];
end

function out = whglobal()
global G128 %#ok<GVMIS>
G128 = 1;
w = whos('G128');
out = {w.global, evalc('whos G128')};
end

function out = suans(T)
clear ans
summary(T(:, 1));
out = exist('ans', 'var');
end

function out = vtcls()
v = vartype('numeric');
out = {class(v), isa(v, 'matlab.internal.tabular.private.subscripter'), ismethod(v, 'keyMatch')};
end

function out = cvcat(T)
lastwarn('');
T2 = convertvars(T, 'kind', 'char');
[~, id] = lastwarn;
out = {class(T2.kind), id};
end

function out = cvnum(T)
lastwarn('');
T2 = convertvars(T, 'n', 'char');
[~, id] = lastwarn;
out = {class(T2.n), id};
end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end

function oi_chk(label, fn, divs)
% u9b_chk for a row whose label a loop computes, or a divergence row (ADR 0234) for one listed in divs.
if any(strcmp(label, divs))
    u9b_chkdiv(label, @() oi_try(fn), 'ADR0234');
else
    u9b_chk(label, fn);
end
end

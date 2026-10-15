% record: -noFigureWindows
% Open item 122 (ADR 0233): the table verbs a categorical needs, in the MATLAB dialect: vartype as a
% table subscript, convertvars, table2cell, join and innerjoin (keys inferred or named, the second
% outputs), stack; array2table, table2struct (ToScalar too), groupsummary (the key in category order,
% undefined last) and sortrows (several keys, directions, numbered variables) keeping a categorical
% one; arrayfun walking a categorical element by element and collecting characters, strings and
% categoricals; save and load of a categorical. ADR 0234 made the display of a vartype, a table of no
% variables keeping its rows, a vartype through a dot, a table saved to a MAT-file and whos('-file')
% exact.
c = categorical({'a', 'b', 'a', 'c'});
T = table([1; 2; 3], categorical({'x'; 'y'; 'x'}), {'p'; 'q'; 'r'}, ["s1"; "s2"; "s3"], [true; false; true], 'VariableNames', {'n', 'kind', 'txt', 'str', 'flag'});
u9b_chk('vt_class', @() class(vartype('numeric')));
u9b_chk('vt_disp', @() evalc('disp(vartype(''numeric''))'));
u9b_chk('vt_num', @() T(:, vartype('numeric')).Properties.VariableNames); u9b_chk('vt_cat', @() T(:, vartype('categorical')).Properties.VariableNames);
u9b_chk('vt_cellstr', @() T(:, vartype('cellstr')).Properties.VariableNames); u9b_chk('vt_string', @() T(:, vartype('string')).Properties.VariableNames);
u9b_chk('vt_logical', @() T(:, vartype('logical')).Properties.VariableNames); u9b_chk('vt_double', @() T(:, vartype('double')).Properties.VariableNames);
u9b_chk('vt_cell', @() T(:, vartype('cell')).Properties.VariableNames); u9b_chk('vt_none', @() size(T(:, vartype('datetime'))));
u9b_chk('vt_brace', @() T{:, vartype('numeric')}); u9b_chk('vt_brace_cat', @() T{:, vartype('categorical')});
u9b_chk('vt_rows', @() T(2:3, vartype('numeric')).n); u9b_chk('vt_bad', @() vartype('nosuchtype')); u9b_chk('vt_noarg', @() vartype());
u9b_chk('vt_num_arg', @() vartype(3)); u9b_chk('vt_string_arg', @() class(vartype("numeric")));
u9b_chk('vt_assign', @() vtassign(T)); u9b_chk('vt_dot', @() T.(vartype('numeric')));
u9b_chk('vt_isa', @() isa(vartype('numeric'), 'vartype'));
u9b_chk('vt_table_type', @() T(:, vartype('table')).Properties.VariableNames);
TI = table(int8(1), 2.5); u9b_chk('vt_integer', @() size(TI(:, vartype('integer'))));
u9b_chk('vt_float', @() TI(:, vartype('float')).Properties.VariableNames);
u9b_chk('cv_name_cat', @() class(convertvars(T, 'txt', 'categorical').txt)); u9b_chk('cv_name_cat_vals', @() convertvars(T, 'txt', 'categorical').txt);
u9b_chk('cv_idx', @() class(convertvars(T, 1, 'categorical').n)); u9b_chk('cv_idx_vals', @() convertvars(T, 1, 'categorical').n);
u9b_chk('cv_cell_names', @() class(convertvars(T, {'n', 'flag'}, 'string').flag)); u9b_chk('cv_cell_names_vals', @() convertvars(T, {'n', 'flag'}, 'string').n);
u9b_chk('cv_logical_mask', @() class(convertvars(T, [true false false false false], 'single').n));
u9b_chk('cv_vartype', @() class(convertvars(T, vartype('cellstr'), 'categorical').txt));
u9b_chk('cv_fn_select', @() class(convertvars(T, @iscellstr, 'categorical').txt));
u9b_chk('cv_fn_convert', @() convertvars(T, 'n', @(x) x * 10).n);
u9b_chk('cv_cat_to_string', @() class(convertvars(T, 'kind', 'string').kind)); u9b_chk('cv_cat_to_cellstr', @() convertvars(T, 'kind', 'cellstr').kind);
u9b_chk('cv_cat_to_double', @() convertvars(T, 'kind', 'double').kind);
u9b_chk('cv_string_to_double', @() convertvars(table(["1"; "2"]), 1, 'double').Var1);
u9b_chk('cv_bad_type', @() convertvars(T, 'n', 'nosuch')); u9b_chk('cv_bad_var', @() convertvars(T, 'zz', 'string'));
u9b_chk('cv_order', @() convertvars(T, 'kind', 'string').Properties.VariableNames);
u9b_chk('cv_int', @() class(convertvars(T, 'n', 'int8').n)); u9b_chk('cv_logical_to_double', @() convertvars(T, 'flag', 'double').flag);
u9b_chk('cv_string_type', @() class(convertvars(T, "txt", "string").txt));
u9b_chk('cv_cellstr_to_double', @() convertvars(table({'1'; '2'}), 1, 'double').Var1);
u9b_chk('t2c_size', @() size(table2cell(T))); u9b_chk('t2c_cat', @() class(subc(table2cell(T), 1, 2))); u9b_chk('t2c_txt', @() subc(table2cell(T), 2, 3)); u9b_chk('t2c_str', @() class(subc(table2cell(T), 1, 4)));
u9b_chk('t2c_n', @() subc(table2cell(T), 3, 1)); u9b_chk('t2c_all', @() table2cell(T(1:2, [1 3])));
u9b_chk('t2c_multi', @() table2cell(table([1 2; 3 4], {'a'; 'b'}))); u9b_chk('t2c_empty', @() size(table2cell(table())));
u9b_chk('t2c_rownames', @() table2cell(table([1; 2], 'RowNames', {'r1', 'r2'})));
u9b_chk('t2c_notable', @() table2cell([1 2]));
u9b_chk('t2c_struct_var', @() class(subc(table2cell(table(struct('a', {1; 2}))), 1, 1)));
L = table(categorical({'x'; 'y'; 'x'; 'z'}), [1; 2; 3; 4], 'VariableNames', {'kind', 'n'});
R = table(categorical({'x'; 'y'; 'z'}), [10; 20; 30], 'VariableNames', {'kind', 'w'});
u9b_chk('j_size', @() size(join(L, R))); u9b_chk('j_w', @() join(L, R).w); u9b_chk('j_names', @() join(L, R).Properties.VariableNames); u9b_chk('j_kind', @() join(L, R).kind);
u9b_chk('j_missing', @() join(L, R(1:2, :))); u9b_chk('j_dup', @() join(L, [R; R(1, :)]));
u9b_chk('j_keys', @() join(L, R, 'Keys', 'kind').w); u9b_chk('j_keys_idx', @() join(L, R, 'Keys', 1).w);
u9b_chk('j_lr_keys', @() join(L, renamevars_(R), 'LeftKeys', 'kind', 'RightKeys', 'k2').Properties.VariableNames);
u9b_chk('j_keep', @() join(L, R, 'KeepOneCopy', 'kind').Properties.VariableNames);
u9b_chk('j_samevar', @() join(table([1; 2], [5; 6], 'VariableNames', {'k', 'v'}), table([2; 1], [7; 8], 'VariableNames', {'k', 'v'}), 'Keys', 'k').Properties.VariableNames);
u9b_chk('j_samevar_vals', @() join(table([1; 2], [5; 6], 'VariableNames', {'k', 'v'}), table([2; 1], [7; 8], 'VariableNames', {'k', 'v'}), 'Keys', 'k').v_right);
u9b_chk('j_nokey', @() join(table([1; 2], 'VariableNames', {'a'}), table([1; 2], 'VariableNames', {'b'})));
u9b_chk('j_num_keys', @() join(table([2; 1; 2], [5; 6; 7], 'VariableNames', {'k', 'v'}), table([1; 2], [100; 200], 'VariableNames', {'k', 'w'})).w);
u9b_chk('j_cellstr_key', @() join(table({'a'; 'b'}, [1; 2], 'VariableNames', {'k', 'v'}), table({'b'; 'a'}, [7; 8], 'VariableNames', {'k', 'w'})).w);
u9b_chk('j_string_key', @() join(table(["a"; "b"], [1; 2], 'VariableNames', {'k', 'v'}), table(["b"; "a"], [7; 8], 'VariableNames', {'k', 'w'})).w);
u9b_chk('j_two_keys', @() join(table([1; 1; 2], {'a'; 'b'; 'a'}, [5; 6; 7], 'VariableNames', {'k1', 'k2', 'v'}), table([1; 2; 1], {'b'; 'a'; 'a'}, [10; 20; 30], 'VariableNames', {'k1', 'k2', 'w'})).w);
[jt, ir] = join(L, R); u9b_chk('j_iright', @() ir);
u9b_chk('j_cat_cats_differ', @() join(table(categorical({'x'}), 1, 'VariableNames', {'k', 'v'}), table(categorical({'x'}, {'q', 'x'}), 2, 'VariableNames', {'k', 'w'})).w);
u9b_chk('ij_size', @() size(innerjoin(L, R))); u9b_chk('ij_w', @() innerjoin(L, R).w); u9b_chk('ij_n', @() innerjoin(L, R).n); u9b_chk('ij_kind', @() innerjoin(L, R).kind);
u9b_chk('ij_partial', @() innerjoin(L, R(1:2, :)).n); u9b_chk('ij_dup', @() innerjoin(L, [R; R(1, :)]).w); u9b_chk('ij_dup_n', @() innerjoin(L, [R; R(1, :)]).n);
[it, il, irr] = innerjoin(L, R); u9b_chk('ij_il', @() il); u9b_chk('ij_ir', @() irr);
u9b_chk('ij_keys', @() innerjoin(L, R, 'Keys', 'kind').Properties.VariableNames);
u9b_chk('ij_leftvars', @() innerjoin(L, R, 'LeftVariables', 'n').Properties.VariableNames);
u9b_chk('ij_rightvars', @() innerjoin(L, R, 'RightVariables', {}).Properties.VariableNames);
u9b_chk('ij_num', @() innerjoin(table([3; 1; 2], [5; 6; 7], 'VariableNames', {'k', 'v'}), table([2; 3; 9], [100; 200; 300], 'VariableNames', {'k', 'w'})).k);
u9b_chk('ij_samevar', @() innerjoin(table([1; 2], [5; 6], 'VariableNames', {'k', 'v'}), table([2; 1], [7; 8], 'VariableNames', {'k', 'v'})).Properties.VariableNames);
u9b_chk('ij_samevar2', @() innerjoin(table([1; 2], [5; 6], 'VariableNames', {'k', 'v'}), table([2; 1], [7; 8], 'VariableNames', {'k', 'v'}), 'Keys', 'k').Properties.VariableNames);
u9b_chk('ij_cellstr_order', @() innerjoin(table({'b'; 'a'; 'c'}, [1; 2; 3], 'VariableNames', {'k', 'v'}), table({'c'; 'b'}, [7; 8], 'VariableNames', {'k', 'w'})).k);
u9b_chk('ij_cat_order', @() innerjoin(table(categorical({'b'; 'a'; 'c'}, {'c', 'b', 'a'}), [1; 2; 3], 'VariableNames', {'k', 'v'}), table(categorical({'c'; 'b'; 'a'}), [7; 8; 9], 'VariableNames', {'k', 'w'})).k);
u9b_chk('ij_cat_cats', @() categories(innerjoin(table(categorical({'b'; 'a'; 'c'}, {'c', 'b', 'a'}), [1; 2; 3], 'VariableNames', {'k', 'v'}), table(categorical({'c'; 'b'; 'a'}), [7; 8; 9], 'VariableNames', {'k', 'w'})).k));
u9b_chk('ij_empty', @() size(innerjoin(L, R([], :))));
S = table([1; 2], [3; 4], [5; 6], 'VariableNames', {'id', 'a', 'b'});
u9b_chk('st_size', @() size(stack(S, {'a', 'b'}))); u9b_chk('st_names', @() stack(S, {'a', 'b'}).Properties.VariableNames);
u9b_chk('st_ind', @() stack(S, {'a', 'b'}).a_b_Indicator); u9b_chk('st_ind_class', @() class(stack(S, {'a', 'b'}).a_b_Indicator)); u9b_chk('st_data', @() stack(S, {'a', 'b'}).a_b); u9b_chk('st_id', @() stack(S, {'a', 'b'}).id);
u9b_chk('st_one', @() stack(S, 'a').Properties.VariableNames); u9b_chk('st_one_n', @() stack(T, 'n').n); u9b_chk('st_one_ind', @() stack(T, 'n').n_Indicator);
u9b_chk('st_cat_kept', @() class(stack(T, 'n').kind)); u9b_chk('st_cat_kept_vals', @() stack(T, 'n').kind);
u9b_chk('st_idx', @() stack(S, [2 3]).Properties.VariableNames); u9b_chk('st_newname', @() stack(S, {'a', 'b'}, 'NewDataVariableName', 'val').Properties.VariableNames);
u9b_chk('st_indname', @() stack(S, {'a', 'b'}, 'IndexVariableName', 'which').Properties.VariableNames);
u9b_chk('st_constant', @() stack(S, {'a', 'b'}, 'ConstantVariables', 'id').Properties.VariableNames);
u9b_chk('st_constant_none', @() stack(S, {'a', 'b'}, 'ConstantVariables', {}).Properties.VariableNames);
u9b_chk('st_ind_cats', @() categories(stack(S, {'a', 'b'}).a_b_Indicator));
[stt, ist] = stack(S, {'a', 'b'}); u9b_chk('st_iu', @() ist);
u9b_chk('st_mismatch', @() stack(table([1; 2], {'p'; 'q'}, 'VariableNames', {'a', 'b'}), {'a', 'b'}));
u9b_chk('st_three', @() stack(table(1, 2, 3, 4, 'VariableNames', {'id', 'a', 'b', 'c'}), 2:4).a_b_c);
u9b_chk('st_cat_stack', @() class(stack(table(categorical({'x'}), categorical({'y'}), 'VariableNames', {'a', 'b'}), {'a', 'b'}).a_b));
u9b_chk('a2t_class', @() class(array2table(c').Var1)); u9b_chk('a2t_vals', @() array2table(c').Var1); u9b_chk('a2t_matrix', @() size(array2table([c' c'])));
u9b_chk('a2t_matrix_class', @() class(array2table([c' c']).Var2)); u9b_chk('a2t_names', @() array2table(c', 'VariableNames', {'k'}).Properties.VariableNames);
u9b_chk('t2s_class', @() class(t2s(T))); u9b_chk('t2s_size', @() size(table2struct(T))); u9b_chk('t2s_scalar', @() class(table2struct(T, 'ToScalar', true).kind));
u9b_chk('t2s_txt', @() class(t2stxt(T)));
u9b_chk('gs_kind_class', @() class(groupsummary(T, 'kind').kind)); u9b_chk('gs_kind', @() groupsummary(T, 'kind').kind); u9b_chk('gs_count', @() groupsummary(T, 'kind').GroupCount);
u9b_chk('gs_mean', @() groupsummary(T, 'kind', 'mean', 'n').mean_n); u9b_chk('gs_names', @() groupsummary(T, 'kind', 'mean', 'n').Properties.VariableNames);
u9b_chk('gs_unused', @() groupsummary(table(categorical({'x'; 'y'}, {'x', 'y', 'w'}), [1; 2], 'VariableNames', {'k', 'v'}), 'k').k);
u9b_chk('gs_undef', @() groupsummary(table(categorical({'x'; ''}), [1; 2], 'VariableNames', {'k', 'v'}), 'k').k);
u9b_chk('gs_vector', @() groupsummary([1; 2; 3], categorical({'x'; 'y'; 'x'}), 'sum'));
[gsv, gsg] = groupsummary([1; 2; 3], categorical({'x'; 'y'; 'x'}), 'sum'); u9b_chk('gs_vector_g', @() gsg);
u9b_chk('sr_two', @() sortrows(T, {'kind', 'n'}).n); u9b_chk('sr_one', @() sortrows(T, 'kind').n); u9b_chk('sr_desc', @() sortrows(T, 'kind', 'descend').n);
u9b_chk('sr_two_desc', @() sortrows(T, {'kind', 'n'}, {'ascend', 'descend'}).n); u9b_chk('sr_idx', @() sortrows(T, [2 1]).n); u9b_chk('sr_neg', @() sortrows(T, -2).n);
u9b_chk('sr_cat_col', @() sortrows(categorical({'b'; 'a'; 'c'}))); u9b_chk('sr_cat_mat', @() sortrows(categorical({'b', 'x'; 'a', 'y'; 'b', 'a'})));
u9b_chk('sr_cat_ord', @() sortrows(table(categorical({'lo'; 'hi'; 'mid'}, {'lo', 'mid', 'hi'}, 'Ordinal', true), [1; 2; 3]), 1).Var2);
u9b_chk('sr_cat_undef', @() sortrows(table(categorical({'b'; ''; 'a'}), [1; 2; 3]), 1).Var2);
u9b_chk('sr_cat_valueset', @() sortrows(table(categorical({'b'; 'a'; 'c'}, {'c', 'b', 'a'}), [1; 2; 3]), 1).Var2);
[srt, sri] = sortrows(T, 'kind'); u9b_chk('sr_index', @() sri);
u9b_chk('sr_cellstr_two', @() sortrows(table({'b'; 'a'; 'b'}, [2; 1; 1]), {'Var1', 'Var2'}).Var2);
u9b_chk('sr_string_two', @() sortrows(table(["b"; "a"; "b"], [2; 1; 1]), {'Var1', 'Var2'}).Var2);
u9b_chk('af_double', @() arrayfun(@(x) double(x), c)); u9b_chk('af_same', @() arrayfun(@(x) x, c)); u9b_chk('af_same_class', @() class(arrayfun(@(x) x, c)));
u9b_chk('af_char', @() arrayfun(@(x) char(x), c, 'UniformOutput', false)); u9b_chk('af_char_uni', @() arrayfun(@(x) char(x), c));
u9b_chk('af_eq', @() arrayfun(@(x) x == 'a', c)); u9b_chk('af_class', @() arrayfun(@(x) string(class(x)), c(1)));
u9b_chk('af_size', @() arrayfun(@(x) numel(x), c)); u9b_chk('af_cats', @() arrayfun(@(x) numel(categories(x)), c));
u9b_chk('af_undef', @() arrayfun(@(x) isundefined(x), categorical({'a', ''})));
u9b_chk('af_two', @() arrayfun(@(x, y) double(x) + y, c, [1 2 3 4]));
u9b_chk('af_cat_col', @() size(arrayfun(@(x) x, c')));
u9b_chk('af_cat_cats', @() categories(arrayfun(@(x) x, categorical({'b', 'a'}, {'b', 'a', 'z'}))));
u9b_chk('af_ord', @() isordinal(arrayfun(@(x) x, categorical({'b', 'a'}, {'b', 'a'}, 'Ordinal', true))));
u9b_chk('af_mixed_cats', @() arrayfun(@(k) categorical({sprintf('n%d', k)}), 1:2));
u9b_chk('cf_cat', @() cellfun(@(x) x, {categorical({'a'}), categorical({'b'})}));
u9b_chk('cf_cat_class', @() class(cellfun(@(x) x, {categorical({'a'}), categorical({'b'})})));
u9b_chk('sl_class', @() saveload(c)); u9b_chk('sl_ord', @() saveloadord()); u9b_chk('sl_cats', @() saveloadcats()); u9b_chk('sl_table', @() saveloadtable(T)); u9b_chk('sl_undef', @() saveload(categorical({'a', ''})));
u9b_chk('sl_struct', @() saveloadstruct(c)); u9b_chk('sl_protected', @() saveloadprot());
u9b_chk('sl_whos', @() saveloadwhos(c));



function pr(label, f)
try
    v = f();
    fprintf('%s\t%s\t%s\t%s\n', label, class(v), mat2str(size(v)), s(v));
catch e
    fprintf('%s\tERR\t%s\t%s\n', label, e.identifier, strrep(e.message, newline, ' '));
end
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
    t = [strjoin(v.Properties.VariableNames, ',') ' ' mat2str(size(v))];
else
    t = class(v);
end
end

function x = subc(c, i, j), x = c{i, j}; end

function x = vtassign(T), T(:, vartype('numeric')) = table([7; 8; 9]); x = T.n; end

function R = renamevars_(R), R.Properties.VariableNames{1} = 'k2'; end

function x = t2s(T), s = table2struct(T); x = s(1).kind; end

function x = t2stxt(T), s = table2struct(T); x = s(1).txt; end

function x = saveload(c)
p = [tempname '.mat']; save(p, 'c'); L = load(p); delete(p); x = sprintf('%s %s %s', class(L.c), s(L.c), strjoin(categories(L.c), '/'));
end

function x = saveloadord()
c = categorical({'lo', 'hi'}, {'lo', 'hi'}, 'Ordinal', true); p = [tempname '.mat']; save(p, 'c'); L = load(p); delete(p); x = isordinal(L.c);
end

function x = saveloadcats()
c = categorical({'a'}, {'z', 'a', 'q'}); p = [tempname '.mat']; save(p, 'c'); L = load(p); delete(p); x = strjoin(categories(L.c), '/');
end

function x = saveloadtable(T)
p = [tempname '.mat']; save(p, 'T'); L = load(p); delete(p); x = class(L.T.kind);
end

function x = saveloadstruct(c)
st.c = c; st.n = 1; p = [tempname '.mat']; save(p, '-struct', 'st'); L = load(p); delete(p); x = class(L.c);
end

function x = saveloadprot()
c = categorical({'a'}, {'a', 'b'}, 'Protected', true); p = [tempname '.mat']; save(p, 'c'); L = load(p); delete(p); x = isprotected(L.c);
end

function x = saveloadwhos(c)
p = [tempname '.mat']; save(p, 'c'); w = whos('-file', p); delete(p); x = w.class;
end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end

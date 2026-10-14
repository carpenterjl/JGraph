% record: -noFigureWindows
% Open items 123 to 125 (ADR 0233), in the MATLAB dialect. 123: R2025b's identifiers and words when a
% verb meets an object of a script's class (the conversions, the reductions, num2str, mat2str,
% sprintf by its conversion, setfield, rmfield, orderfields), a list where one value is wanted
% (a.p(end), class(a.p)), a parameter the caller did not pass (MATLAB:minrhs), and a categorical
% handed to times, cumsum, round, the text verbs, cellfun, fieldnames or a dot. 124: horzcat,
% vertcat and cat(1|2, ...) join as the bracket does, cells, char rows, structs, strings and objects
% included. 125: isenum and istabular. Lines marked div=ADR0233 are set and addlistener over an
% array of handles, the product of two categoricals, struct(categorical), isspace of one, a row of
% function handles, an enumeration member, and where which and exist say a name lives.
s1 = Val116(1); s2 = Val116(2); s3 = Val116(3); a = [s1 s2 s3];
h1 = Hnd116(1); h2 = Hnd116(2); ha = [h1 h2];
c = categorical({'a', 'b', 'a', 'c'});
T = table([1; 2], categorical({'x'; 'y'}), 'VariableNames', {'n', 'kind'});
u9b_chk('o_double', @() double(s1)); u9b_chk('o_double_arr', @() double(a)); u9b_chk('o_single', @() single(s1)); u9b_chk('o_int8', @() int8(s1)); u9b_chk('o_logical', @() logical(s1));
u9b_chk('o_char', @() char(s1)); u9b_chk('o_string', @() string(s1)); u9b_chk('o_cell', @() cell(s1)); u9b_chk('o_cellstr', @() cellstr(s1));
u9b_chk('o_num2str', @() num2str(s1)); u9b_chk('o_mat2str', @() mat2str(s1)); u9b_chk('o_int2str', @() int2str(s1)); u9b_chk('o_disp_sprintf_d', @() sprintf('%d', s1)); u9b_chk('o_sprintf_s', @() sprintf('%s', s1)); u9b_chk('o_sprintf_f', @() sprintf('%f', s1));
u9b_chk('o_fprintf', @() fprintf('%d', s1));
u9b_chk('o_sum', @() sum(a)); u9b_chk('o_prod', @() prod(a)); u9b_chk('o_max', @() max(a)); u9b_chk('o_min', @() min(a)); u9b_chk('o_any', @() any(a)); u9b_chk('o_all', @() all(a)); u9b_chk('o_cumsum', @() cumsum(a)); u9b_chk('o_cumprod', @() cumprod(a));
u9b_chk('o_mean', @() mean(a)); u9b_chk('o_std', @() std(a)); u9b_chk('o_var', @() var(a)); u9b_chk('o_nnz', @() nnz(a)); u9b_chk('o_abs', @() abs(a)); u9b_chk('o_round', @() round(a)); u9b_chk('o_sqrt', @() sqrt(a));
u9b_chk('o_isnan', @() isnan(a)); u9b_chk('o_diff', @() diff(a)); u9b_chk('o_norm', @() norm(a)); u9b_chk('o_floor', @() floor(a));
u9b_chk('o_setfield', @() setfield(s1, 'p', 5).p); u9b_chk('o_setfield_new', @() setfield(s1, 'zz', 5)); u9b_chk('o_getfield_bad', @() getfield(s1, 'zz'));
u9b_chk('o_rmfield', @() rmfield(s1, 'p')); u9b_chk('o_orderfields', @() orderfields(s1)); u9b_chk('o_isfield', @() isfield(s1, 'p')); u9b_chk('o_fieldnames', @() fieldnames(s1)); u9b_chk('o_struct', @() class(struct(s1)));
u9b_chk('o_af_uniform', @() arrayfun(@(o) o, a, 'UniformOutput', true)); u9b_chk('o_af_uniform_char', @() arrayfun(@(o) o.name, [s1 Val116('ab')]));
u9b_chk('o_af_nonscalar', @() arrayfun(@(o) [o.p o.p], a));
u9b_chk('o_cs_end', @() a.p(end)); u9b_chk('o_cs_sub_end', @() a(1:2).p(end)); u9b_chk('o_cs_index', @() a.p(1)); u9b_chk('o_cs_index_sub', @() a(1:2).name(1));
u9b_chk('o_deal', @() dealwrong(a)); u9b_chk('o_deal_more', @() dealmore(a));
u9b_chk('o_class_cs', @() class(a.p)); u9b_chk('o_class_cs2', @() class(a(1:2).p));
u9b_chk('o_minrhs_ctor', @() gapnoargs()); u9b_chk('o_minrhs_fn', @() needsarg()); u9b_chk('o_minrhs_fn2', @() needstwo(1)); u9b_chk('o_minrhs_ctor_direct', @() NoArg116());
u9b_chk('o_minrhs_nested', @() callsneeds()); u9b_chk('o_minrhs_if', @() needsif()); u9b_chk('o_minrhs_nargin', @() needsnargin());
u9b_chk('o_minrhs_exist', @() needsexist());
u9b_chk('o_h_sum', @() sum(ha)); u9b_chk('o_h_max', @() max(ha)); u9b_chk('o_h_min', @() min(ha)); u9b_chk('o_h_double', @() double(h1)); u9b_chk('o_h_mean', @() mean(ha)); u9b_chk('o_h_abs', @() abs(h1));
u9b_chkdiv('o_h_set', @() oi_try( @() set(ha, 'p', 1)), 'ADR0233'); u9b_chk('o_h_get', @() get(h1, 'p')); u9b_chkdiv('o_h_addlistener', @() oi_try( @() class(addlistener(ha, 'p', 'PostSet', @(s, e) 1))), 'ADR0233');
u9b_chk('o_h_max_cs', @() max(ha.p)); u9b_chk('o_max_cs3', @() max(a.p));
u9b_chk('o_mixed_mat2str', @() mat2str([Val116(1) Sub116(2)]));
u9b_chk('o_assign_mismatch', @() rangeassignbad(a)); u9b_chk('o_assign_mismatch_num', @() numassignbad());
u9b_chk('o_h_isgraphics', @() isgraphics(ha)); u9b_chk('o_isgraphics_val', @() isgraphics(a));
u9b_chk('o_metaclass', @() class(metaclass(s1))); u9b_chk('o_metaclass_name', @() metaclass(s1).Name); u9b_chk('o_meta_q', @() class(?Val116));
u9b_chk('c_times', @() c .* 2); u9b_chk('c_mtimes', @() c * 2); u9b_chk('c_plus', @() c + 1); u9b_chk('c_minus', @() c - 1); u9b_chk('c_rdivide', @() c ./ 2); u9b_chk('c_power', @() c .^ 2); u9b_chk('c_uminus', @() -c); u9b_chk('c_not', @() ~c);
u9b_chk('c_and', @() c & true); u9b_chkdiv('c_times_cat', @() oi_try( @() c .* c), 'ADR0233'); u9b_chk('c_plus_cat', @() c + c); u9b_chk('c_abs', @() abs(c)); u9b_chk('c_sum', @() sum(c)); u9b_chk('c_mean', @() mean(c)); u9b_chk('c_cumsum', @() cumsum(c)); u9b_chk('c_round', @() round(c));
u9b_chk('c_regexp', @() regexp(c, 'a')); u9b_chk('c_regexprep', @() regexprep(c, 'a', 'b')); u9b_chk('c_regexpi', @() regexpi(c, 'a')); u9b_chk('c_strjoin', @() strjoin(c, ',')); u9b_chk('c_strsplit', @() strsplit(c(1))); u9b_chk('c_strtrim', @() strtrim(c));
u9b_chk('c_cellfun', @() cellfun(@(x) x, c)); u9b_chk('c_str2num', @() str2num(c)); u9b_chk('c_str2double', @() str2double(c)); u9b_chk('c_datestr', @() datestr(c)); u9b_chk('c_fieldnames', @() fieldnames(c)); u9b_chk('c_dot', @() dotcat(c)); u9b_chk('c_dot_write', @() dotwrite(c));
u9b_chk('c_strrep', @() strrepw(c)); u9b_chk('c_strfind', @() strfind(c, 'a')); u9b_chk('c_upper', @() upper(c)); u9b_chk('c_num2str', @() num2str(c)); u9b_chk('c_mat2str', @() mat2str(c)); u9b_chk('c_sprintf_d', @() sprintf('%d', c)); u9b_chk('c_int2str', @() int2str(c));
u9b_chk('c_empty_name', @() categorical(["a" missing], ["a" missing])); u9b_chk('c_empty_name2', @() categorical({'a', 'b'}, {'a', ''}));
u9b_chk('c_empty_name3', @() categorical({'a'}, {'a', 'b'}, {'x', ''})); u9b_chk('c_missing_names', @() categorical([1 2], [1 2], ["x" missing]));
u9b_chk('c_brace', @() bracecat(c)); u9b_chk('c_isfield', @() isfield(c, 'a')); u9b_chkdiv('c_struct', @() oi_try( @() struct(c)), 'ADR0233'); u9b_chk('c_cell', @() cell(c)); u9b_chk('c_logical', @() logical(c)); u9b_chk('c_char_mat', @() size(char(categorical({'ab'; 'c'}))));
u9b_chk('c_setfield', @() setfield(c, 'a', 1)); u9b_chk('c_rmfield', @() rmfield(c, 'a')); u9b_chk('c_sort_cell', @() sort({c}));
u9b_chk('c_contains_cat', @() contains(c, categorical({'a'}))); u9b_chk('c_strcmp', @() strcmp(c, 'a')); u9b_chk('c_strcmpi', @() strcmpi(c, 'A')); u9b_chkdiv('c_isspace', @() oi_try( @() isspace(c)), 'ADR0233');
u9b_chk('c_double_str', @() double("a") + c);
u9b_chk('t_mixed_brace', @() T{:, :}); u9b_chk('t_mixed_brace_rows', @() T{1, :});
u9b_chk('t_mixed_brace_num_char', @() table1brace());
u9b_chk('hc_cells', @() horzcat({1}, {2})); u9b_chk('hc_cells_class', @() class(horzcat({1}, {2}))); u9b_chk('vc_char', @() vertcat('x', 'x')); u9b_chk('hc_char', @() horzcat('ab', 'cd'));
u9b_chk('vc_names', @() vertcat(a.name)); u9b_chk('hc_names', @() horzcat(a.name)); u9b_chk('hc_obj_cells', @() class(horzcat({s1}, {s2}))); u9b_chk('hc_obj_struct_cells', @() size(horzcat({a}, {s1})));
u9b_chk('hc_objects', @() class(horzcat(s1, s2))); u9b_chk('vc_objects', @() size(vertcat(s1, s2))); u9b_chk('hc_strings', @() horzcat("a", "b")); u9b_chk('vc_strings', @() vertcat("a", "b"));
u9b_chk('hc_mixed', @() horzcat({1}, 2)); u9b_chk('vc_mixed_char_num', @() vertcat('a', 66)); u9b_chk('hc_cat', @() horzcat(c, c)); u9b_chk('vc_cat', @() size(vertcat(c, c)));
u9b_chk('hc_struct', @() size(horzcat(struct('a', 1), struct('a', 2)))); u9b_chk('vc_struct_bad', @() vertcat(struct('a', 1), struct('b', 2)));
u9b_chk('hc_none', @() horzcat()); u9b_chk('vc_none', @() vertcat()); u9b_chk('hc_one', @() horzcat({1})); u9b_chk('vc_char_mismatch', @() vertcat('ab', 'c'));
u9b_chk('hc_table', @() size(horzcat(T, table([5; 6], 'VariableNames', {'z'})))); u9b_chk('vc_table', @() size(vertcat(T, T)));
u9b_chk('hc_logical_num', @() class(horzcat(true, 2))); u9b_chk('hc_int_double', @() class(horzcat(int8(1), 2.7))); u9b_chk('hc_char_num', @() horzcat('a', 66));
u9b_chk('hc_cell_cs', @() cscell()); u9b_chk('vc_cs_struct', @() csstruct()); u9b_chk('hc_handles', @() class(horzcat(h1, h2)));
u9b_chk('hc_empty_cells', @() size(horzcat({}, {1}))); u9b_chk('vc_empty_char', @() vertcat('', 'ab'));
u9b_chk('cat_cells', @() cat(2, {1}, {2})); u9b_chk('cat_chars', @() cat(1, 'x', 'y'));
u9b_chk('hc_dim_mismatch', @() horzcat([1; 2], [1 2 3]));
u9b_chk('vc_dim_mismatch', @() vertcat([1 2], [1 2 3]));
u9b_chk('isenum_cat', @() isenum(c)); u9b_chk('isenum_num', @() isenum(1)); u9b_chk('isenum_obj', @() isenum(s1)); u9b_chk('isenum_char', @() isenum('a')); u9b_chkdiv('isenum_enum', @() oi_try( @() isenum(matlab.lang.OnOffSwitchState.on)), 'ADR0233');
u9b_chk('isenum_noarg', @() isenum()); u9b_chk('isenum_table', @() isenum(T)); u9b_chk('isenum_cell', @() isenum({}));
u9b_chk('istabular_t', @() istabular(T)); u9b_chk('istabular_tt', @() istabular(timetable(seconds([1; 2]), [1; 2]))); u9b_chk('istabular_num', @() istabular(1)); u9b_chk('istabular_struct', @() istabular(struct('a', 1)));
u9b_chk('istabular_cat', @() istabular(c)); u9b_chk('istabular_noarg', @() istabular()); u9b_chk('istabular_two', @() istabular(T, T)); u9b_chk('istabular_empty', @() istabular(table()));
u9b_chk('isenum_two', @() isenum(1, 2));
u9b_chk('exist_isenum', @() exist('isenum')); u9b_chkdiv('exist_istabular', @() oi_try( @() exist('istabular')), 'ADR0233');



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
    t = strjoin(v.Properties.VariableNames, ',');
elseif isa(v, 'Val116') || isa(v, 'Hnd116')
    t = sprintf('<%s %s>', class(v), mat2str(size(v)));
else
    t = class(v);
end
end

function out = dealwrong(a), [a.p] = deal(4, 5); out = [a.p]; end

function out = dealmore(a), [x, y] = deal(1, 2, 3); out = [x y]; end

function out = gapnoargs(), a = NoArg116(5); a(3) = NoArg116(1); out = [a.p]; end

function out = needsarg(x), out = x + 1; end

function out = needstwo(x, y), out = x + y; end

function out = callsneeds(), out = needsarg(); end

function out = needsif(x), if x, out = 1; else, out = 2; end, end

function out = needsnargin(x), out = nargin; end

function out = needsexist(x), out = exist('x', 'var'); end

function out = undefvar(), out = qqq_undefined + 1; end

function out = rangeassignbad(a), a(1:2) = [Val116(7) Val116(8) Val116(9)]; out = [a.p]; end

function out = numassignbad(), v = 1:5; v(1:2) = [7 8 9]; out = v; end

function x = dotcat(c), x = c.a; end

function x = dotwrite(c), c.a = 1; x = c; end

function x = bracecat(c), x = c{1}; end

function x = strrepw(c)
lastwarn(''); x = strrep(c, 'a', 'b'); [m, id] = lastwarn; x = sprintf('%s | %s | %s', class(x), id, m);
end

function x = table1brace(), T = table(1, {'a'}); x = T{1, :}; end

function x = cscell(), c = {1, 2}; x = horzcat(c{:}); end

function x = csstruct(), st = struct('v', {[1 2], [3 4]}); x = vertcat(st.v); end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end

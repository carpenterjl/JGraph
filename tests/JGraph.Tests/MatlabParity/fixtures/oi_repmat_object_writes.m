% record: -noFigureWindows
% Open item 127 (ADR 0234): repmat past two counts, in the MATLAB dialect, over numbers, chars, cells,
% structs, strings, categoricals, value objects and handles, with R2025b's refusals; and writes
% through an array of value objects reached by a struct field, a cell or an element, which keep the
% array's other elements and run no constructor. No line diverges.
global VAL116_CTOR
VAL116_CTOR = 0;
m = [1 2; 3 4];
u9b_chk('rm_vec3', @() repmat(m, [1 2 2])); u9b_chk('rm_vec3_size', @() size(repmat(m, [1 2 2]))); u9b_chk('rm_args3', @() repmat(m, 1, 2, 2));
u9b_chk('rm_scalar3', @() size(repmat(5, [2 3 4]))); u9b_chk('rm_char3', @() repmat('ab', [1 2 2])); u9b_chk('rm_char3_size', @() size(repmat('ab', [1 2 2])));
u9b_chk('rm_cell3', @() size(repmat({1}, 1, 2, 2))); u9b_chk('rm_cell3_class', @() class(repmat({1, 'a'}, 1, 1, 2))); u9b_chk('rm_cell3_v', @() repmat({1, 'a'}, 1, 1, 2));
u9b_chk('rm_struct3', @() size(repmat(struct('a', 1), [1 2 2]))); u9b_chk('rm_string3', @() size(repmat("s", [2 1 2])));
u9b_chk('rm_string3_v', @() repmat(["a" "b"], [1 1 2])); u9b_chk('rm_cat3', @() size(repmat(categorical({'a'}), [1 2 2])));
u9b_chk('rm_cat3_v', @() repmat(categorical({'a', 'b'}), [1 1 2])); u9b_chk('rm_obj3', @() size(repmat(Val116(1), [1 2 2])));
u9b_chk('rm_obj3_p', @() [repmat(Val116(4), [1 2 2]).p]); u9b_chk('rm_obj3_class', @() class(repmat(Val116(1), 1, 2, 2)));
u9b_chk('rm_h3', @() size(repmat(Hnd116(1), [1 2 2]))); u9b_chk('rm_h3_same', @() hsame());
u9b_chk('rm_int3', @() repmat(int8(3), [1 1 3])); u9b_chk('rm_logical3', @() repmat(true, [1 1 2])); u9b_chk('rm_4d', @() size(repmat(1:3, [1 1 1 2])));
u9b_chk('rm_trailing1', @() size(repmat(m, [2 2 1]))); u9b_chk('rm_zero3', @() size(repmat(m, [0 2 2]))); u9b_chk('rm_neg', @() size(repmat(m, -1, 2)));
u9b_chk('rm_neg3', @() size(repmat(m, [1 -2 2]))); u9b_chk('rm_frac', @() msgof(@() repmat(m, 1.5, 2))); u9b_chk('rm_frac3', @() msgof(@() repmat(m, [1 2 1.5])));
u9b_chk('rm_mixed', @() msgof(@() repmat(m, 2, [3 4]))); u9b_chk('rm_mixed2', @() msgof(@() repmat(m, [1 2], 2)));
u9b_chk('rm_nd_in', @() size(repmat(zeros(2, 2, 2), 2))); u9b_chk('rm_nd_in2', @() size(repmat(zeros(2, 2, 2), [1 1 2])));
u9b_chk('rm_nd_in3', @() size(repmat(zeros(2, 2, 2), 1, 2))); u9b_chk('rm_nd_vals', @() repmat(reshape(1:4, 1, 2, 2), [2 1 1]));
u9b_chk('rm_sparse3', @() msgof(@() repmat(sparse(1), [1 1 2]))); u9b_chk('rm_sparse2', @() issparse(repmat(sparse(1), [2 2])));
u9b_chk('rm_table3', @() msgof(@() repmat(table(1), [1 1 2]))); u9b_chk('rm_table2', @() size(repmat(table(1), [2 2])));
u9b_chk('rm_fh', @() msgof(@() repmat(@sin, [1 2]))); u9b_chk('rm_fh1', @() class(repmat(@sin, 1)));
u9b_chk('rm_empty_in', @() size(repmat(zeros(0, 3), [2 2 2]))); u9b_chk('rm_one_arg', @() msgof(@() repmat(m)));
u9b_chk('rm_count_empty', @() msgof(@() repmat(m, []))); u9b_chk('rm_count_one', @() size(repmat(m, [3])));
u9b_chk('rm_count_col', @() size(repmat(m, [1; 2; 2]))); u9b_chk('rm_count_char', @() msgof(@() repmat(m, 'a')));
u9b_chk('rm_count_logical', @() size(repmat(m, true, 2))); u9b_chk('rm_count_nan', @() msgof(@() repmat(m, NaN, 2))); u9b_chk('rm_count_inf', @() msgof(@() repmat(m, Inf, 1)));
u9b_chk('rm_complex', @() repmat(1 + 2i, [1 1 2])); u9b_chk('rm_single', @() class(repmat(single(1), [1 1 2])));
u9b_chk('rm_args4', @() size(repmat(1, 1, 2, 3, 4))); u9b_chk('rm_args_vec', @() msgof(@() repmat(1, 1, [2 3])));
u9b_chk('rm_struct3_v', @() rmstruct()); u9b_chk('rm_ctor_count', @() rmcount());
u9b_chk('ns_struct', @() ns1()); u9b_chk('ns_struct_grow', @() ns2()); u9b_chk('ns_struct_deep', @() ns3()); u9b_chk('ns_struct_arr', @() ns4());
u9b_chk('ns_struct_name', @() ns5()); u9b_chk('ns_struct_replace', @() ns6()); u9b_chk('ns_struct_idx', @() ns7()); u9b_chk('ns_struct_end', @() ns8());
u9b_chk('ns_struct_endp1', @() ns9()); u9b_chk('ns_struct_delete', @() ns10()); u9b_chk('ns_struct_ctor', @() ns11());
u9b_chk('ns_cell', @() nc1()); u9b_chk('ns_cell_grow', @() nc2()); u9b_chk('ns_cell_name', @() nc3()); u9b_chk('ns_cell_scalar', @() nc4());
u9b_chk('ns_cell_deep', @() nc5()); u9b_chk('ns_cell_delete', @() nc6()); u9b_chk('ns_cell_end', @() nc7()); u9b_chk('ns_cell_paren', @() nc8());
u9b_chk('ns_elem', @() ne1()); u9b_chk('ns_elem_grow', @() ne2()); u9b_chk('ns_elem_name', @() ne3()); u9b_chk('ns_elem_deep', @() ne4());
u9b_chk('ns_obj_struct', @() no1()); u9b_chk('ns_obj_cell', @() no2()); u9b_chk('ns_handle_struct', @() nh1()); u9b_chk('ns_handle_cell', @() nh2());
u9b_chk('ns_handle_alias', @() nh3()); u9b_chk('ns_struct_cs', @() ns12()); u9b_chk('ns_cell_cs', @() nc9());
u9b_chk('ns_struct_nd', @() ns13()); u9b_chk('ns_mixed_cell_struct', @() nm1()); u9b_chk('ns_struct_in_obj_arr', @() nm2());
u9b_chk('ns_bad_prop', @() msgof(@() nbad1())); u9b_chk('ns_bad_index', @() msgof(@() nbad2()));



function m = msgof(f)
try
    f();
    m = 'no error';
catch e
    m = [e.identifier ' | ' e.message];
end
end

function out = hsame()
h = Hnd116(1);
r = repmat(h, [1 2 2]);
r(2).p = 7;
out = [h.p r(1, 1, 2).p];
end

function out = rmstruct()
r = repmat(struct('a', 1), [1 1 2]);
r(1, 1, 2).a = 5;
out = [r.a];
end

function out = rmcount()
global VAL116_CTOR
VAL116_CTOR = 0;
r = repmat(Val116(1), [2 2 2]); %#ok<NASGU>
out = VAL116_CTOR;
end

function out = ns1(), st.list = [Val116(2) Val116(3)]; st.list(2).p = 9; out = [st.list.p]; end

function out = ns2(), st.list = [Val116(2) Val116(3)]; st.list(4).p = 9; out = [st.list.p]; end

function out = ns3(), st.a.b.list = [Val116(2) Val116(3)]; st.a.b.list(1).p = 7; out = [st.a.b.list.p]; end

function out = ns4(), st(2).list = [Val116(2) Val116(3)]; st(2).list(2).p = 8; out = [st(2).list.p]; end

function out = ns5(), st.list = [Val116(2) Val116(3)]; st.list(2).name = 'zz'; out = {st.list.name}; end

function out = ns6(), st.list = [Val116(2) Val116(3)]; st.list(2) = Val116(5); out = [st.list.p]; end

function out = ns7(), st.list = [Val116(2) Val116(3)]; st.list(2).p(3) = 5; out = st.list(2).p; end

function out = ns8(), st.list = [Val116(2) Val116(3)]; st.list(end).p = 6; out = [st.list.p]; end

function out = ns9(), st.list = [Val116(2) Val116(3)]; st.list(end + 1).p = 6; out = [st.list.p]; end

function out = ns10(), st.list = [Val116(2) Val116(3)]; st.list(1) = []; out = [st.list.p]; end

function out = ns11()
global VAL116_CTOR
VAL116_CTOR = 0;
st.list = [Val116(2) Val116(3)];
st.list(5).p = 9;
out = VAL116_CTOR;
end

function out = ns12(), st.list = [Val116(2) Val116(3)]; [st.list.p] = deal(4); out = [st.list.p]; end

function out = ns13(), st.list = cat(3, [Val116(2) Val116(3)], [Val116(4) Val116(5)]); st.list(1, 2, 2).p = 0; out = [st.list.p]; end

function out = nc1(), c = {[Val116(2) Val116(3)]}; c{1}(2).p = 9; out = [c{1}.p]; end

function out = nc2(), c = {[Val116(2) Val116(3)]}; c{1}(3).p = 9; out = [c{1}.p]; end

function out = nc3(), c = {[Val116(2) Val116(3)]}; c{1}(2).name(2) = 'q'; out = c{1}(2).name; end

function out = nc4(), c = {Val116(2), 5}; c{1}.p = 9; out = c{1}.p; end

function out = nc5(), c = {{[Val116(2) Val116(3)]}}; c{1}{1}(1).p = 0; out = [c{1}{1}.p]; end

function out = nc6(), c = {[Val116(2) Val116(3)]}; c{1}(2) = []; out = [c{1}.p]; end

function out = nc7(), c = {[Val116(2) Val116(3)]}; c{end}(end).p = 1; out = [c{1}.p]; end

function out = nc8(), c = {[Val116(2) Val116(3)]}; c(1) = {[Val116(7) Val116(8)]}; c{1}(1).p = 0; out = [c{1}.p]; end

function out = nc9(), c = {[Val116(2) Val116(3)]}; [c{1}.p] = deal(5); out = [c{1}.p]; end

function out = ne1(), a = [Val116(1) Val116(2)]; a(2).name = [Val116(5) Val116(6)]; a(2).name(1).p = 0; out = [a(2).name.p]; end

function out = ne2(), a = [Val116(1) Val116(2)]; a(2).name = [Val116(5) Val116(6)]; a(2).name(3).p = 7; out = [a(2).name.p]; end

function out = ne3(), a = [Val116(1) Val116(2)]; a(1).name = [Val116(5) Val116(6)]; a(1).name(2).name = 'w'; out = a(1).name(2).name; end

function out = ne4(), a = [Val116(1) Val116(2)]; a(1).name = Val116(5); a(1).name.name = [Val116(7) Val116(8)]; a(1).name.name(2).p = 1; out = [a(1).name.name.p]; end

function out = no1(), o = Val116(1); o.name = struct('list', [Val116(2) Val116(3)]); o.name.list(2).p = 9; out = [o.name.list.p]; end

function out = no2(), o = Val116(1); o.name = {[Val116(2) Val116(3)]}; o.name{1}(1).p = 9; out = [o.name{1}.p]; end

function out = nh1(), h = Hnd116(1); st.list = [h Hnd116(2)]; st.list(1).p = 9; out = [h.p st.list(1).p]; end

function out = nh2(), h = Hnd116(1); c = {[h Hnd116(2)]}; c{1}(1).p = 9; out = [h.p c{1}(1).p]; end

function out = nh3(), h = Hnd116(1); st.list = [Hnd116(2) h]; c = st; c.list(2).p = 4; out = [h.p st.list(2).p]; end

function out = nm1(), st.c = {[Val116(2) Val116(3)]}; st.c{1}(2).p = 9; out = [st.c{1}.p]; end

function out = nm2(), a = [Val116(1) Val116(2)]; a(2).name = struct('list', [Val116(5) Val116(6)]); a(2).name.list(2).p = 0; out = [a(2).name.list.p]; end

function nbad1(), st.list = [Val116(2) Val116(3)]; st.list(2).zz = 9; end

function nbad2(), st.list = [Val116(2) Val116(3)]; st.list(0).p = 9; end

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

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end

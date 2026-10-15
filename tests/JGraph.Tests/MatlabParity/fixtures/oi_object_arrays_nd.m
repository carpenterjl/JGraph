% record: -noFigureWindows
% Open item 121 (ADR 0233): object arrays past two dimensions, in the MATLAB dialect: cat(3, ...),
% three subscripts to read, write, grow and delete, size, ndims, reshape, permute, squeeze, flip,
% num2cell, isvalid and == keep the shape; a value-object array held in a property is written through
% (o.list(2).p = 9); a method named after a dot on an array is called (a.total); a comma-separated
% list spreads into subscripts (v(a(1:2).p)); a struct or an empty string beside an object goes
% through the constructor; cell2mat joins objects. The classes Val116 and Hnd116 live in helpers/. ADR
% 0234 made repmat with three counts and the writes through a struct, a cell or an element of an array
% on the way to the array exact.
global VAL116_CTOR
VAL116_CTOR = 0;
s1 = Val116(1); s2 = Val116(2); s3 = Val116(3);
a = [s1 s2 s3];
b = [s1 s2; s3 s1];
h1 = Hnd116(1); h2 = Hnd116(2); h3 = Hnd116(3); ha = [h1 h2 h3];
t3 = cat(3, a, a);
u9b_chk('nd_size', @() size(t3)); u9b_chk('nd_numel', @() numel(t3)); u9b_chk('nd_ndims', @() ndims(t3)); u9b_chk('nd_class', @() class(t3));
u9b_chk('nd_p', @() [t3.p]); u9b_chk('nd_page', @() size(t3(:, :, 2))); u9b_chk('nd_page_p', @() [t3(:, :, 2).p]); u9b_chk('nd_el', @() t3(1, 2, 2).p);
u9b_chk('nd_lin', @() t3(5).p); u9b_chk('nd_colon', @() size(t3(:))); u9b_chk('nd_size3', @() size(t3, 3)); u9b_chk('nd_end', @() t3(end).p); u9b_chk('nd_end3', @() t3(1, 1, end).p);
u9b_chk('nd_cat3_b', @() size(cat(3, b, b))); u9b_chk('nd_cat3_b_p', @() [subsel(cat(3, b, b)).p]);
u9b_chk('nd_reshape', @() size(reshape(a, 1, 1, 3))); u9b_chk('nd_reshape_p', @() [reshape(a, 1, 1, 3).p]);
u9b_chk('nd_permute', @() size(permute(t3, [3 1 2]))); u9b_chk('nd_squeeze', @() size(squeeze(t3))); u9b_chk('nd_squeeze_p', @() [squeeze(t3).p]);
u9b_chk('nd_write', @() ndwrite(t3)); u9b_chk('nd_grow', @() ndgrow(a)); u9b_chk('nd_grow_p', @() ndgrowp(a)); u9b_chk('nd_delete_page', @() nddel(t3));
u9b_chk('nd_dot_write', @() nddot(t3)); u9b_chk('nd_for', @() ndfor(t3)); u9b_chk('nd_arrayfun', @() arrayfun(@(o) o.p, t3)); u9b_chk('nd_arrayfun_size', @() size(arrayfun(@(o) o.p, t3)));
u9b_chk('nd_isequal', @() isequal(t3, t3)); u9b_chk('nd_isempty', @() isempty(t3)); u9b_chk('nd_cat_mismatch', @() size(cat(3, a, b)));
u9b_chk('nd_handle', @() size(cat(3, ha, ha))); u9b_chk('nd_handle_valid', @() isvalid(cat(3, ha, ha))); u9b_chk('nd_handle_eq', @() cat(3, ha, ha) == h1);
u9b_chk('nd_cs_name', @() {t3(:, 2, :).name}); u9b_chk('nd_num2cell', @() size(num2cell(t3))); u9b_chk('nd_flip', @() [flip(t3, 3).p]);
u9b_chk('nd_repmat', @() size(repmat(s1, [1 2 2]))); u9b_chk('nd_empty3', @() size(Val116.empty(0, 2, 3)));
u9b_chk('nd_cat4', @() size(cat(4, s1, s2)));
u9b_chk('nd_horzcat_3d', @() size([t3 t3]));
u9b_chk('nd_vertcat_3d', @() size([t3; t3]));
u9b_chk('nd_index_two', @() size(t3(1, 4)));
u9b_chk('nd_index_two_p', @() t3(1, 4).p);
u9b_chk('nest_write', @() nest1()); u9b_chk('nest_grow', @() nest2()); u9b_chk('nest_replace', @() nest3()); u9b_chk('nest_name_char', @() nest4()); u9b_chk('nest_cs', @() nest5());
u9b_chk('nest_struct', @() nest6()); u9b_chk('nest_cell', @() nest7()); u9b_chk('nest_handle', @() nest8()); u9b_chk('nest_deep', @() nest9()); u9b_chk('nest_handle_val', @() nest10());
u9b_chk('nest_delete', @() nest11()); u9b_chk('nest_end', @() nest12()); u9b_chk('nest_in_arr', @() nest13());
u9b_chk('m_arr', @() a.total); u9b_chk('m_scalar', @() s1.total); u9b_chk('m_arr_paren', @() a.total()); u9b_chk('m_fn', @() total(a)); u9b_chk('m_bump', @() [a.bump.p]); u9b_chk('m_bump_s', @() s1.bump.p);
u9b_chk('m_sub', @() a(1:2).total); u9b_chk('m_class', @() class(a.bump)); u9b_chk('m_h_bump', @() hbump(ha)); u9b_chk('m_in_expr', @() a.total + 1);
u9b_chk('m_handle_noout', @() hstate(h1));
u9b_chk('m_paren_arg', @() mixtotal(a));
v = 10:20; m = magic(4);
u9b_chk('ls_v', @() v(a(1:2).p)); u9b_chk('ls_m', @() m(a(1:2).p)); u9b_chk('ls_m3', @() m(a(1:2).p, 1)); u9b_chk('ls_all', @() lsall(v, a)); u9b_chk('ls_cell', @() lscell(a)); u9b_chk('ls_fn', @() max(a(1:2).p)); u9b_chk('ls_fn3', @() plus(a(1:2).p));
u9b_chk('ls_struct', @() lsstruct()); u9b_chk('ls_brace', @() lsbrace(a)); u9b_chk('ls_write', @() lswrite(a)); u9b_chk('ls_one', @() v(a(1).p)); u9b_chk('ls_zeros', @() size(zeros(a(2:3).p)));
u9b_chk('fp_struct', @() class([s1 struct('p', 1)])); u9b_chk('fp_struct_p', @() class(fp1())); u9b_chk('fp_struct_size', @() size([s1 struct('p', 1)]));
u9b_chk('fp_struct2', @() size([s1 struct('p', {1, 2})])); u9b_chk('fp_struct_first', @() class([struct('p', 1) s1]));
u9b_chk('fp_string_empty', @() size([s1 string.empty])); u9b_chk('fp_string_empty_p', @() fp2());
u9b_chk('fp_struct_empty', @() size([s1 struct([])])); u9b_chk('fp_empty', @() size([s1 []])); u9b_chk('fp_cell_empty', @() size([s1 {}])); u9b_chk('fp_zeros', @() size([s1 zeros(1, 0)])); u9b_chk('fp_char_empty', @() size([s1 ''])); u9b_chk('fp_char', @() size([s1 'ab']));
u9b_chk('fp_num', @() size([s1 5])); u9b_chk('fp_numvec', @() size([s1 [5 6]])); u9b_chk('fp_numvec_p', @() fp3()); u9b_chk('fp_string', @() size([s1 "ab"])); u9b_chk('fp_logical', @() size([s1 true]));
u9b_chk('fp_vert_struct', @() size([s1; struct('p', 1)])); u9b_chk('fp_ctor_count', @() fpcount());
u9b_chk('c2m_col', @() size(cell2mat({a; a}))); u9b_chk('c2m_row', @() size(cell2mat({a, a}))); u9b_chk('c2m_scalars', @() size(cell2mat({s1, s2}))); u9b_chk('c2m_class', @() class(cell2mat({s1, s2}))); u9b_chk('c2m_p', @() [subsel2(cell2mat({a; a})).p]);
u9b_chk('c2m_handle', @() size(cell2mat({ha; ha}))); u9b_chk('c2m_mixed', @() class(cell2mat({s1, 5})));
u9b_chk('c2m_one', @() class(cell2mat({s1})));



function pr(label, f)
try
    v = f();
    fprintf('%s\t%s\t%s\t%s\n', label, class(v), mat2str(size(v)), s(v));
catch e
    fprintf('%s\tERR\t%s\t%s\n', label, e.identifier, strrep(e.message, newline, ' '));
end
end

function t = s(v)
if isnumeric(v) || islogical(v)
    t = mat2str(v, 10);
elseif ischar(v)
    t = strrep(v, newline, '\n');
elseif isstring(v)
    v(ismissing(v)) = "<missing>"; t = strjoin(reshape(v, 1, []), ',');
elseif iscell(v)
    t = strjoin(cellfun(@(x) s(x), reshape(v, 1, []), 'UniformOutput', false), '|');
elseif isstruct(v)
    t = strjoin(fieldnames(v)', ',');
elseif isa(v, 'Val116') || isa(v, 'Hnd116')
    t = sprintf('<%s %s>', class(v), mat2str([v.p]));
else
    t = class(v);
end
end

function x = subsel(t), x = t(:, :, 2); end

function x = subsel2(t), x = t(2, :); end

function out = ndwrite(t), t(1, 2, 2) = Val116(9); out = [t.p]; end

function out = ndgrow(a), a(1, 1, 2) = Val116(9); out = size(a); end

function out = ndgrowp(a), a(1, 2, 2) = Val116(9); out = [a.p]; end

function out = nddel(t), t(:, :, 1) = []; out = size(t); end

function out = nddot(t), t(1, 1, 2).p = 7; out = [t.p]; end

function out = ndfor(t), out = []; for o = t, out(end + 1) = numel(o); end, end

function out = nest1(), o = Val116(1); o.name = [Val116(2) Val116(3)]; o.name(2).p = 9; out = [o.name.p]; end

function out = nest2(), o = Val116(1); o.name = [Val116(2) Val116(3)]; o.name(4).p = 4; out = [o.name.p]; end

function out = nest3(), o = Val116(1); o.name = [Val116(2) Val116(3)]; o.name(2) = Val116(7); out = [o.name.p]; end

function out = nest4(), o = Val116(1); o.name = [Val116(2) Val116(3)]; o.name(2).name(1) = 'Q'; out = o.name(2).name; end

function out = nest5(), o = Val116(1); o.name = [Val116(2) Val116(3)]; [o.name.p] = deal(5); out = [o.name.p]; end

function out = nest6(), st.list = [Val116(2) Val116(3)]; st.list(2).p = 9; out = [st.list.p]; end

function out = nest7(), c = {[Val116(2) Val116(3)]}; c{1}(2).p = 9; out = [c{1}.p]; end

function out = nest8(), h = Hnd116(1); h.name = [Hnd116(2) Hnd116(3)]; k = h.name(2); h.name(2).p = 9; out = [k.p h.name(2).p]; end

function out = nest9(), o = Val116(1); o.name = Val116(2); o.name.name = [Val116(4) Val116(5)]; o.name.name(1).p = 8; out = [o.name.name.p]; end

function out = nest10(), h = Hnd116(1); h.name = [Val116(2) Val116(3)]; h.name(2).p = 9; out = [h.name.p]; end

function out = nest11(), o = Val116(1); o.name = [Val116(2) Val116(3)]; o.name(1) = []; out = [o.name.p]; end

function out = nest12(), o = Val116(1); o.name = [Val116(2) Val116(3)]; o.name(end).p = 6; out = [o.name.p]; end

function out = nest13(), a = [Val116(1) Val116(2)]; a(2).name = [Val116(5) Val116(6)]; a(2).name(1).p = 0; out = [a(2).name.p]; end

function out = hbump(ha), ha.bump; out = [ha.p]; end

function out = hstate(h), h.bump; out = h.p; end

function out = mixtotal(a), out = a(2).total; end

function out = lsall(v, a), out = v(a.p); end

function out = lscell(a), c = {1, 2; 3, 4}; out = c{a(1:2).p}; end

function out = lsstruct(), st = struct('p', {1, 2}); m = magic(3); out = m(st.p); end

function out = lsbrace(a), c = {10, 20, 30}; x = {a.p}; out = c{x{2}}; end

function out = lswrite(a), m = zeros(2); m(a(1:2).p) = 5; out = m; end

function out = fp1(), x = [Val116(1) struct('p', 1)]; out = x(2).p; end

function out = fp2(), x = [Val116(1) string.empty]; out = sprintf('%s %s', class(x(2).p), mat2str(size(x(2).p))); end

function out = fp3(), x = [Val116(1) [5 6]]; out = x(2).p; end

function out = fpcount(), global VAL116_CTOR; VAL116_CTOR = 0; x = [Val116(1) struct('p', {1, 2}) 7]; out = [VAL116_CTOR numel(x)]; end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end

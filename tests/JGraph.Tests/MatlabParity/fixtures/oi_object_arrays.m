% record: -noFigureWindows
% Open item 116 (ADR 0232): object arrays past the first cut, in the MATLAB dialect. a.p on an
% array is a comma-separated list ([a.p], {a.name}, [a.p] = deal(...), x = a.p takes the first);
% a(r, c) reads, writes and deletes; a gap a(5) = s opens is filled by the class's no-argument
% constructor; a foreign piece in a bracket goes through the class constructor; handle arrays
% hold the objects themselves (a write through a(2) reaches the object, == is identity, delete and
% isvalid take the array); Class.empty; isequal, arrayfun, cellfun, num2cell, cell2mat and the
% shape verbs over objects; properties, methods and isprop of an array. The classes Val116, Hnd116,
% Sub116, NoArg116 and Zero116 live in helpers/. Lines marked div=ADR0232 keep this build's own
% words where R2025b's message names an internal (an operator on a value object, an N-D cat, the
% order a sort gives handles).
global VAL116_CTOR
global VAL116_CTOR
VAL116_CTOR = 0;
s1 = Val116(1); s2 = Val116(2); s3 = Val116(3);
a = [s1 s2 s3];
b = [s1 s2; s3 s1];
u9b_chk('a_class', @() class(a)); u9b_chk('a_size', @() size(a));
u9b_chk('cs_bracket', @() [a.p]); u9b_chk('cs_brace', @() {a.p}); u9b_chk('cs_brace_name', @() {a.name}); u9b_chk('cs_bracket_name', @() [a.name]);
u9b_chk('cs_vertcat', @() vertcat(a.p)); u9b_chk('cs_numel', @() numel(a.p)); u9b_chk('cs_max', @() max([a.p])); u9b_chk('cs_plus_args', @() plus(a(1:2).p));
u9b_chk('cs_single_out', @() singleout(a)); u9b_chk('cs_two_out', @() twoout(a)); u9b_chk('cs_three_out', @() threeout(a)); u9b_chk('cs_four_out', @() fourout(a));
u9b_chk('cs_deal', @() dealinto(a)); u9b_chk('cs_deal_each', @() dealeach(a)); u9b_chk('cs_deal_wrong_count', @() dealwrong(a)); u9b_chk('cs_assign_list', @() assignlist(a));
u9b_chk('cs_sub', @() [a(2:3).p]); u9b_chk('cs_sub_logical', @() [a([true false true]).p]); u9b_chk('cs_empty', @() [a([]).p]); u9b_chk('cs_empty_size', @() size([a([]).p])); u9b_chk('cs_empty_brace', @() size({a([]).p}));
u9b_chk('cs_nested', @() nested(a)); u9b_chk('cs_in_cell', @() {a.p, 9});
u9b_chk('cs_dot_on_scalar', @() s1.p); u9b_chk('dot_on_array_assign', @() dotassign(a)); u9b_chk('dot_colon_assign', @() dotassignall(a)); u9b_chk('dot_colon_deal', @() dotassignallcs(a));
u9b_chk('cs_index_into', @() a(2).p(1)); u9b_chk('cs_arr_index', @() arrindex(a)); u9b_chk('cs_arr_index_write', @() arrindexwrite(a)); u9b_chk('cs_arr_field_grow', @() fieldgrow(a));
u9b_chk('cs_struct_like_name', @() [a.name]); u9b_chk('a_p_end', @() a(end).p); u9b_chk('a_p_deref_end', @() a(end).name(end));
u9b_chk('cs_method_cs_arg', @() total(a)); u9b_chk('cs_in_struct_field', @() structfield(a)); u9b_chk('cs_from_cell_holder', @() cellcs(a));
u9b_chk('cs_isempty', @() isempty([a.p])); u9b_chk('cs_func_handle', @() feval(@(varargin) numel(varargin), a.p));
u9b_chk('cs_cell_write', @() cswrite(a)); u9b_chk('cs_as_args_to_zeros', @() size(zeros(a(1:2).p)));
u9b_chk('cs_in_bracket_mixed', @() [a.p 10]); u9b_chk('cs_in_bracket_mixed_rows', @() [a.p; 10 20 30]);
u9b_chk('cs_dot_chain', @() [a.name]); u9b_chk('cs_dot_dot', @() dotdot());
u9b_chkdiv('cs_dot_end', @() oi_try( @() a.p(end)), 'ADR0232');
u9b_chk('b_size', @() size(b)); u9b_chk('b_12', @() b(1, 2).p); u9b_chk('b_21', @() b(2, 1).p); u9b_chk('b_row', @() [b(1, :).p]); u9b_chk('b_col', @() [b(:, 2).p]); u9b_chk('b_lin', @() [b(:).p]); u9b_chk('b_end', @() b(end, end).p); u9b_chk('b_end2', @() b(end).p);
u9b_chk('b_assign_2d', @() assign2d(b)); u9b_chk('b_assign_grow_2d', @() assigngrow2d(b)); u9b_chk('b_assign_grow_2d_vals', @() assigngrow2dvals(b)); u9b_chk('b_delete_row', @() delrow(b)); u9b_chk('b_delete_lin', @() dellin(b)); u9b_chk('b_delete_col', @() delcol(b)); u9b_chk('b_delete_all', @() delall(b));
u9b_chk('b_reshape', @() size(reshape(b, 1, 4))); u9b_chk('b_reshape_vals', @() [reshape(b, 1, 4).p]); u9b_chk('b_transpose_vals', @() transposevals(b)); u9b_chk('b_transpose_size', @() size(b')); u9b_chk('b_ctranspose', @() size(b.'));
u9b_chk('b_cat_dim1', @() size(cat(1, b, b))); u9b_chk('b_horzcat', @() size(horzcat(a, a))); u9b_chk('b_vertcat', @() size(vertcat(a, a))); u9b_chk('b_vertcat_mismatch', @() vertcat(a, a(1:2)));
u9b_chkdiv('b_cat3', @() oi_try( @() size(cat(3, a, a))), 'ADR0232'); u9b_chk('b_repmat', @() size(repmat(a, 2, 2))); u9b_chk('b_repmat_vals', @() [repmat(a, 1, 2).p]); u9b_chk('b_fliplr', @() [fliplr(a).p]); u9b_chk('b_flipud', @() [flipud(b).p]); u9b_chk('b_circshift', @() [circshift(a, 1).p]); u9b_chk('b_rot90', @() [rot90(b).p]);
u9b_chk('b_colon', @() size(b(:))); u9b_chk('b_isrow', @() isrow(a)); u9b_chk('b_iscolumn', @() iscolumn(a')); u9b_chk('b_isvector', @() isvector(b)); u9b_chk('b_isscalar', @() isscalar(s1)); u9b_chk('b_isempty', @() isempty(a)); u9b_chk('b_numel', @() numel(b)); u9b_chk('b_length', @() length(b)); u9b_chk('b_ndims', @() ndims(b)); u9b_chk('b_size_dim', @() size(a, 2)); u9b_chk('b_numel_idx', @() numel(a, 1));
u9b_chk('b_squeeze', @() size(squeeze(b))); u9b_chk('b_permute', @() size(permute(b, [2 1]))); u9b_chkdiv('b_3d', @() oi_try( @() size(cat(3, b, b))), 'ADR0232'); u9b_chkdiv('b_3d_index', @() oi_try( @() threed(b)), 'ADR0232'); u9b_chkdiv('b_3d_cs', @() oi_try( @() threedcs(b)), 'ADR0232');
u9b_chk('b_index_2d_range', @() size(b(1:2, 1))); u9b_chk('b_index_logical_2d', @() [b(logical([1 0; 0 1])).p]); u9b_chk('b_index_out', @() b(3, 1)); u9b_chk('b_index_zero', @() b(0)); u9b_chk('b_index_frac', @() b(1.5));
u9b_chk('b_index_colon_colon', @() size(b(:, :))); u9b_chk('b_index_end_arith', @() b(end - 1).p); u9b_chk('b_index_matrix_idx', @() size(a([1 2; 2 3])));
u9b_chk('b_index_1d_on_2d', @() [b(1:3).p]); u9b_chk('b_linear_assign_2d', @() linassign2d(b));
u9b_chk('gap_fill', @() gapfill(a)); u9b_chk('gap_fill_class', @() gapclass(a)); u9b_chk('gap_fill_sub', @() gapsub()); u9b_chk('gap_fill_ctor_called', @() gapctor()); u9b_chk('gap_fill_ctor_called_2d', @() gapctor2d());
u9b_chk('gap_fill_2d', @() gap2d(a)); u9b_chk('gap_fill_empty_start', @() gapempty()); u9b_chkdiv('gap_fill_noargs_ctor', @() oi_try( @() gapnoargs()), 'ADR0232'); u9b_chk('gap_fill_dot', @() gapdot(a)); u9b_chk('gap_fill_dot_vals', @() gapdotvals(a));
u9b_chk('gap_fill_default_prop', @() gapdefault()); u9b_chk('gap_fill_mixed', @() gapmixed());
u9b_chk('gap_fill_from_empty_static', @() gapfromstatic());
u9b_chk('gap_fill_from_zero_by_zero', @() gapfromzero());
u9b_chk('isequal_same', @() isequal(a, [s1 s2 s3])); u9b_chk('isequal_diff', @() isequal(a, [s1 s2 s1])); u9b_chk('isequal_shape', @() isequal(a, a')); u9b_chk('isequal_scalar', @() isequal(s1, Val116(1))); u9b_chk('isequal_class', @() isequal(s1, Sub116(1))); u9b_chk('isequal_len', @() isequal(a, a(1:2))); u9b_chk('isequal_struct', @() isequal(s1, struct('p', 1, 'name', 'x')));
u9b_chk('isequal_nested', @() isequal({a}, {[s1 s2 s3]})); u9b_chk('isequal_nested_obj', @() isequal(Val116(a), Val116(a)));
u9b_chk('isequaln', @() isequaln(a, a)); u9b_chk('isequal_three', @() isequal(a, a, a));
u9b_chk('eq_op', @() s1 == s2); u9b_chk('eq_op_arr', @() a == a); u9b_chk('ne_op', @() s1 ~= s2); u9b_chk('lt_op', @() s1 < s2); u9b_chk('plus_op', @() s1 + s2); u9b_chk('uminus_op', @() -s1); u9b_chk('not_op', @() ~s1); u9b_chk('and_op', @() s1 & s2); u9b_chk('colon_op', @() s1:s2); u9b_chk('transpose_scalar', @() size(s1'));
u9b_chk('eq_struct', @() s1 == struct('p', 1)); u9b_chk('eq_num', @() s1 == 1); u9b_chk('num_eq_obj', @() 1 == s1);
u9b_chk('plus_num_obj', @() 1 + s1); u9b_chk('mtimes', @() s1 * 2); u9b_chkdiv('abs_obj', @() oi_try( @() abs(s1)), 'ADR0232'); u9b_chkdiv('double_obj', @() oi_try( @() double(s1)), 'ADR0232'); u9b_chkdiv('logical_obj', @() oi_try( @() logical(s1)), 'ADR0232'); u9b_chkdiv('cell_obj', @() oi_try( @() cell(s1)), 'ADR0232'); u9b_chk('struct_obj', @() struct(s1)); u9b_chkdiv('num2str_obj', @() oi_try( @() num2str(s1)), 'ADR0232'); u9b_chkdiv('mat2str_obj', @() oi_try( @() mat2str(s1)), 'ADR0232'); u9b_chkdiv('sprintf_obj', @() oi_try( @() sprintf('%d', s1)), 'ADR0232'); u9b_chkdiv('sum_obj', @() oi_try( @() sum(a)), 'ADR0232'); u9b_chkdiv('max_obj', @() oi_try( @() max(a)), 'ADR0232'); u9b_chk('ismember_obj', @() ismember(s1, a)); u9b_chkdiv('any_obj', @() oi_try( @() any(a)), 'ADR0232'); u9b_chkdiv('nnz_obj', @() oi_try( @() nnz(a)), 'ADR0232'); u9b_chkdiv('cumsum_obj', @() oi_try( @() cumsum(a)), 'ADR0232'); u9b_chkdiv('mean_obj', @() oi_try( @() mean(a)), 'ADR0232'); u9b_chk('numel_obj_args', @() numel(a, 1, 2)); u9b_chk('length_obj', @() length(a));
u9b_chk('if_obj', @() ifobj(s1)); u9b_chk('while_obj', @() s1 && true); u9b_chk('obj_in_switch', @() switchobj(s1));
u9b_chk('arrayfun_p', @() arrayfun(@(o) o.p, a)); u9b_chk('arrayfun_obj', @() arrayfun(@(o) o, a)); u9b_chk('arrayfun_obj_class', @() class(arrayfun(@(o) o, a))); u9b_chk('arrayfun_nonuni', @() arrayfun(@(o) o.name, a, 'UniformOutput', false)); u9b_chk('arrayfun_2d', @() arrayfun(@(o) o.p, b)); u9b_chk('arrayfun_2d_size', @() size(arrayfun(@(o) o.p, b)));
u9b_chk('arrayfun_newobj', @() arrayfun(@(k) Val116(k), 1:3)); u9b_chk('arrayfun_newobj_class', @() class(arrayfun(@(k) Val116(k), 1:3))); u9b_chk('arrayfun_newobj_size', @() size(arrayfun(@(k) Val116(k), 1:3))); u9b_chk('arrayfun_newobj_cs', @() [arrayfun(@(k) Val116(k), 1:3).p]);
u9b_chk('arrayfun_obj_two_out', @() twoouts(a)); u9b_chk('arrayfun_obj_err', @() arrayfun(@(o) [o.p o.p], a)); u9b_chk('arrayfun_empty', @() size(arrayfun(@(o) o.p, a([])))); u9b_chk('arrayfun_empty_obj', @() class(arrayfun(@(o) o, a([]))));
u9b_chk('arrayfun_obj_handle', @() class(arrayfun(@(k) Hnd116(k), 1:2))); u9b_chk('arrayfun_obj_handle_size', @() size(arrayfun(@(k) Hnd116(k), 1:2)));
u9b_chk('arrayfun_mixed_class', @() arrayfun(@(k) iff(k), 1:2));
u9b_chk('cellfun_on_arr', @() cellfun(@(o) o.p, {s1, s2})); u9b_chk('cellfun_class', @() cellfun(@class, {s1, a}, 'UniformOutput', false)); u9b_chk('cellfun_obj_out', @() class(cellfun(@(o) o, {s1, s2}))); u9b_chk('cellfun_obj_out_size', @() size(cellfun(@(o) o, {s1, s2})));
u9b_chk('num2cell', @() class(num2cell(a))); u9b_chk('num2cell_size', @() size(num2cell(a))); u9b_chk('num2cell_el', @() class(num2cellel(a))); u9b_chk('num2cell_2d', @() size(num2cell(b)));
u9b_chk('cell2mat_obj', @() cell2mat({s1, s2})); u9b_chk('cat_cell_of_obj', @() class([{s1} {s2}])); u9b_chkdiv('horzcat_cells', @() oi_try( @() class(horzcat({s1}, {s2}))), 'ADR0232');
u9b_chk('cellfun_isobject', @() cellfun(@isobject, {s1, 1}));
u9b_chk('structfun_obj', @() structfun(@(x) x.p, struct('a', s1, 'b', s2)));
u9b_chkdiv('mat_of_obj_in_cell', @() oi_try( @() size(cell2mat({a; a}))), 'ADR0232');
u9b_chk('properties_arr', @() properties(a)); u9b_chk('methods_arr', @() methods(a)); u9b_chk('fieldnames_arr', @() fieldnames(a)); u9b_chk('isprop_arr', @() isprop(a, 'p')); u9b_chk('isprop_arr_size', @() size(isprop(a, 'p'))); u9b_chk('ismethod_arr', @() ismethod(a, 'bump')); u9b_chk('struct_arr', @() struct(a)); u9b_chk('struct_arr_size', @() size(struct(a))); u9b_chk('struct_arr_vals', @() [struct(a).p]);
u9b_chk('isa_arr', @() isa(a, 'Val116')); u9b_chk('isobject_arr', @() isobject(a)); u9b_chk('isstruct_arr', @() isstruct(a)); u9b_chk('class_empty', @() class(a([]))); u9b_chk('empty_size', @() size(a([]))); u9b_chk('empty_static', @() size(Val116.empty)); u9b_chk('empty_static_03', @() size(Val116.empty(0, 3))); u9b_chk('empty_static_class', @() class(Val116.empty)); u9b_chk('empty_static_bad', @() Val116.empty(1, 2));
u9b_chk('isempty_static', @() isempty(Val116.empty)); u9b_chk('empty_cs', @() size([Val116.empty.p])); u9b_chk('empty_cat', @() size([Val116.empty s1])); u9b_chk('empty_cat_class', @() class([Val116.empty s1]));
u9b_chkdiv('metaclass_arr', @() oi_try( @() class(metaclass(a))), 'ADR0232');
u9b_chk('isfield_obj', @() isfield(s1, 'p')); u9b_chk('getfield_obj', @() getfield(s1, 'p')); u9b_chkdiv('setfield_obj', @() oi_try( @() setfield(s1, 'p', 5).p), 'ADR0232'); u9b_chkdiv('rmfield_obj', @() oi_try( @() rmfield(s1, 'p')), 'ADR0232'); u9b_chkdiv('orderfields_obj', @() oi_try( @() orderfields(s1)), 'ADR0232');
u9b_chk('isequal_prop_list', @() isequal(properties(a), properties(s1)));
u9b_chk('method_on_arr', @() [bump(a).p]); u9b_chk('method_on_arr_dot', @() [a.bump().p]); u9b_chk('method_scalar', @() s1.bump().p); u9b_chk('method_on_arr_dot_noparen', @() [a.bump.p]);
u9b_chk('method_total', @() total(a)); u9b_chkdiv('method_total_dot', @() oi_try( @() a.total), 'ADR0232');
u9b_chk('method_on_2d', @() size(bump(b))); u9b_chk('method_on_empty', @() size(bump(a([]))));
u9b_chk('method_on_arr_elem', @() a(2).bump().p);
u9b_chk('for_row', @() forrow(a)); u9b_chk('for_2d', @() for2d(b)); u9b_chk('for_col', @() forrow(a')); u9b_chk('for_empty', @() forrow(a([]))); u9b_chk('for_3d', @() forrow(cat(3, a, a)));
u9b_chk('for_row_vals', @() forvals(a)); u9b_chk('for_cell_of_obj', @() forrow({s1, s2}));
u9b_chk('while_numel', @() whilearr(a));
u9b_chk('sub_array', @() class([Sub116(1) Sub116(2)])); u9b_chk('sub_cs', @() subcs()); u9b_chkdiv('mixed_sub_val', @() oi_try( @() mixed()), 'ADR0232'); u9b_chkdiv('mixed_val_sub', @() oi_try( @() mixedrev()), 'ADR0232'); u9b_chk('mixed_sub_val_cs', @() mixedcs());
u9b_chk('assign_sub_into_val', @() subintoval(a)); u9b_chk('assign_val_into_sub', @() valintosub()); u9b_chk('assign_wrong_class', @() wrongclass(a)); u9b_chk('assign_struct', @() assignstruct(a)); u9b_chk('assign_num', @() assignnum(a)); u9b_chk('assign_cell', @() assigncell(a)); u9b_chk('assign_empty_cell', @() assignemptycell(a)); u9b_chk('assign_empty_str', @() assignemptystr(a));
u9b_chk('assign_scalar_to_range', @() rangeassign(a)); u9b_chk('assign_arr_to_range', @() rangeassignarr(a)); u9b_chkdiv('assign_arr_to_range_mismatch', @() oi_try( @() rangeassignbad(a)), 'ADR0232'); u9b_chk('a_logical_mask_write', @() maskwrite(a)); u9b_chk('assign_colon', @() colonassign(a)); u9b_chk('assign_colon_arr', @() colonassignarr(a)); u9b_chk('assign_end_plus', @() endplus(a));
u9b_chk('assign_whole_from_scalar', @() wholefromscalar(a)); u9b_chk('assign_zero_index', @() zeroindex(a)); u9b_chk('assign_two_idx_on_row', @() twoidxrow(a)); u9b_chk('assign_two_idx_on_row_vals', @() twoidxrowvals(a));
u9b_chk('assign_elem_prop_on_shared', @() sharedwrite(a)); u9b_chk('nested_obj', @() nestedobj()); u9b_chkdiv('nested_obj_write', @() oi_try( @() nestedwrite()), 'ADR0232');
u9b_chk('deal_struct_like', @() dealstruct()); u9b_chk('deal_name', @() dealname(a));
u9b_chk('assign_cs_from_cs', @() csfromcs(a));
u9b_chk('assign_cs_from_vector', @() csfromvec(a));
u9b_chk('assign_cs_from_cell', @() csfromcell(a));
u9b_chk('assign_cs_logical', @() cslogical(a));
u9b_chk('assign_cs_end', @() csend(a));
u9b_chk('assign_cs_one_elem', @() csone(a));
u9b_chk('assign_cs_grow', @() csgrow(a));
u9b_chk('assign_cs_2d', @() cs2d(b));
u9b_chk('assign_cs_nested_field', @() csnested(a));
u9b_chk('assign_cs_method_result', @() csmethod(a));
u9b_chk('struct_to_obj_array', @() structtoobj());
u9b_chk('obj_array_props_order', @() properties([Sub116(1) Sub116(2)]));
u9b_chk('obj_array_in_struct_cs', @() structcs());
u9b_chk('obj_cs_as_struct_arg', @() struct('f', {a.p}));
u9b_chk('obj_cs_as_struct_arg_size', @() size(struct('f', {a.p})));
u9b_chk('obj_cs_cellfun', @() cellfun(@(x) x * 2, {a.p}));
u9b_chk('obj_cs_sum_brackets', @() sum([a.p]));
u9b_chk('obj_cs_in_sprintf', @() sprintf('%d,', a.p));
u9b_chk('obj_cs_in_fprintf_names', @() sprintf('%s,', a.name));
u9b_chk('obj_cs_in_max_two_args', @() max(a(1:2).p));
u9b_chk('obj_cs_in_horzcat_fn', @() horzcat(a.p));
u9b_chk('obj_cs_in_cat_fn', @() cat(1, a.p));
u9b_chk('obj_cs_strjoin', @() strjoin({a.name}, '-'));
u9b_chk('obj_cs_in_cellarray_literal_2d', @() size({a.p; a.p}));
u9b_chkdiv('obj_cs_in_index', @() oi_try( @() csindex(a)), 'ADR0232');
u9b_chk('obj_cs_in_anon', @() feval(@(varargin) nargin, a.p));
u9b_chk('obj_cs_sub_dot_index', @() a(2:3).name(1));
u9b_chk('obj_cs_numel_dot', @() numel(a(2:3).name));
h1 = Hnd116(1); h2 = Hnd116(2); h3 = Hnd116(3);
ha = [h1 h2 h3];
u9b_chk('h_class', @() class(ha)); u9b_chk('h_size', @() size(ha)); u9b_chk('h_cs', @() [ha.p]); u9b_chk('h_cs_brace', @() {ha.name});
u9b_chk('h_write_through', @() hwrite(ha, h2)); u9b_chk('h_elem_write_shares', @() helem(ha, h1)); u9b_chk('h_copy_write_through', @() hcopy(ha, h3));
u9b_chk('h_eq', @() ha(1) == h1); u9b_chk('h_eq_arr', @() ha == h1); u9b_chk('h_eq_other', @() h1 == h2); u9b_chk('h_ne', @() ha ~= h1); u9b_chk('h_isequal', @() isequal(h1, Hnd116(1))); u9b_chk('h_isequal_same', @() isequal(h1, h1)); u9b_chk('h_isequal_arr', @() isequal(ha, [h1 h2 h3])); u9b_chk('h_isequal_arr_other', @() isequal(ha, [Hnd116(1) Hnd116(2) Hnd116(3)]));
u9b_chk('h_eq_arr_arr', @() ha == ha); u9b_chk('h_eq_mismatch', @() ha == ha(1:2)); u9b_chk('h_lt', @() h1 < h2); u9b_chk('h_eq_num', @() h1 == 1); u9b_chk('h_eq_val', @() h1 == s1);
u9b_chk('h_2d', @() size([h1 h2; h3 h1])); u9b_chk('h_2d_same', @() h2d(h1)); u9b_chk('h_assign_grow', @() hgrow(ha)); u9b_chk('h_assign_grow_valid', @() hgrowvalid(ha)); u9b_chk('h_delete_elem', @() hdel(ha)); u9b_chk('h_delete_elem_valid', @() hdelvalid(ha, h2));
u9b_chk('h_isvalid', @() isvalid(ha)); u9b_chk('h_delete_obj', @() hdelobj()); u9b_chk('h_delete_obj_cs', @() hdelobjcs()); u9b_chkdiv('h_delete_obj_display', @() oi_try( @() hdelobjdisp()), 'ADR0232'); u9b_chk('h_delete_obj_class', @() hdelobjclass()); u9b_chk('h_delete_obj_prop', @() hdelobjprop());
u9b_chk('h_cs_deal', @() hdeal(ha)); u9b_chk('h_arrayfun', @() arrayfun(@(o) o.p, ha)); u9b_chk('h_for', @() forrow(ha)); u9b_chk('h_for_write', @() hforwrite(ha)); u9b_chk('h_find', @() hfind(ha));
u9b_chk('h_cat_val', @() [h1 s1]); u9b_chk('h_cat_sub', @() class([Val116(1) Sub116(2)])); u9b_chk('h_cat_sub_rev', @() class([Sub116(2) Val116(1)])); u9b_chk('h_cat_num', @() [h1 1]); u9b_chk('h_cat_empty', @() size([h1 []])); u9b_chk('h_cat_cell', @() [h1 {1}]);
u9b_chk('h_empty', @() size(Hnd116.empty)); u9b_chk('h_empty_class', @() class(Hnd116.empty)); u9b_chk('h_empty_isvalid', @() size(isvalid(Hnd116.empty)));
u9b_chk('h_method_arr', @() hbumpall(ha)); u9b_chk('h_method_arr_after', @() [ha.p]); u9b_chk('h_method_dot', @() hbumpdot(ha));
u9b_chk('h_gap_fill', @() hgap()); u9b_chk('h_gap_fill_class', @() hgapclass()); u9b_chk('h_gap_fill_valid', @() hgapvalid()); u9b_chk('h_gap_fill_same', @() hgapsame()); u9b_chk('h_gap_fill_p', @() hgapp()); u9b_chk('h_gap_fill_dot', @() hgapdot()); u9b_chk('h_gap_ctor_count', @() hgapctor());
u9b_chk('h_repmat', @() size(repmat(h1, 1, 3))); u9b_chk('h_repmat_same', @() hrepmatsame(h1)); u9b_chk('h_repmat_write', @() hrepmatwrite(h1));
u9b_chk('h_unique', @() size(unique(ha))); u9b_chk('h_unique_dup', @() size(unique([h1 h1 h2]))); u9b_chk('h_ismember', @() ismember(h2, ha)); u9b_chk('h_sort', @() sort(ha)); u9b_chk('h_setdiff', @() size(setdiff(ha, h1)));
u9b_chk('h_numel', @() numel(ha)); u9b_chk('h_colon', @() size(ha(:))); u9b_chk('h_end', @() ha(end).p); u9b_chk('h_logical', @() [ha([true false true]).p]); u9b_chk('h_reshape', @() size(reshape(ha, 3, 1))); u9b_chk('h_transpose', @() size(ha'));
u9b_chk('h_cs_assign', @() hcsassign()); u9b_chk('h_props_arr', @() properties(ha)); u9b_chk('h_methods_arr', @() methods(ha)); u9b_chk('h_findobj', @() class(findobj(ha, 'p', 2))); u9b_chk('h_findobj_size', @() size(findobj(ha, 'p', 2))); u9b_chk('h_set_get', @() hsetget(ha)); u9b_chkdiv('h_set_get_arr', @() oi_try( @() hsetgetarr(ha)), 'ADR0232'); u9b_chk('h_get_struct', @() class(get(h1)));
u9b_chk('h_in_cell', @() class({ha})); u9b_chk('h_struct', @() size(struct(ha))); u9b_chkdiv('h_addlistener_arr', @() oi_try( @() haddl(ha)), 'ADR0232'); u9b_chk('h_isequal_after_write', @() hisequalwrite());
u9b_chk('h_assign_val_into', @() hassignval(ha)); u9b_chk('h_assign_other_handle', @() hassignother(ha)); u9b_chk('h_assign_empty', @() hassignempty(ha)); u9b_chk('h_assign_num', @() hassignnum(ha));
u9b_chk('h_cs_dot_assign', @() hdotassign(ha)); u9b_chk('h_colon_dot_deal', @() hcolondeal(ha));
u9b_chk('h_arr_elem_dot_write_shared', @() hshared(ha));
u9b_chk('h_arr_in_prop_of_val', @() harrinval(ha));
u9b_chk('h_isa', @() isa(ha, 'handle')); u9b_chk('h_isobject', @() isobject(ha)); u9b_chk('h_ishandle', @() ishandle(ha)); u9b_chkdiv('h_isgraphics', @() oi_try( @() isgraphics(ha)), 'ADR0232'); u9b_chk('h_class_handle_arr', @() class(ha(1:2)));
u9b_chk('h_eq_deleted', @() heqdeleted()); u9b_chk('h_cs_deleted', @() hcsdeleted()); u9b_chk('h_isvalid_shape', @() size(isvalid([h1 h2; h3 h1])));
u9b_chk('h_arrayfun_delete', @() harrayfundel());
u9b_chkdiv('h_cs_in_fn', @() oi_try( @() hfn(ha)), 'ADR0232');
u9b_chk('h_numel_after_delete_elem', @() hnumel(ha));
u9b_chk('h_handle_prop_cs', @() hpropcs());
u9b_chk('h_delete_all', @() hdelall(ha)); u9b_chk('h_cs_after_delete_all', @() [ha.p]); u9b_chk('h_isvalid_after_delete_all', @() isvalid(ha)); u9b_chk('h_class_after_delete_all', @() class(ha)); u9b_chk('h_size_after_delete_all', @() size(ha)); u9b_chk('h_eq_after_delete_all', @() ha == ha); u9b_chk('h_methods_after_delete_all', @() numel(methods(ha))); u9b_chk('h_cat_after_delete_all', @() size([ha Hnd116(9)])); u9b_chk('h_cat_after_delete_all_valid', @() isvalid([ha Hnd116(9)]));
u9b_chkdiv('h_findobj_graphics', @() oi_try( @() class(findobj(groot))), 'ADR0232');
u9b_chk('v_holds_h', @() vholdsh()); u9b_chk('h_holds_v', @() hholdsv());
u9b_chkdiv('ctor_count_final', @() oi_try( @() VAL116_CTOR), 'ADR0232');


global VAL116_CTOR
VAL116_CTOR = 0;
s1 = Val116(1); s2 = Val116(2); s3 = Val116(3);
a = [s1 s2 s3];
b = [s1 s2; s3 s1];
h1 = Hnd116(1); h2 = Hnd116(2); h3 = Hnd116(3);
ha = [h1 h2 h3];
fprintf('CHK|DISP_ARR|%s|exact\n', strrep(strrep(regexprep(evalc('disp(a)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_ARR|%s|exact\n', strrep(strrep(regexprep(evalc('a'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_2D|%s|exact\n', strrep(strrep(regexprep(evalc('b'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_EMPTY|%s|exact\n', strrep(strrep(regexprep(evalc('e = Val116.empty'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_EMPTY_03|%s|exact\n', strrep(strrep(regexprep(evalc('e3 = Val116.empty(0, 3)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_SCALAR|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('s1'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISP_SCALAR|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('disp(s1)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_COL|%s|exact\n', strrep(strrep(regexprep(evalc('ac = a'''), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_3D|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('t3 = cat(3, a, a)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_SUB_ARR|%s|exact\n', strrep(strrep(regexprep(evalc('sa = [Sub116(1) Sub116(2)]'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_OBJ_PROP|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('nn = Val116(a)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_H_ARR|%s|exact\n', strrep(strrep(regexprep(evalc('ha'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_H_SCALAR|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('h1'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_H_2D|%s|exact\n', strrep(strrep(regexprep(evalc('hb = [h1 h2; h3 h1]'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_H_EMPTY|%s|exact\n', strrep(strrep(regexprep(evalc('he = Hnd116.empty'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_H_DELETED_SCALAR|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('hd = Hnd116(1); delete(hd); hd'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_H_DELETED_ARR|%s|exact\n', strrep(strrep(regexprep(evalc('hd2 = [Hnd116(1) Hnd116(2)]; delete(hd2(1)); hd2'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISP_H_DELETED|%s|exact\n', strrep(strrep(regexprep(evalc('hd3 = Hnd116(1); delete(hd3); disp(hd3)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_ARR_PROPS_OBJ|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('po = Val116(1); po.name = [Val116(2) Val116(3)]; po'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_EMPTY_H_03|%s|exact\n', strrep(strrep(regexprep(evalc('eh = Hnd116.empty(0, 3)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_EMPTY_SUB|%s|exact\n', strrep(strrep(regexprep(evalc('es = Sub116.empty'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_1x1_FROM_ARR|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('one = a(2)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_NOARG|%s|div=ADR0232\n', strrep(strrep(regexprep(evalc('na = NoArg116(5)'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
fprintf('CHK|DISPLAY_ZERO|%s|exact\n', strrep(strrep(regexprep(evalc('za = [Zero116 Zero116]'), '^\n|<a [^>]*>|</a>', ''), newline, '\n'), '|', '/'));
u9b_chk('mixed_sub_val_class', @() class([Val116(1) Sub116(2)]));
u9b_chk('mixed_sub_val_elem_classes', @() mixedclasses());
u9b_chk('mixed_sub_val_ps', @() mixedps());
u9b_chk('mixed_val_sub_class', @() class([Sub116(2) Val116(1)]));
u9b_chk('mixed_val_sub_elem_classes', @() mixedrevclasses());
u9b_chk('mixed_val_sub_ps', @() mixedrevps());
u9b_chk('mixed_val_sub_q', @() mixedrevq());
u9b_chk('cat_val_num', @() [s1 5]); u9b_chk('cat_val_num_class', @() class([s1 5])); u9b_chk('cat_val_num_p', @() ps([s1 5]));
u9b_chk('cat_num_val', @() [5 s1]); u9b_chk('cat_num_val_class', @() class([5 s1]));
u9b_chk('cat_val_char', @() ps([s1 'q'])); u9b_chk('cat_val_str', @() class([s1 "q"])); u9b_chk('cat_val_cell', @() class([s1 {1}])); u9b_chkdiv('cat_val_struct', @() oi_try( @() class([s1 struct('p', 1)])), 'ADR0232');
u9b_chk('cat_val_handle', @() class([s1 h1])); u9b_chk('cat_val_handle_p', @() catvh(s1, h1));
u9b_chk('cat_handle_val', @() class([h1 s1])); u9b_chk('cat_handle_val_p', @() cathv(h1, s1));
u9b_chk('cat_zero_num', @() [Zero116 1]);
u9b_chk('cat_zero_val', @() [Zero116 s1]);
u9b_chk('cat_noarg_num', @() ps([NoArg116(5) 1]));
u9b_chk('cat_noarg_cell', @() class([NoArg116(5) {1}]));
u9b_chk('cat_val_logical', @() ps([s1 true]));
u9b_chk('cat_val_empty_cell', @() size([s1 {}]));
u9b_chk('cat_val_empty_str', @() size([s1 '']));
u9b_chkdiv('cat_val_empty_string', @() oi_try( @() size([s1 string.empty])), 'ADR0232');
u9b_chkdiv('cat_val_empty_struct', @() oi_try( @() size([s1 struct([])])), 'ADR0232');
u9b_chk('cat_sub_num', @() class([Sub116(1) 5])); u9b_chk('cat_sub_num_q', @() qs([Sub116(1) 5]));
u9b_chk('cat_val_sub_num', @() class([s1 Sub116(1) 5]));
u9b_chk('cat_vert_val_sub', @() class([s1; Sub116(1)]));
u9b_chk('assign_num_p', @() assignnump(a)); u9b_chk('assign_struct_p', @() assignstructp(a)); u9b_chk('assign_cell_p', @() assigncellp(a));
u9b_chk('assign_zero_num', @() assignzero());
u9b_chk('assign_sub_into_val_q', @() subintovalq(a));
u9b_chk('assign_val_into_sub_q', @() valintosubq());
u9b_chk('assign_val_into_sub_class_elem', @() valintosubclass());
u9b_chk('assign_handle_into_val', @() handleintoval(a));
u9b_chk('assign_val_into_handle', @() valintohandle());
u9b_chk('assign_num_into_handle', @() numintohandle());
u9b_chkdiv('h_max', @() oi_try( @() max(ha)), 'ADR0232'); u9b_chkdiv('h_min', @() oi_try( @() min(ha)), 'ADR0232');
u9b_chk('h_eq_num_arr', @() ha == 1); u9b_chk('h_eq_cell', @() h1 == {h1});
u9b_chkdiv('h_sum', @() oi_try( @() sum(ha)), 'ADR0232');
u9b_chk('h_cs_find_p2', @() find([ha.p] == 2));
u9b_chk('cs_first_only', @() firstonly(a)); u9b_chk('cs_first_only_name', @() firstonlyname(a));
u9b_chk('cs_paren_single', @() parensingle(a));
u9b_chk('cs_anon_return', @() feval(@() a.p));
u9b_chk('cs_in_if', @() ifcs(a));
u9b_chk('cs_while', @() whilecs(a));
u9b_chk('cs_switch', @() switchcs(a));
u9b_chk('cs_and', @() andcs(a));
u9b_chk('cs_unary', @() -a.p);
u9b_chk('cs_binary', @() a.p + 1);
u9b_chk('cs_binary_rev', @() 1 + a.p);
u9b_chk('cs_transpose', @() a.p');
u9b_chk('cs_power', @() a.p .^ 2);
u9b_chk('cs_range', @() 1:a.p);
u9b_chk('cs_paren_index', @() a.p(1));
u9b_chk('cs_brace_after', @() bracecs(a));
u9b_chk('cs_field_after', @() a.name.x);
u9b_chk('cs_call_after', @() a.name(1));
u9b_chkdiv('cs_sub2_dot_end', @() oi_try( @() a(1:2).p(end)), 'ADR0232');
u9b_chk('cs_one_elem_dot_index', @() a(2).p(end));
u9b_chk('cs_colon_dot_index', @() a(:).p);
u9b_chk('cs_empty_dot_value', @() emptydot(a));
u9b_chk('cs_empty_dot_bracket', @() size([a([]).p]));
u9b_chk('cs_struct_arg_two', @() struct('p', {a.p}, 'q', {4, 5, 6}));
u9b_chk('cs_deal_one_to_many', @() dealonemany(a));
u9b_chk('cs_deal_cell_spread', @() dealcellspread(a));
u9b_chk('cs_deal_mismatch_names', @() dealmismatch(a));
u9b_chk('cs_assign_from_fn_multi', @() assignfnmulti(a));
u9b_chk('cs_assign_from_cs_other_arr', @() assigncsother(a));
u9b_chk('cs_assign_sub_range_from_cs', @() assignsubrange(a));
u9b_chk('cs_assign_shape', @() assignshape(b));
u9b_chk('cs_assign_shape_vals', @() assignshapevals(b));
u9b_chk('cs_2d_bracket', @() [b.p]);
u9b_chk('cs_2d_brace_size', @() size({b.p}));
u9b_chk('cs_2d_bracket_size', @() size([b.p]));
u9b_chk('cs_2d_sub', @() [b(2, :).p]);
u9b_chkdiv('cs_vert_bracket', @() oi_try( @() vertcat(a.name)), 'ADR0232');
u9b_chk('cs_vert_bracket_literal', @() [a.name; 'yyy']);
u9b_chk('cs_char_cat_size', @() size([a.name]));
u9b_chk('cs_name_strcat', @() strcat(a.name));
u9b_chk('cs_cellfun_over_brace', @() cellfun(@numel, {a.name}));
u9b_chk('cs_nargin', @() nargin_count(a.p));
u9b_chk('cs_numel_fn', @() numel(a.p));
u9b_chk('cs_size_fn', @() size(a.p));
u9b_chk('cs_isempty_fn', @() isempty(a.p));
u9b_chkdiv('cs_class_fn', @() oi_try( @() class(a.p)), 'ADR0232');
u9b_chk('cs_fprintf', @() sprintf('%d-', a.p));
u9b_chk('cs_ismember', @() ismember(2, [a.p]));
u9b_chk('cs_any_eq', @() any([a.p] == 2));
u9b_chk('cs_logical_mask', @() a([a.p] > 1));
u9b_chk('cs_logical_mask_p', @() [a([a.p] > 1).p]);
u9b_chk('cs_sort_by', @() sortby(a));
u9b_chk('cs_handle_mask', @() [ha([ha.p] >= 2).p]);
u9b_chk('cs_handle_mask_write', @() hmaskwrite(ha));
u9b_chk('cs_method_each', @() arrayfun(@(o) o.total(), a));
u9b_chk('cs_name_write_grow', @() namewritegrow(a));
u9b_chk('cs_name_write_shrink', @() namewriteshrink(a));
u9b_chk('cs_nested_arr_cs', @() nestedarrcs());
u9b_chk('cs_handle_in_val_cs', @() handleinvalcs());
u9b_chk('cs_struct_in_obj_cs', @() structinobjcs());
u9b_chk('cs_cell_in_obj_brace', @() cellinobjbrace());
u9b_chk('cs_end_in_dot_index', @() endindot(a));
u9b_chk('cs_end_in_arr_index', @() a(end).p);
u9b_chk('cs_end_arith', @() a(end - 1).p);
u9b_chk('cs_end_in_bracket', @() [a(2:end).p]);
u9b_chk('cs_end_in_brace', @() {a(1:end - 1).p});
u9b_chkdiv('ctor_count_final_2', @() oi_try( @() VAL116_CTOR), 'ADR0232');



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
    if isa(v, 'Hnd116') && ~all(isvalid(v(:)))
        t = sprintf('%s valid=%s', class(v), mat2str(isvalid(v)));
    else
        t = sprintf('%s p=%s', class(v), mat2str(arrayfun(@(o) o.p, v)));
    end
else
    t = class(v);
end
end

function out = singleout(a), x = a.p; out = x; end

function out = twoout(a), [x, y] = a.p; out = [x y]; end

function out = threeout(a), [x, y, z] = a.p; out = [x y z]; end

function out = fourout(a), [x, y, z, w] = a.p; out = [x y z w]; end

function out = dealinto(a), [a.p] = deal(7); out = [a.p]; end

function out = dealeach(a), [a.p] = deal(4, 5, 6); out = [a.p]; end

function out = dealwrong(a), [a.p] = deal(4, 5); out = [a.p]; end

function out = assignlist(a), [a(1:2).p] = deal(8, 9); out = [a.p]; end

function out = nested(a), c = {a.p}; out = [c{:}]; end

function out = dotassign(a), a.p = 5; out = [a.p]; end

function out = dotassignall(a), a(:).p = 1; out = [a.p]; end

function out = dotassignallcs(a), [a(:).p] = deal(1); out = [a.p]; end

function out = arrindex(a), out = a(2:3).p; end

function out = arrindexwrite(a), a(2).name(1) = 'Z'; out = a(2).name; end

function out = fieldgrow(a), a(2).name(5) = 'Z'; out = double(a(2).name); end

function out = structfield(a), st.list = a; out = [st.list.p]; end

function out = cellcs(a), c = {a}; out = [c{1}.p]; end

function out = cswrite(a), c = cell(1, 3); [c{:}] = a.p; out = c; end

function out = dotdot(), o = Val116(1); o.name = Val116(5); out = o.name.p; end

function out = assign2d(b), b(1, 2) = Val116(9); out = [b.p]; end

function out = assigngrow2d(b), b(3, 3) = Val116(9); out = size(b); end

function out = assigngrow2dvals(b), b(3, 3) = Val116(9); out = [b.p]; end

function out = delrow(b), b(1, :) = []; out = size(b); end

function out = dellin(b), b(2) = []; out = size(b); end

function out = delcol(b), b(:, 1) = []; out = size(b); end

function out = delall(b), b(:) = []; out = sprintf('%s %s', class(b), mat2str(size(b))); end

function out = transposevals(b), t = b'; out = [t.p]; end

function out = threed(b), t = cat(3, b, b); out = t(1, 2, 2).p; end

function out = threedcs(b), t = cat(3, b, b); out = [t(:, :, 2).p]; end

function out = linassign2d(b), b(3) = Val116(7); out = [b.p]; end

function out = gapfill(a), a(5) = Val116(5); out = [a.p]; end

function out = gapclass(a), a(5) = Val116(5); out = class(a(4)); end

function out = gapsub(), a = Sub116(1); a(3) = Sub116(3); out = sprintf('%s %s %s', class(a), class(a(2)), mat2str([a.q])); end

function out = gapctor(), global VAL116_CTOR; VAL116_CTOR = 0; a = Val116(1); a(4) = Val116(4); out = VAL116_CTOR; end

function out = gapctor2d(), global VAL116_CTOR; VAL116_CTOR = 0; a = Val116(1); a(3, 3) = Val116(4); out = VAL116_CTOR; end

function out = gap2d(a), a(2, 2) = Val116(9); out = [size(a) a(2, 1).p a(1, 3).p a(2, 3).p]; end

function out = gapempty(), x(3) = Val116(3); out = sprintf('%s %s', class(x), mat2str([x.p])); end

function out = gapnoargs(), a = NoArg116(5); a(3) = NoArg116(1); out = [a.p]; end

function out = gapdot(a), a(5).p = 9; out = [a.p]; end

function out = gapdotvals(a), a(5).name = 'q'; out = {a.name}; end

function out = gapdefault(), a = Val116(1); a(3) = Val116(3); out = sprintf('%d %s', a(2).p, a(2).name); end

function out = gapmixed(), a = Val116(1); a(3) = Sub116(3); out = sprintf('%s %s %s', class(a), class(a(2)), class(a(3))); end

function out = gapfromstatic(), a = Val116.empty; a(2) = Val116(2); out = sprintf('%s %s', mat2str(size(a)), mat2str([a.p])); end

function out = gapfromzero(), a = Val116.empty(0, 0); a(2, 2) = Val116(2); out = sprintf('%s %s', mat2str(size(a)), mat2str([a.p])); end

function out = ifobj(s), if s, out = 'truthy'; else, out = 'falsy'; end, end

function out = switchobj(s), switch s, case 1, out = 'one'; otherwise, out = 'other'; end, end

function out = twoouts(a), [x, y] = arrayfun(@(o) deal(o.p, o.p * 2), a); out = [x y]; end

function out = iff(k), if k == 1, out = Val116(1); else, out = Sub116(2); end, end

function out = num2cellel(a), n = num2cell(a); out = n{1}; end

function out = forrow(a), out = []; for o = a, out(end + 1) = numel(o); end, end

function out = for2d(b), out = []; for o = b, out(end + 1) = numel(o); end, end

function out = forvals(a), out = []; for o = a, out(end + 1) = o.p; end, end

function out = whilearr(a), n = 0; while numel(a) > 1, a(end) = []; n = n + 1; end, out = n; end

function out = mixed(), a = [Val116(1) Sub116(2)]; out = sprintf('%s %s', class(a), mat2str([a.p])); end

function out = mixedrev(), a = [Sub116(2) Val116(1)]; out = sprintf('%s %s', class(a), mat2str([a.p])); end

function out = mixedcs(), a = [Val116(1) Sub116(2)]; out = [a.q]; end

function out = subintoval(a), a(2) = Sub116(5); out = sprintf('%s %s', class(a), class(a(2))); end

function out = valintosub(), s = [Sub116(1) Sub116(2)]; s(1) = Val116(3); out = sprintf('%s %s', class(s), class(s(1))); end

function out = wrongclass(a), a(2) = Hnd116(1); out = class(a); end

function out = assignstruct(a), a(2) = struct('p', 1, 'name', 'x'); out = class(a); end

function out = assignnum(a), a(2) = 5; out = class(a); end

function out = assigncell(a), a(2) = {Val116(5)}; out = class(a); end

function out = assignemptycell(a), a(2) = {}; out = size(a); end

function out = assignemptystr(a), a(2) = ''; out = size(a); end

function out = rangeassign(a), a(1:2) = Val116(7); out = [a.p]; end

function out = rangeassignarr(a), a(1:2) = [Val116(7) Val116(8)]; out = [a.p]; end

function out = rangeassignbad(a), a(1:2) = [Val116(7) Val116(8) Val116(9)]; out = [a.p]; end

function out = maskwrite(a), a([true false true]) = Val116(0); out = [a.p]; end

function out = colonassign(a), a(:) = Val116(0); out = [a.p]; end

function out = colonassignarr(a), a(:) = [Val116(7) Val116(8) Val116(9)]; out = [a.p]; end

function out = endplus(a), a(end + 1) = Val116(4); out = [a.p]; end

function out = wholefromscalar(a), a = Val116(9); out = sprintf('%s %s', class(a), mat2str(size(a))); end

function out = zeroindex(a), a(0) = Val116(9); out = [a.p]; end

function out = twoidxrow(a), a(1, 2) = Val116(9); out = size(a); end

function out = twoidxrowvals(a), a(1, 2) = Val116(9); out = [a.p]; end

function out = sharedwrite(a), c = a; c(1).p = 99; out = [a(1).p c(1).p]; end

function out = nestedobj(), o = Val116(1); o.name = [Val116(2) Val116(3)]; out = [o.name.p]; end

function out = nestedwrite(), o = Val116(1); o.name = [Val116(2) Val116(3)]; o.name(2).p = 9; out = [o.name.p]; end

function out = dealstruct(), a = [Val116(1) Val116(2)]; [a.name] = deal('u', 'v'); out = {a.name}; end

function out = dealname(a), [a.name] = deal('z'); out = {a.name}; end

function out = csfromcs(a), b = a; [b.p] = a.p; out = [b.p]; end

function out = csfromvec(a), v = [7 8 9]; [a.p] = v; out = [a.p]; end

function out = csfromcell(a), c = {7, 8, 9}; [a.p] = c{:}; out = [a.p]; end

function out = cslogical(a), [a([true false true]).p] = deal(0); out = [a.p]; end

function out = csend(a), [a(end).p] = deal(0); out = [a.p]; end

function out = csone(a), [a(2).p] = deal(0); out = [a.p]; end

function out = csgrow(a), [a(4:5).p] = deal(0); out = [a.p]; end

function out = cs2d(b), [b(1, :).p] = deal(0); out = [b.p]; end

function out = csnested(a), [a.name] = deal('qq'); [a(1:2).name] = deal('r'); out = {a.name}; end

function out = csmethod(a), [a.p] = deal(a(1).total()); out = [a.p]; end

function out = structtoobj(), st = struct('p', {1, 2}); out = class([Val116(st(1).p) Val116(st(2).p)]); end

function out = structcs(), st = struct('o', {Val116(1), Val116(2)}); out = [st.o]; end

function out = csindex(a), v = 10:20; out = v(a(1:2).p); end

function out = hwrite(ha, h2), ha(2).p = 20; out = h2.p; end

function out = helem(ha, h1), x = ha(1); x.p = 30; out = h1.p; end

function out = hcopy(ha, h3), hb = ha; hb(3).p = 40; out = [h3.p ha(3).p]; end

function out = h2d(h1), m = [h1 h1; h1 h1]; m(2, 2).p = 55; out = h1.p; end

function out = hgrow(ha), ha(5) = Hnd116(5); out = sprintf('%s %s', class(ha(4)), mat2str(isvalid(ha(4)))); end

function out = hgrowvalid(ha), ha(5) = Hnd116(5); out = ha(4).p; end

function out = hdel(ha), ha(2) = []; out = [ha.p]; end

function out = hdelvalid(ha, h2), ha(2) = []; out = isvalid(h2); end

function out = hdelobj(), h = Hnd116(1); hh = [h Hnd116(2)]; delete(h); out = isvalid(hh); end

function out = hdelobjcs(), h = Hnd116(1); hh = [h Hnd116(2)]; delete(h); out = [hh.p]; end

function out = hdelobjdisp(), h = Hnd116(1); hh = [h Hnd116(2)]; delete(h); out = evalc('hh'); end

function out = hdelobjclass(), h = Hnd116(1); delete(h); out = class(h); end

function out = hdelobjprop(), h = Hnd116(1); delete(h); out = h.p; end

function out = hdeal(ha), [ha.p] = deal(0); out = [ha.p]; end

function out = hforwrite(ha), for o = ha, o.p = o.p * 10; end, out = [ha.p]; end

function out = hfind(ha), out = find([ha.p] == 2); end

function out = hbumpall(ha), bump(ha); out = [ha.p]; end

function out = hbumpdot(ha), ha.bump(); out = [ha.p]; end

function out = hgap(), h = Hnd116(1); h(3) = Hnd116(3); out = size(h); end

function out = hgapclass(), h = Hnd116(1); h(3) = Hnd116(3); out = class(h(2)); end

function out = hgapvalid(), h = Hnd116(1); h(3) = Hnd116(3); out = isvalid(h(2)); end

function out = hgapsame(), h = Hnd116(1); h(4) = Hnd116(3); out = h(2) == h(3); end

function out = hgapp(), h = Hnd116(1); h(3) = Hnd116(3); out = h(2).p; end

function out = hgapdot(), h = Hnd116(1); h(3).p = 9; out = [h.p]; end

function out = hgapctor(), h = Hnd116(1); h(4) = Hnd116(4); out = sprintf('%s %s', mat2str(isvalid(h)), mat2str(h(2) == h(3))); end

function out = hrepmatsame(h1), r = repmat(h1, 1, 3); out = r(1) == r(2); end

function out = hrepmatwrite(h1), r = repmat(h1, 1, 3); r(2).p = 77; out = [h1.p r(1).p]; end

function out = hcsassign(), h = [Hnd116(1) Hnd116(2)]; [h.p] = deal(5, 6); out = [h.p]; end

function out = hsetget(ha), set(ha(1), 'p', 1); out = get(ha(1), 'p'); end

function out = hsetgetarr(ha), set(ha, 'p', 1); out = get(ha, 'p'); end

function out = haddl(ha), l = addlistener(ha, 'p', 'PostSet', @(s, e) 1); out = class(l); end

function out = hisequalwrite(), x = Hnd116(1); y = Hnd116(1); x.p = 2; out = isequal(x, y); end

function out = hassignval(ha), ha(2) = Val116(1); out = class(ha); end

function out = hassignother(ha), ha(2) = Hnd116(50); out = [ha.p]; end

function out = hassignempty(ha), ha(2) = []; out = size(ha); end

function out = hassignnum(ha), ha(2) = 5; out = class(ha); end

function out = hdotassign(ha), ha.p = 1; out = [ha.p]; end

function out = hcolondeal(ha), [ha(:).p] = deal(3); out = [ha.p]; end

function out = hshared(ha), hb = ha; hb(1).p = 123; out = [ha(1).p hb(1).p]; end

function out = harrinval(ha), v = Val116(1); v.name = ha; v.name(2).p = 321; out = ha(2).p; end

function out = heqdeleted(), h = Hnd116(1); g = h; delete(h); out = h == g; end

function out = hcsdeleted(), h = [Hnd116(1) Hnd116(2)]; delete(h(1)); out = {h.p}; end

function out = harrayfundel(), h = [Hnd116(1) Hnd116(2)]; arrayfun(@delete, h); out = isvalid(h); end

function out = hfn(ha), out = max(ha.p); end

function out = hnumel(ha), delete(ha(2)); out = numel(ha); end

function out = hpropcs(), h = Hnd116(1); h.name = [Hnd116(2) Hnd116(3)]; out = [h.name.p]; end

function out = hdelall(ha), delete(ha); out = isvalid(ha); end

function out = vholdsh(), v = Val116(1); v.name = Hnd116(5); w = v; w.name.p = 6; out = v.name.p; end

function out = hholdsv(), h = Hnd116(1); h.name = Val116(5); g = h; g.name.p = 6; out = h.name.p; end

function out = subcs(), a = [Sub116(1) Sub116(2)]; out = [a.q]; end

function out = nargin_count(varargin), out = nargin; end

function out = ps(x), out = [x.p]; end

function out = qs(x), out = [x.q]; end

function out = catvh(s1, h1), x = [s1 h1]; out = sprintf('%s %s', class(x(2)), class(x(2).p)); end

function out = cathv(h1, s1), x = [h1 s1]; out = sprintf('%s %s', class(x(2)), class(x(2).p)); end

function out = mixedclasses(), a = [Val116(1) Sub116(2)]; out = sprintf('%s %s', class(a(1)), class(a(2))); end

function out = mixedps(), a = [Val116(1) Sub116(2)]; out = sprintf('%s %s', s(a(1).p), s(a(2).p)); end

function out = mixedrevclasses(), a = [Sub116(2) Val116(1)]; out = sprintf('%s %s', class(a(1)), class(a(2))); end

function out = mixedrevps(), a = [Sub116(2) Val116(1)]; out = sprintf('%s %s', s(a(1).p), s(a(2).p)); end

function out = mixedrevq(), a = [Sub116(2) Val116(1)]; out = sprintf('%s %s', s(a(1).q), s(a(2).q)); end

function out = assignnump(a), a(2) = 5; out = sprintf('%s %s', class(a(2)), s(a(2).p)); end

function out = assignstructp(a), a(2) = struct('p', 1, 'name', 'x'); out = sprintf('%s %s', class(a(2)), s(a(2).p)); end

function out = assigncellp(a), a(2) = {Val116(5)}; out = sprintf('%s %s', class(a(2)), s(a(2).p)); end

function out = assignzero(), z = [Zero116 Zero116]; z(2) = 5; out = class(z); end

function out = subintovalq(a), a(2) = Sub116(5); out = sprintf('%s %s', class(a(2)), s(a(2).p)); end

function out = valintosubq(), s9 = [Sub116(1) Sub116(2)]; s9(1) = Val116(3); out = sprintf('%s %s %s', class(s9(1)), s(s9(1).p), s(s9(1).q)); end

function out = valintosubclass(), s9 = [Sub116(1) Sub116(2)]; s9(1) = Val116(3); out = class(s9); end

function out = handleintoval(a), a(2) = Hnd116(7); out = sprintf('%s %s', class(a(2)), class(a(2).p)); end

function out = valintohandle(), h = [Hnd116(1) Hnd116(2)]; h(2) = Val116(7); out = sprintf('%s %s', class(h(2)), class(h(2).p)); end

function out = numintohandle(), h = [Hnd116(1) Hnd116(2)]; h(2) = 7; out = sprintf('%s %s', class(h(2)), s(h(2).p)); end

function out = hsortorder(), h = [Hnd116(3) Hnd116(1) Hnd116(2)]; sh = sort(h); out = [sh.p]; end

function out = hsorttwice(), h = [Hnd116(3) Hnd116(1) Hnd116(2)]; sh = sort(h); sh2 = sort(fliplr(h)); out = [[sh.p] [sh2.p]]; end

function out = huniqueorder(), h = [Hnd116(3) Hnd116(1) Hnd116(2) Hnd116(1)]; u = unique(h); out = [u.p]; end

function out = hltorder(), h = [Hnd116(3) Hnd116(1)]; out = [h(1) < h(2), h(2) < h(1), h(1) < h(1)]; end

function out = firstonly(a), out = a.p; end

function out = firstonlyname(a), out = a.name; end

function out = parensingle(a), out = (a.p); end

function out = ifcs(a), if a.p, out = 'yes'; else, out = 'no'; end, end

function out = whilecs(a), n = 0; while a.p && n < 1, n = n + 1; end, out = n; end

function out = switchcs(a), switch a.p, case 1, out = 'one'; otherwise, out = 'other'; end, end

function out = andcs(a), out = a.p && true; end

function out = bracecs(a), out = a.name{1}; end

function out = emptydot(a), out = a([]).p; end

function out = dealonemany(a), [a.p] = deal(9); out = [a.p]; end

function out = dealcellspread(a), c = {7, 8, 9}; [a.p] = deal(c{:}); out = [a.p]; end

function out = dealmismatch(a), [a.p, a.name] = deal(1); out = [a.p]; end

function out = assignfnmulti(a), [a(1:2).p] = size(ones(4, 5)); out = [a.p]; end

function out = assigncsother(a), b = [Val116(7) Val116(8) Val116(9)]; [a.p] = b.p; out = [a.p]; end

function out = assignsubrange(a), [a(2:3).p] = a(1:2).p; out = [a.p]; end

function out = assignshape(b), [b.p] = deal(0); out = size(b); end

function out = assignshapevals(b), [b(:, 1).p] = deal(0); out = [b.p]; end

function out = sortby(a), [~, i] = sort([a.p], 'descend'); out = [a(i).p]; end

function out = hmaskwrite(ha), [ha([ha.p] >= 2).p] = deal(0); out = [ha.p]; end

function out = namewritegrow(a), [a.name] = deal('longer'); out = {a.name}; end

function out = namewriteshrink(a), [a.name] = deal(''); out = size([a.name]); end

function out = nestedarrcs(), o = Val116(1); o.name = [Val116(2) Val116(3)]; out = [o.name.p]; end

function out = handleinvalcs(), v = Val116(1); v.name = [Hnd116(2) Hnd116(3)]; out = [v.name.p]; end

function out = structinobjcs(), v = Val116(1); v.name = struct('f', {1, 2}); out = [v.name.f]; end

function out = cellinobjbrace(), v = Val116(1); v.name = {1, 2}; out = [v.name{:}]; end

function out = endindot(a), out = a(end).name(end); end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end

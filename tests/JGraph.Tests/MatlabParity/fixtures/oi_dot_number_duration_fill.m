% Open items 130, 134 and 135 (ADR 0235), in the MATLAB dialect: T.(n) reading and writing a table's
% n-th variable with R2025b's refusals (a timetable's, a struct's and a nested one's too); the text of
% a duration - string, char, cellstr, compose and sprintf - at R2025b's five digits, its units and
% plurals, and the clock formats' truncated fractions; fillmissing over a string array (only
% <missing> is missing) and a cellstr along a dimension with EndValues and its constants and
% refusals, and the ends of every method, 'linear' extrapolating. Lines marked div=ADR0235 are
% where this build keeps its own words or a JGraph extra (ADR 0156).
T = table([1; 2; 3], categorical({'a'; 'b'; 'a'}), {'x'; 'y'; 'z'}, 'VariableNames', {'n', 'kind', 'name'});
u9b_chk('dn_read1', @() T.(1));
u9b_chk('dn_read2', @() T.(2));
u9b_chk('dn_read3', @() T.(3));
u9b_chk('dn_read_int8', @() T.(int8(1)));
u9b_chk('dn_read_single', @() T.(single(3)));
u9b_chk('dn_read_logical', @() T.(true));
u9b_chk('dn_read0', @() T.(0));
u9b_chk('dn_read4', @() T.(4));
u9b_chk('dn_read_frac', @() T.(1.5));
u9b_chk('dn_read_neg', @() T.(-1));
u9b_chk('dn_read_vec', @() T.([1 2]));
u9b_chk('dn_read_empty', @() T.([]));
u9b_chk('dn_read_nan', @() T.(NaN));
u9b_chk('dn_read_str', @() T.("n"));
u9b_chk('dn_read_cell', @() T.({'n'}));
u9b_chk('dn_read_index', @() T.(1)(2));
u9b_chk('dn_read_brace', @() T.(3){2});
u9b_chk('dn_write1', @() dnw(T, 1, [7; 8; 9]));
u9b_chk('dn_write3', @() dnw(T, 3, {'p'; 'q'; 'r'}));
u9b_chk('dn_write_new', @() dnw(T, 4, [4; 5; 6]));
u9b_chk('dn_write_new5', @() dnw(T, 5, [4; 5; 6]));
u9b_chk('dn_write0', @() dnw(T, 0, [4; 5; 6]));
u9b_chk('dn_write_frac', @() dnw(T, 1.5, [4; 5; 6]));
u9b_chk('dn_write_rows', @() dnw(T, 1, [4; 5]));
u9b_chk('dn_write_scalar', @() dnw(T, 1, 5));
u9b_chk('dn_write_elem', @() dnwe(T));
u9b_chk('dn_write_delete', @() dnwdel(T));
u9b_chk('dn_tt_read', @() ttread());
u9b_chk('dn_tt_write', @() ttwrite());
u9b_chk('dn_tt_read0', @() ttread0());
u9b_chk('dn_struct', @() stdot());
u9b_chk('dn_nested', @() nestdot());
T = table([NaN; 2; 3], [1; NaN; NaN]);
u9b_chk('du_s1', @() string(seconds(1.4142135624)));
u9b_chk('du_c1', @() char(seconds(1.4142135624)));
u9b_chk('du_cellstr', @() cellstr(seconds([1.4142135624 2])));
u9b_chk('du_min', @() string(minutes(1.23456789)));
u9b_chk('du_hr', @() string(hours(2.5)));
u9b_chk('du_hr2', @() string(hours(2/3)));
u9b_chk('du_day', @() string(days(1/3)));
u9b_chk('du_yr', @() string(years(1.23456)));
u9b_chk('du_small', @() string(seconds(1e-5)));
u9b_chk('du_tiny', @() string(seconds(1.23456e-9)));
u9b_chk('du_big', @() string(seconds(123456.789)));
u9b_chk('du_huge', @() string(seconds(1.23456789e12)));
u9b_chk('du_neg', @() string(seconds(-1.4142135624)));
u9b_chk('du_specials', @() string(seconds([1.5 NaN Inf -Inf])));
u9b_chk('du_int', @() string(seconds(3)));
u9b_chk('du_round5', @() string(seconds(2.00005)));
u9b_chk('du_round4', @() string(seconds(99999.5)));
u9b_chk('du_hms', @() string(duration(1, 2, 3.456)));
u9b_chk('du_hms_sss', @() string(duration(1, 2, 3.456, 'Format', 'hh:mm:ss.SSS')));
u9b_chk('du_dhms', @() string(duration(26, 2, 3, 'Format', 'dd:hh:mm:ss')));
u9b_chk('du_fmt_s', @() string(duration(0, 0, 1.4142135624, 'Format', 's')));
u9b_chk('du_fmt_m', @() string(duration(0, 1, 30, 'Format', 'm')));
u9b_chk('du_fmt_h', @() string(duration(1, 20, 0, 'Format', 'h')));
u9b_chk('du_fmt_d', @() string(duration(30, 0, 0, 'Format', 'd')));
u9b_chk('du_fmt_y', @() string(duration(9000, 0, 0, 'Format', 'y')));
u9b_chk('du_matrix', @() string(seconds([1.11111 22.2222; 333.333 4444.44])));
u9b_chk('du_compose', @() compose("%s", seconds(1.4142135624)));
u9b_chk('du_num2str', @() msgof(@() num2str(seconds(1.4142135624))));
u9b_chk('du_sprintf', @() sprintf('%s', seconds(1.4142135624)));
u9b_chk('du_cat_char', @() ['t=' char(minutes(1.23456789))]);
u9b_chk('du_cal', @() string(calmonths(2)));
u9b_chk('dg_99999_4', @() string(seconds(99999.4)));
u9b_chk('dg_99999_5', @() string(seconds(99999.5)));
u9b_chk('dg_1e5', @() string(seconds(1e5)));
u9b_chk('dg_123456', @() string(seconds(123456)));
u9b_chk('dg_9_99995', @() string(seconds(9.99995)));
u9b_chk('dg_9_99994', @() string(seconds(9.99994)));
u9b_chk('dg_1e4', @() string(seconds(1e-4)));
u9b_chk('dg_9e5', @() string(seconds(0.000099999)));
u9b_chk('dg_0_00012345678', @() string(seconds(0.00012345678)));
u9b_chk('dg_12345_6', @() string(seconds(12345.6)));
u9b_chk('dg_0', @() string(seconds(0)));
u9b_chk('dg_negzero', @() string(seconds(-0)));
u9b_chk('dg_half', @() string(seconds(0.5)));
u9b_chk('dg_1_23455', @() string(seconds(1.23455)));
u9b_chk('dg_2_5e_5', @() string(seconds(2.5e-5)));
u9b_chk('un_day1', @() string(days(1)));
u9b_chk('un_day2', @() string(days(2)));
u9b_chk('un_daym1', @() string(days(-1)));
u9b_chk('un_day0', @() string(days(0)));
u9b_chk('un_day_half', @() string(days(0.5)));
u9b_chk('un_day_1_5', @() string(days(1.5)));
u9b_chk('un_yr1', @() string(years(1)));
u9b_chk('un_yr2', @() string(years(2)));
u9b_chk('un_yr0', @() string(years(0)));
u9b_chk('un_hr1', @() string(hours(1)));
u9b_chk('un_hr2', @() string(hours(2)));
u9b_chk('un_min2', @() string(minutes(2)));
u9b_chk('un_sec1', @() string(seconds(1)));
u9b_chk('un_nan', @() string(seconds(NaN)));
u9b_chk('un_nan_char', @() char(seconds(NaN)));
u9b_chk('un_inf_hms', @() string(duration(Inf, 0, 0)));
u9b_chk('un_nan_hms', @() char(duration(NaN, 0, 0)));
u9b_chk('un_cellstr', @() cellstr(seconds([1.4142135624 NaN])));
u9b_chk('hm_round', @() string(duration(1, 2, 3.7)));
u9b_chk('hm_round59', @() string(duration(1, 2, 59.7)));
u9b_chk('hm_neg', @() string(duration(-1, -2, -3.7)));
u9b_chk('hm_big', @() string(duration(123, 2, 3)));
u9b_chk('hm_sss', @() string(duration(1, 2, 3.4567, 'Format', 'hh:mm:ss.SSS')));
u9b_chk('hm_ss', @() string(duration(1, 2, 3.4567, 'Format', 'hh:mm:ss.SS')));
u9b_chk('hm_ssss', @() string(duration(1, 2, 3.4567, 'Format', 'hh:mm:ss.SSSS')));
u9b_chk('hm_mmss', @() string(duration(1, 2, 3.4567, 'Format', 'mm:ss')));
u9b_chk('hm_mmss_s', @() string(duration(0, 2, 3.4567, 'Format', 'mm:ss.S')));
u9b_chk('hm_hhmm', @() string(duration(1, 2, 33, 'Format', 'hh:mm')));
u9b_chk('hm_dd', @() string(duration(49, 2, 3.4567, 'Format', 'dd:hh:mm:ss')));
u9b_chk('hm_dd_sss', @() string(duration(49, 2, 3.4567, 'Format', 'dd:hh:mm:ss.SSS')));
u9b_chk('fs_prev', @() fillmissing(["a" missing "c"], 'previous'));
u9b_chk('fs_next', @() fillmissing([missing "b" missing], 'next'));
u9b_chk('fs_near', @() fillmissing(["a" missing "c"], 'nearest'));
u9b_chk('fs_near2', @() fillmissing(["a" missing missing "d"], 'nearest'));
u9b_chk('fs_const', @() fillmissing(["a" missing ""], 'constant', "z"));
u9b_chk('fs_const_char', @() msgof(@() fillmissing(["a" missing], 'constant', 'z')));
u9b_chk('fs_const_vec', @() fillmissing(["a" missing; missing "b"], 'constant', ["y" "z"]));
u9b_chk('fs_tf', @() fstf(["a" missing "" "c"]));
u9b_chk('fs_col', @() fillmissing(["a"; missing; "c"], 'previous'));
u9b_chk('fs_mat', @() fillmissing(["a" missing; missing "d"], 'previous'));
u9b_chk('fs_mat2', @() fillmissing(["a" missing; missing "d"], 'previous', 2));
u9b_chk('fs_ends', @() fillmissing([missing "b" missing], 'previous'));
u9b_chk('fs_ends_extrap', @() fillmissing([missing "b" missing], 'previous', 'EndValues', 'nearest'));
u9b_chk('fs_endvalues_none', @() fillmissing([missing "b" missing], 'nearest', 'EndValues', 'none'));
u9b_chk('fs_rmmissing', @() rmmissing(["a" "" missing]));
u9b_chk('fs_std', @() standardizeMissing(["a" "" "x"], "x"));
u9b_chk('fs_cell_const', @() fillmissing({'a', ''}, 'constant', 'z'));
u9b_chk('fs_cell_const_str', @() fillmissing({'a', ''}, 'constant', "z"));
u9b_chk('fs_char', @() msgof(@() fillmissing('a b', 'previous')));
u9b_chk('fs_cell_mixed', @() msgof(@() fillmissing({'a', 1}, 'previous')));
u9b_chk('fs_table_str', @() fstab());
u9b_chk('fe_ends_two', @() fillmissing([NaN 1 2 10 NaN], 'linear'));
u9b_chk('fe_tf_one', @() fetf([NaN 2 NaN]));
u9b_chk('fe_tf_none', @() fetf2([NaN NaN NaN]));
u9b_chk('fe_near_tf', @() fetf3([NaN 2 NaN]));
u9b_chk('fe_int', @() fillmissing(int8([1 2 3]), 'linear'));
u9b_chk('fe_logical', @() msgof(@() fillmissing([true false], 'linear')));
u9b_chk('fe_extrap_prev', @() fillmissing([NaN 2 NaN 4 NaN], 'previous', 'EndValues', 'extrap'));
u9b_chk('fe_near_next', @() fillmissing([NaN 2 NaN 4 NaN], 'nearest', 'EndValues', 'next'));
u9b_chk('fe_mov_none', @() fillmissing([NaN 2 NaN 4 NaN], 'movmean', 3, 'EndValues', 'none'));
u9b_chk('fe_mov_const', @() fillmissing([NaN NaN 2 NaN 4 NaN NaN], 'movmean', 3, 'EndValues', 7));
u9b_chk('fe_const_endnone', @() fillmissing([NaN 2 NaN 4 NaN], 'constant', 9, 'EndValues', 'none'));
u9b_chkdiv('fe_complex', @() oi_try( @() fillmissing([NaN 1+1i 2+3i], 'linear')), 'ADR0235');
u9b_chk('fl_lin_start', @() fillmissing([NaN 2 3], 'linear'));
u9b_chk('fl_lin_end', @() fillmissing([1 2 NaN NaN], 'linear'));
u9b_chk('fl_lin_both', @() fillmissing([NaN NaN 2 4 NaN], 'linear'));
u9b_chk('fl_lin_one', @() fillmissing([NaN 2 NaN], 'linear'));
u9b_chk('fl_lin_gap', @() fillmissing([NaN 1 NaN 5 NaN], 'linear'));
u9b_chkdiv('fl_lin_default', @() oi_try( @() fillmissing([NaN 2 3 NaN])), 'ADR0235');
u9b_chk('fl_lin_none', @() fillmissing([NaN 2 3 NaN], 'linear', 'EndValues', 'none'));
u9b_chk('fl_lin_nearest', @() fillmissing([NaN 2 3 NaN], 'linear', 'EndValues', 'nearest'));
u9b_chk('fl_lin_prev', @() fillmissing([NaN 2 3 NaN], 'linear', 'EndValues', 'previous'));
u9b_chk('fl_lin_next', @() fillmissing([NaN 2 3 NaN], 'linear', 'EndValues', 'next'));
u9b_chk('fl_lin_const', @() fillmissing([NaN 2 3 NaN], 'linear', 'EndValues', 0));
u9b_chk('fl_lin_extrap', @() fillmissing([NaN 2 3 NaN], 'linear', 'EndValues', 'extrap'));
u9b_chk('fl_lin_col', @() fillmissing([NaN NaN; 2 1; 3 NaN; NaN 5], 'linear'));
u9b_chk('fl_lin_dim2', @() fillmissing([NaN 2 3; 1 NaN NaN], 'linear', 2));
u9b_chkdiv('fl_lin_sp', @() oi_try( @() fillmissing([NaN 2 3 NaN], 'linear', 'SamplePoints', [0 1 3 7])), 'ADR0235');
u9b_chk('fl_near_ends', @() fillmissing([NaN 2 3 NaN], 'nearest'));
u9b_chk('fl_prev_ends', @() fillmissing([NaN 2 3 NaN], 'previous'));
u9b_chk('fl_next_ends', @() fillmissing([NaN 2 3 NaN], 'next'));
u9b_chk('fl_mov_ends', @() fillmissing([NaN 2 3 NaN], 'movmean', 3));
u9b_chk('fl_const_ends', @() fillmissing([NaN 2 3 NaN], 'constant', 9));
u9b_chk('fl_prev_extrap', @() fillmissing([NaN 2 3 NaN], 'previous', 'EndValues', 'extrap'));
u9b_chk('fl_near_none', @() fillmissing([NaN 2 3 NaN], 'nearest', 'EndValues', 'none'));
u9b_chk('fl_single', @() fillmissing(single([NaN 2 3]), 'linear'));
u9b_chk('fl_table', @() fltab());
u9b_chk('fl_dt', @() string(fillmissing(datetime(2020, 1, [NaN 2 3]), 'linear')));
u9b_chk('fl_dur', @() seconds(fillmissing(seconds([NaN 2 3 NaN]), 'linear')));
u9b_chk('fl_lin_tf', @() fltf());
u9b_chk('fm_ismissing_str', @() ismissing(["a" "" missing]));
u9b_chk('fm_any_str', @() anymissing(["a" ""]));



function out = dnw(T, n, v)
T.(n) = v;
out = {T.Properties.VariableNames, size(T)};
end

function out = dnwe(T)
T.(1)(2) = 50;
out = T.n';
end

function out = dnwdel(T)
T.(2) = [];
out = T.Properties.VariableNames;
end

function out = ttread()
TT = timetable(seconds([1; 2]), [3; 4], [5; 6]);
out = TT.(2)';
end

function out = ttwrite()
TT = timetable(seconds([1; 2]), [3; 4], [5; 6]);
TT.(1) = [8; 9];
out = TT.Var1';
end

function out = ttread0()
TT = timetable(seconds([1; 2]), [3; 4]);
out = TT.(0);
end

function out = stdot()
s.a = 1;
out = s.(1);
end

function out = nestdot()
s.t = table([1; 2], [3; 4]);
out = s.t.(2)';
end

function out = fmtf(x)
[f, tf] = fillmissing(x, 'previous');
out = {f, tf};
end

function out = fltab()
T = table([NaN; 2; 3], [1; NaN; NaN]);
F = fillmissing(T, 'linear');
out = [F.Var1'; F.Var2'];
end

function out = fltf()
[f, tf] = fillmissing([NaN 2 NaN 4 NaN], 'linear', 'EndValues', 'none');
out = {f, tf};
end

function m = msgof(f)
try
    f();
    m = 'no error';
catch e
    m = [e.identifier ' | ' e.message];
end
end

function out = longstr()
format long
out = string(seconds(1.4142135624));
format short
end

function out = fstf(x)
[~, tf] = fillmissing(x, 'previous');
out = tf;
end

function out = fstab()
T = table(["a"; missing; "c"], [1; NaN; 3]);
F = fillmissing(T, 'previous');
out = F.Var1;
end

function out = fetf(x)
[~, tf] = fillmissing(x, 'linear');
out = tf;
end

function out = fetf2(x)
[f, tf] = fillmissing(x, 'linear');
out = [f; tf];
end

function out = fetf3(x)
[~, tf] = fillmissing(x, 'nearest', 'EndValues', 'none');
out = tf;
end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end

% table_rebuild_metadata.m -- V6 of the value-ownership plan, first sub-stage: a table write is a
% rebuild, and every rebuild keeps what the table is -- its row names, its row times, its dimension
% names, the per-variable units and descriptions, its description and its user data (appendix A #38,
% #39). Dot, brace and paren writes, deletion and growth, on a table and on a timetable, each with
% the alias it was copied from checked afterwards.

run_case('rn_dot_elem_write', @rn_dot_elem_write);
run_case('rn_dot_whole_write', @rn_dot_whole_write);
run_case('rn_dot_new_var', @rn_dot_new_var);
run_case('rn_dot_delete_var', @rn_dot_delete_var);
run_case('rn_dot_text_var_write', @rn_dot_text_var_write);
run_case('rn_dot_growth_extends_names', @rn_dot_growth_extends_names);
run_case('rn_brace_elem_write', @rn_brace_elem_write);
run_case('rn_brace_growth_extends_names', @rn_brace_growth_extends_names);
run_case('rn_paren_row_delete', @rn_paren_row_delete);
run_case('rn_paren_row_select', @rn_paren_row_select);
run_case('rn_row_by_name_after_write', @rn_row_by_name_after_write);
run_case('tt_dot_elem_write', @tt_dot_elem_write);
run_case('tt_dot_whole_write', @tt_dot_whole_write);
run_case('tt_dot_new_var', @tt_dot_new_var);
run_case('tt_rowtimes_class_after_write', @tt_rowtimes_class_after_write);
run_case('tt_time_read_after_write', @tt_time_read_after_write);
run_case('tt_datetime_rowtimes_after_write', @tt_datetime_rowtimes_after_write);
run_case('tt_dot_growth_extends_times', @tt_dot_growth_extends_times);
run_case('tt_paren_row_delete', @tt_paren_row_delete);
run_case('pr_description_survives_write', @pr_description_survives_write);
run_case('pr_userdata_survives_write', @pr_userdata_survives_write);
run_case('pr_units_survive_write', @pr_units_survive_write);
run_case('pr_units_extend_with_new_var', @pr_units_extend_with_new_var);
run_case('pr_units_follow_var_delete', @pr_units_follow_var_delete);
run_case('pr_var_descriptions_survive_write', @pr_var_descriptions_survive_write);
run_case('pr_dimension_names_survive_write', @pr_dimension_names_survive_write);
run_case('pr_dimension_names_default', @pr_dimension_names_default);
run_case('pr_dimension_names_timetable_default', @pr_dimension_names_timetable_default);
run_case('pr_all_survive_row_select', @pr_all_survive_row_select);
run_case('pr_units_follow_column_select', @pr_units_follow_column_select);
run_case('pr_alias_keeps_its_own', @pr_alias_keeps_its_own);
run_case('pr_userdata_is_a_copy', @pr_userdata_is_a_copy);
run_case('pr_defaults', @pr_defaults);
run_case('pr_survive_function_call', @pr_survive_function_call);
run_case('pr_survive_cell_round_trip', @pr_survive_cell_round_trip);

function run_case(name, fn)
try
    fprintf('CHK|%s|%s|exact\n', name, clean(fn()));
catch err
    fprintf('CHK|%s|ERR %s|exact\n', name, clean(err.message));
end
end

function s = clean(s)
if isnumeric(s) || islogical(s)
    s = mat2str(s);
end
s = char(s);
s = strrep(s, char(13), '');
s = strrep(s, char(10), ' ');
s = strrep(s, '|', '/');
end

function s = names(c)
if isempty(c)
    s = '<none>';
else
    s = strjoin(reshape(c, 1, []), ',');
end
end

% --- row names --------------------------------------------------------------------------------------

function s = rn_dot_elem_write()
T = table([1; 2], 'RowNames', {'a'; 'b'});
U = T; U.Var1(1) = 9;
s = sprintf('%s %s %s', names(U.Properties.RowNames), mat2str(U.Var1'), mat2str(T.Var1'));
end

function s = rn_dot_whole_write()
T = table([1; 2], 'RowNames', {'a'; 'b'});
U = T; U.Var1 = [5; 6];
s = sprintf('%s %s %s', names(U.Properties.RowNames), mat2str(U.Var1'), names(T.Properties.RowNames));
end

function s = rn_dot_new_var()
T = table([1; 2], 'RowNames', {'a'; 'b'});
U = T; U.New = [7; 8];
s = sprintf('%s %d %d', names(U.Properties.RowNames), width(U), width(T));
end

function s = rn_dot_delete_var()
T = table([1; 2], [3; 4], 'RowNames', {'a'; 'b'});
U = T; U.Var1 = [];
s = sprintf('%s %s %d', names(U.Properties.RowNames), names(U.Properties.VariableNames), width(T));
end

function s = rn_dot_text_var_write()
T = table([1; 2], {'x'; 'y'}, 'RowNames', {'a'; 'b'});
U = T; U.Var2{2} = 'z';
s = sprintf('%s %s %s', names(U.Properties.RowNames), names(U.Var2), names(T.Var2));
end

function s = rn_dot_growth_extends_names()
T = table([1; 2], 'RowNames', {'a'; 'b'});
U = T; U.Var1(4) = 5;
s = sprintf('%s %s %d', names(U.Properties.RowNames), mat2str(U.Var1'), height(T));
end

function s = rn_brace_elem_write()
T = table([1; 2], 'RowNames', {'a'; 'b'});
U = T; U{2, 1} = 9;
s = sprintf('%s %s %s', names(U.Properties.RowNames), mat2str(U.Var1'), mat2str(T.Var1'));
end

function s = rn_brace_growth_extends_names()
T = table([1; 2], 'RowNames', {'a'; 'b'});
U = T; U{3, 1} = 9;
s = sprintf('%s %s', names(U.Properties.RowNames), mat2str(U.Var1'));
end

function s = rn_paren_row_delete()
T = table([1; 2; 3], 'RowNames', {'a'; 'b'; 'c'});
U = T; U(2, :) = [];
s = sprintf('%s %s', names(U.Properties.RowNames), names(T.Properties.RowNames));
end

function s = rn_paren_row_select()
T = table([1; 2; 3], 'RowNames', {'a'; 'b'; 'c'});
U = T([3 1], :);
s = sprintf('%s %s', names(U.Properties.RowNames), mat2str(U.Var1'));
end

function s = rn_row_by_name_after_write()
T = table([1; 2], 'RowNames', {'a'; 'b'});
T.Var1(2) = 9;
U = T('b', :);
s = sprintf('%d %s', U.Var1, names(U.Properties.RowNames));
end

% --- row times --------------------------------------------------------------------------------------

function s = tt_dot_elem_write()
TT = timetable(seconds([1; 2]), [1; 2]);
U = TT; U.Var1(1) = 9;
s = sprintf('%d %d %s %s', istimetable(U), isequal(U.Properties.RowTimes, seconds([1; 2])), mat2str(U.Var1'), mat2str(TT.Var1'));
end

function s = tt_dot_whole_write()
TT = timetable(seconds([1; 2]), [1; 2]);
U = TT; U.Var1 = [5; 6];
s = sprintf('%d %d %s', istimetable(U), isequal(U.Properties.RowTimes, seconds([1; 2])), mat2str(U.Var1'));
end

function s = tt_dot_new_var()
TT = timetable(seconds([1; 2]), [1; 2]);
U = TT; U.New = [7; 8];
s = sprintf('%d %d %d %d', istimetable(U), isequal(U.Properties.RowTimes, seconds([1; 2])), width(U), width(TT));
end

function s = tt_rowtimes_class_after_write()
TT = timetable(seconds([1; 2]), [1; 2]);
before = class(TT.Properties.RowTimes);
TT.Var1(1) = 9;
s = sprintf('%s %s %s', before, class(TT.Properties.RowTimes), class(TT.Time));
end

function s = tt_time_read_after_write()
TT = timetable(seconds([1; 2]), [1; 2]);
TT.Var1(2) = 9;
s = mat2str(seconds(TT.Time)');
end

function s = tt_datetime_rowtimes_after_write()
TT = timetable(datetime(2020, 1, [1; 2]), [1; 2]);
U = TT; U.Var1(1) = 9;
s = sprintf('%d %s %d %d', istimetable(U), class(U.Properties.RowTimes), day(U.Properties.RowTimes(2)), isequal(U.Properties.RowTimes, TT.Properties.RowTimes));
end

function s = tt_dot_growth_extends_times()
TT = timetable(seconds([1; 2]), [1; 2]);
U = TT; U.Var1(4) = 5;
s = sprintf('%d %s %s %d', height(U), mat2str(seconds(U.Time)'), mat2str(U.Var1'), height(TT));
end

function s = tt_paren_row_delete()
TT = timetable(seconds([1; 2; 3]), [1; 2; 3]);
U = TT; U(2, :) = [];
s = sprintf('%d %s %s', istimetable(U), mat2str(seconds(U.Time)'), mat2str(seconds(TT.Time)'));
end

% --- the other properties ---------------------------------------------------------------------------

function s = pr_description_survives_write()
T = table([1; 2]);
T.Properties.Description = 'about';
T.Var1(1) = 9;
T.New = [3; 4];
s = sprintf('[%s] %s', T.Properties.Description, class(T.Properties.Description));
end

function s = pr_userdata_survives_write()
T = table([1; 2]);
T.Properties.UserData = [1 2 3];
T.Var1(1) = 9;
T.New = [3; 4];
s = mat2str(T.Properties.UserData);
end

function s = pr_units_survive_write()
T = table([1; 2], [3; 4]);
T.Properties.VariableUnits = {'m', 's'};
T.Var1(1) = 9;
T.Var2 = [7; 8];
s = names(T.Properties.VariableUnits);
end

function s = pr_units_extend_with_new_var()
T = table([1; 2], [3; 4]);
T.Properties.VariableUnits = {'m', 's'};
T.New = [5; 6];
u = T.Properties.VariableUnits;
s = sprintf('%d [%s] [%s] [%s]', numel(u), u{1}, u{2}, u{3});
end

function s = pr_units_follow_var_delete()
T = table([1; 2], [3; 4], [5; 6]);
T.Properties.VariableUnits = {'m', 's', 'kg'};
T.Var2 = [];
s = names(T.Properties.VariableUnits);
end

function s = pr_var_descriptions_survive_write()
T = table([1; 2], [3; 4]);
T.Properties.VariableDescriptions = {'first', 'second'};
T.Var2(2) = 0;
s = names(T.Properties.VariableDescriptions);
end

function s = pr_dimension_names_survive_write()
T = table([1; 2]);
T.Properties.DimensionNames = {'Id', 'Data'};
T.Var1(1) = 9;
T.New = [3; 4];
s = names(T.Properties.DimensionNames);
end

function s = pr_dimension_names_default()
T = table([1; 2]);
s = names(T.Properties.DimensionNames);
end

function s = pr_dimension_names_timetable_default()
TT = timetable(seconds([1; 2]), [1; 2]);
TT.Var1(1) = 9;
s = names(TT.Properties.DimensionNames);
end

function s = pr_all_survive_row_select()
T = table([1; 2; 3], [4; 5; 6], 'RowNames', {'a'; 'b'; 'c'});
T.Properties.Description = 'about';
T.Properties.UserData = 7;
T.Properties.VariableUnits = {'m', 's'};
T.Properties.DimensionNames = {'Id', 'Data'};
U = T([1 3], :);
s = sprintf('[%s] %d %s %s %s', U.Properties.Description, U.Properties.UserData, ...
    names(U.Properties.VariableUnits), names(U.Properties.DimensionNames), names(U.Properties.RowNames));
end

function s = pr_units_follow_column_select()
T = table([1; 2], [3; 4], [5; 6]);
T.Properties.VariableUnits = {'m', 's', 'kg'};
T.Properties.VariableDescriptions = {'one', 'two', 'three'};
U = T(:, [3 1]);
s = sprintf('%s %s', names(U.Properties.VariableUnits), names(U.Properties.VariableDescriptions));
end

function s = pr_alias_keeps_its_own()
T = table([1; 2]);
T.Properties.Description = 't';
U = T;
U.Properties.Description = 'u';
U.Properties.VariableUnits = {'m'};
U.Var1(1) = 9;
s = sprintf('[%s] [%s] %s %s', T.Properties.Description, U.Properties.Description, ...
    names(T.Properties.VariableUnits), names(U.Properties.VariableUnits));
end

function s = pr_userdata_is_a_copy()
T = table([1; 2]);
v = [1 2 3];
T.Properties.UserData = v;
v(1) = 7;
w = T.Properties.UserData;
w(2) = 8;
s = sprintf('%s %s %s', mat2str(v), mat2str(w), mat2str(T.Properties.UserData));
end

function s = pr_defaults()
T = table([1; 2], [3; 4]);
p = T.Properties;
s = sprintf('%s/%s/%s/%s/%s/%s', class(p.Description), mat2str(size(p.Description)), ...
    class(p.UserData), mat2str(size(p.UserData)), class(p.VariableUnits), mat2str(size(p.VariableUnits)));
end

function T = bump_first(T)
T.Var1(1) = 9;
end

function s = pr_survive_function_call()
T = table([1; 2], 'RowNames', {'a'; 'b'});
T.Properties.Description = 'about';
U = bump_first(T);
s = sprintf('[%s] %s %s %s', U.Properties.Description, names(U.Properties.RowNames), mat2str(U.Var1'), mat2str(T.Var1'));
end

function s = pr_survive_cell_round_trip()
T = table([1; 2], 'RowNames', {'a'; 'b'});
T.Properties.Description = 'about';
c = {T};
U = c{1};
U.Var1(2) = 0;
s = sprintf('[%s] %s %s', U.Properties.Description, names(U.Properties.RowNames), mat2str(c{1}.Var1'));
end

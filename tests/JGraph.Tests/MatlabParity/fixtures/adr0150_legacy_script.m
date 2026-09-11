% adr0150_legacy_script.m -- what a folder of legacy MATLAB scripts needed to run here (ADR 0150),
% measured in R2025b: upper and lower hand anything that is not text back untouched and refuse a
% cell holding non-char; warning keeps a state per identifier, answers and restores it as a struct,
% and a suppressed warning is still what lastwarn reports, identifier included; hist counts and
% centres by MATLAB's own arithmetic, column by column for a matrix; and run() of a function file
% calls its main function.

% --- upper and lower over what is not text ----------------------------------------------------
chk('upper_number_class', class(upper(600)));
chk('upper_number', upper(600));
chk('upper_number_strcmp', strcmp(upper(600), 'NAN'));
chk('upper_row', mat2str(upper([65 97])));
chk('lower_int8_class', class(lower(int8(66))));
chk('upper_logical', upper(true));
chk('upper_logical_class', class(upper(true)));
m = upper(['ab'; 'cd']);
chk('upper_charmatrix_rows', [m(1, :) '/' m(2, :)]);
chk('upper_charmatrix_size', mat2str(size(m)));
c = upper({'ab', 'cd'});
chk('upper_cell_class', class(c));
chk('upper_cell', [c{1} '/' c{2}]);
chk('upper_cell_col_size', mat2str(size(upper({'ab'; 'cd'}))));
chk('upper_empty_cell_class', class(upper({})));
chk('upper_empty_cell_size', mat2str(size(upper({}))));
chk('upper_string_class', class(upper("ab")));
chk('upper_string', upper("ab"));
chk('lower_string_array', strjoin(lower(["AB" "Cd"]), '/'));
chk('upper_zeros_size', mat2str(size(upper(zeros(2, 3)))));
s = upper(struct('a', 1));
chk('upper_struct_field', s.a);
try
    upper({'ab', 65});
    chk('upper_mixed_cell', 'no error');
catch e
    chk('upper_mixed_cell', e.identifier);
end
try
    upper({'ab', "cd"});
    chk('upper_string_in_cell', 'no error');
catch e
    chk('upper_string_in_cell', e.identifier);
end

% --- warning keeps its states -----------------------------------------------------------------
q = warning('query', 'MATLAB:divideByZero');
chk('query_class', class(q));
chk('query_id', q.identifier);
chk('query_state', q.state);
q = warning('query', 'ADR0150:unset');
chk('query_unset_state', q.state);
p1 = warning('off', 'ADR0150:one');
chk('off_previous_state', p1.state);
chk('off_previous_id', p1.identifier);
q = warning('query', 'ADR0150:one');
chk('off_then_query', q.state);
warning('ADR0150:one', 'hidden %d', 4);
[msg, id] = lastwarn;
chk('lastwarn_hidden_msg', msg);
chk('lastwarn_hidden_id', id);
p2 = warning('on', 'ADR0150:one');
chk('on_previous_state', p2.state);
q = warning('query', 'ADR0150:one');
chk('on_then_query', q.state);
warning(p2);
q = warning('query', 'ADR0150:one');
chk('restore_from_struct', q.state);
warning(p1);
q = warning('query', 'ADR0150:one');
chk('restore_to_on', q.state);
warning('ADR0150:one', 'shown %d', 5);
[msg, id] = lastwarn;
chk('lastwarn_shown_msg', msg);
chk('lastwarn_shown_id', id);
warning('plain message %d', 6);
[msg, id] = lastwarn;
chk('lastwarn_plain', ['[' msg '][' id ']']);
lastwarn('replaced', 'ADR0150:two');
[msg, id] = lastwarn;
chk('lastwarn_set', ['[' msg '][' id ']']);
lastwarn('');
[msg, id] = lastwarn;
chk('lastwarn_cleared', ['[' msg '][' id ']']);
q = warning('QUERY', 'ADR0150:one');
chk('query_verb_case', q.state);
chk('query_all_class', class(warning('query', 'all')));
try
    warning('query', 3);
    chk('query_number', 'no error');
catch e
    chk('query_number', e.identifier);
end
try
    warning('off', 'no colon');
    chk('off_no_colon', 'no error');
catch e
    chk('off_no_colon', e.identifier);
end

% --- hist, the legacy histogram ---------------------------------------------------------------
y = [1 2 2 3 5 8 8.5 9 10];
[n, x] = hist(y);
chk('hist_default_n', mat2str(n));
chk('hist_default_x', mat2str(x, 15));
[n, x] = hist(y, 4);
chk('hist_4_n', mat2str(n));
chk('hist_4_x', mat2str(x, 15));
[n, x] = hist(y, [2 5 8]);
chk('hist_centres_n', mat2str(n));
chk('hist_centres_x', mat2str(x));
[n, x] = hist(y, [0 3 6 20]);
chk('hist_centres2_n', mat2str(n));
[n, x] = hist([3 3 3], 5);
chk('hist_const', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist([1 2 3; 4 5 6; 7 8 9], 3);
chk('hist_matrix_n', mat2str(n));
chk('hist_matrix_x', mat2str(x, 15));
[n, x] = hist([1 2 3; 4 5 6; 7 8 9]', 3);
chk('hist_matrix_t_n', mat2str(n));
[n, x] = hist(y', 3);
chk('hist_col_sizes', [mat2str(size(n)) ' ' mat2str(size(x))]);
[n, x] = hist([1 NaN 2 3], 2);
chk('hist_nan', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist([], 3);
chk('hist_empty', [mat2str(n) ' ' mat2str(x)]);
n = hist(y, 3);
chk('hist_one_out', mat2str(n));
[n, x] = hist(y, 0);
chk('hist_zero_bins', [mat2str(size(n)) ' ' mat2str(size(x))]);
[n, x] = hist(y, 2.5);
chk('hist_frac', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist(y, 1);
chk('hist_one_bin', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist(y, [2 2 8]);
chk('hist_dup_centres', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist([NaN NaN], 2);
chk('hist_all_nan', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist([1 Inf 2], 2);
chk('hist_inf', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist([1 2 3 4], [1 2 3 4]');
chk('hist_col_centres', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist([1 2 3; 4 5 6]', [1.5 4.5]);
chk('hist_matrix_centres', [mat2str(n) ' ' mat2str(x)]);
[n, x] = hist(logical([1 0 1 1]), 2);
chk('hist_logical', [mat2str(n) ' ' mat2str(x)]);
try
    hist('abc');
    chk('hist_char', 'no error');
catch e
    chk('hist_char', e.identifier);
end
try
    hist({1});
    chk('hist_cell', 'no error');
catch e
    chk('hist_cell', e.identifier);
end
try
    hist();
    chk('hist_no_args', 'no error');
catch e
    chk('hist_no_args', e.identifier);
end
z = mod((1:500) * 0.618033988749895, 1);
[n, x] = hist(z, 50);
chk('hist_noise_sum', sum(n));
chk('hist_noise_max', max(n));
chk('hist_noise_n', mat2str(n));
fprintf('CHK|hist_noise_x1|%.17g|rel=1e-12\n', x(1));
fprintf('CHK|hist_noise_x50|%.17g|rel=1e-12\n', x(50));

% --- run() of a function file calls its main function -----------------------------------------
root = fullfile(tempdir, 'adr0150_run');
if exist(root, 'dir') == 7
    rmdir(root, 's');
end
mkdir(root);
fid = fopen(fullfile(root, 'mainfile.m'), 'w');
fprintf(fid, 'function mainfile\n');
fprintf(fid, 'fprintf(''CHK|run_main_called|yes|exact\\n'');\n');
fprintf(fid, 'helper();\n');
fprintf(fid, 'function helper\n');
fprintf(fid, 'fprintf(''CHK|run_local_called|yes|exact\\n'');\n');
fclose(fid);
run(fullfile(root, 'mainfile.m'));
rmdir(root, 's');

function chk(name, v)
fprintf('CHK|%s|%s|exact\n', name, show(v));
end

function s = show(v)
if ischar(v)
    s = v;
elseif isstring(v)
    s = char(v);
elseif isscalar(v) && (isnumeric(v) || islogical(v))
    s = sprintf('%.17g', double(v));
else
    s = ['<' class(v) '>'];
end
end

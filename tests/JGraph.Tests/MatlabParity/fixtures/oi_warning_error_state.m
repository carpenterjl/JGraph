% record: -noFigureWindows
% Open item 189 (ADR 0346): warning('error', id) is the third warning state. A warning under the
% identifier is raised as an error with that identifier and the warning's message, lastwarn is left
% alone, the query answers 'error', a query's struct puts it back, 'all' is refused with it, and
% warning('on'/'off', 'all') forgets it. Probes probe_w..probe_w5 (agent F2's scratch).
s0 = warning;
u9b_chk('set_error_returns', @() warning('error', 'oi189:a'));
u9b_chk('query_error', @() warning('query', 'oi189:a'));
fprintf('CHK|query_error_stmt|%s|exact\n', strtrim(evalc('warning(''query'', ''oi189:a'')')));
oi189_raise('raise_format', @() warning('oi189:a', 'hello %d and %s', 5, 'abc'));
oi189_raise('raise_plain', @() warning('oi189:a', 'plain text'));
oi189_raise('raise_in_fn', @() oi189_in_fn('oi189:a', 'from a function'), 'stack');
oi189_raise('raise_tab', @() warning('oi189:a', 'tab\there %d%%', 7));
oi189_raise('raise_newline', @() warning('oi189:a', 'line1\nline2'));
oi189_raise('other_id', @() warning('oi189:b', 'other id'));
oi189_raise('no_id', @() warning('no identifier here'));
u9b_chk('set_error_again', @() warning('error', 'oi189:a'));
u9b_chk('set_off_from_error', @() warning('off', 'oi189:a'));
oi189_raise('raise_off', @() warning('oi189:a', 'now off'));
u9b_chk('set_on_from_off', @() warning('on', 'oi189:a'));
oi189_raise('raise_on', @() warning('oi189:a', 'now on'));
warning(struct('identifier', 'oi189:a', 'state', 'error'));
u9b_chk('restored_by_struct', @() warning('query', 'oi189:a'));
oi189_raise('raise_restored', @() warning('oi189:a', 'restored error'));
warning('on', 'oi189:a');
u9b_chk('error_all', @() warning('error', 'all'));
u9b_chk('after_all_refused', @() warning('query', 'oi189:c'));
warning('error', 'oi189:q');
warning('on', 'all');
u9b_chk('on_all_forgets', @() warning('query', 'oi189:q'));
warning('error', 'oi189:r');
warning('off', 'all');
u9b_chk('off_all_forgets', @() warning('query', 'oi189:r'));
oi189_raise('off_all_raise', @() warning('oi189:r', 'silent now'));
warning('error', 'oi189:f');
oi189_raise('error_under_off_all', @() warning('oi189:f', 'error though all off'));
oi189_raise('other_under_off_all', @() warning('oi189:g', 'silent'));
warning('on', 'all');
u9b_chk('upper_verb', @() warning('ERROR', 'oi189:h'));
u9b_chk('upper_verb_query', @() warning('query', 'oi189:h'));
oi189_raise('upper_verb_raise', @() warning('oi189:h', 'upper case verb'));
warning('on', 'oi189:h');
u9b_chk('error_backtrace', @() warning('error', 'backtrace'));
u9b_chk('error_verbose', @() warning('error', 'verbose'));
u9b_chk('error_bad_id', @() warning('error', 'noColon'));
u9b_chk('error_number', @() warning('error', 5));
u9b_chk('error_three', @() warning('error', 'oi189:i', 'extra'));
fprintf('CHK|statement_silent|%s|exact\n', u9b_text(evalc('warning(''error'', ''oi189:t'')')));
warning('on', 'oi189:t');
oi189_raise('one_arg_error', @() warning('error'));
warning('off', 'backtrace');
warning('error', 'oi189:j');
oi189_raise('backtrace_off', @() warning('oi189:j', 'backtrace off'));
warning('on', 'backtrace');
warning('on', 'verbose');
oi189_raise('verbose_on', @() warning('oi189:j', 'verbose on'));
warning('off', 'verbose');
warning('on', 'oi189:j');
% Two warnings the engine raises itself.
warning('error', 'MATLAB:rmpath:DirNotFound');
oi189_raise('engine_rmpath', @() rmpath(fullfile(tempdir, 'oi189_no_such_folder')), 'nomsg');
warning('on', 'MATLAB:rmpath:DirNotFound');
oi189_raise('engine_rmpath_on', @() rmpath(fullfile(tempdir, 'oi189_no_such_folder')), 'nomsg');
x = 1:10;
warning('error', 'MATLAB:colon:nonIntegerIndex');
oi189_raise('engine_colon', @() x(1.5:3));
warning('on', 'MATLAB:colon:nonIntegerIndex');
oi189_raise('engine_colon_on', @() x(1.5:3));
% Caught by the script's own try.
warning('error', 'oi189:k');
try
    warning('oi189:k', 'caught %s', 'here');
catch e
    fprintf('CHK|caught|%s %s|exact\n', class(e), e.identifier);
    fprintf('CHK|caught_msg|%s|exact\n', e.message);
    fprintf('CHK|caught_report|%s|div=ADR0346\n', strrep(getReport(e, 'basic'), newline, '/'));
end
warning('on', 'oi189:k');
% The state table.
warning('error', 'oi189:m');
warning('off', 'oi189:n');
s = warning;
fprintf('CHK|table_m|%s|exact\n', u9b_text(s(strcmp({s.identifier}, 'oi189:m'))));
fprintf('CHK|table_n|%s|exact\n', u9b_text(s(strcmp({s.identifier}, 'oi189:n'))));
fprintf('CHK|table_all|%s|exact\n', u9b_text(s(1)));
warning(s0);
u9b_chk('restored_whole_table', @() warning('query', 'oi189:m'));

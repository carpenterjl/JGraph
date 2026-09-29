% probe_sp_callbacks: configureCallback's forms and refusals, where and how often a
% BytesAvailableFcn runs, its event data, an error inside it, ErrorOccurredFcn, and timers during
% a blocking read.
device_peer();
global CBLOG
s = serialport("COM20", 9600, "Timeout", 2);
dp(s, 'reset');
dv_px('cc_none', 'configureCallback(s)');
dv_px('cc_off', 'configureCallback(s, "off")');
dv_px('cc_off_extra', 'configureCallback(s, "off", @disp)');
dv_px('cc_term_noarg', 'configureCallback(s, "terminator")');
dv_px('cc_byte_3', 'configureCallback(s, "byte", @disp)');
dv_px('cc_term_4', 'configureCallback(s, "terminator", 5, @disp)');
dv_px('cc_bad_mode', 'configureCallback(s, "bogus", @disp)');
dv_px('cc_num_mode', 'configureCallback(s, 5, @disp)');
dv_px('cc_term_badfcn', 'configureCallback(s, "terminator", 5)');
dv_px('cc_term_strfcn', 'configureCallback(s, "terminator", "disp")');
dv_px('cc_byte_badcount', 'configureCallback(s, "byte", -1, @disp)');
dv_px('cc_byte_zero', 'configureCallback(s, "byte", 0, @disp)');
dv_px('cc_byte_frac', 'configureCallback(s, "byte", 1.5, @disp)');
dv_px('cc_byte_str', 'configureCallback(s, "byte", "5", @disp)');
dv_px('cc_five', 'configureCallback(s, "byte", 1, @disp, 5)');
dv_px('cc_partial', 'configureCallback(s, "term", @disp)');
dv_pr('cc_partial_mode', 's.BytesAvailableFcnMode');
dv_pr('cc_partial_fcn', 'func2str(s.BytesAvailableFcn)');
dv_px('cc_byte_ok', 'configureCallback(s, "byte", 7, @disp)');
dv_pr('cc_byte_mode', 's.BytesAvailableFcnMode');
dv_pr('cc_byte_count', 's.BytesAvailableFcnCount');
dv_pr('cc_byte_count_class', 'class(s.BytesAvailableFcnCount)');
dv_px('cc_off_again', 'configureCallback(s, "off")');
dv_pr('cc_off_mode', 's.BytesAvailableFcnMode');
dv_pr('cc_off_count', 's.BytesAvailableFcnCount');
dv_pr('cc_off_fcn', 's.BytesAvailableFcn');
dv_px('cc_off_upper', 'configureCallback(s, "OFF")');
dv_px('cc_empty_fcn', 'configureCallback(s, "terminator", [])');
dv_pr('cc_empty_fcn_mode', 's.BytesAvailableFcnMode');
configureCallback(s, "off");
% byte mode: how many calls for 10 bytes at count 3, in one chunk
CBLOG = {};
configureCallback(s, "byte", 3, @(src, evt) sp_cblog(src, evt));
dp(s, sprintf('send %s', dp_hex(1:10)));
pause(1);
dv_pr('byte_chunk_calls', 'numel(CBLOG)');
dv_pr('byte_chunk_log', 'CBLOG');
dv_pr('byte_chunk_left', 's.NumBytesAvailable');
flush(s);
% byte mode: 10 bytes arriving one at a time
CBLOG = {};
dp(s, sprintf('chunks 40 1 %s', dp_hex(1:10)));
pause(1.5);
dv_pr('byte_trickle_calls', 'numel(CBLOG)');
dv_pr('byte_trickle_log', 'CBLOG');
flush(s);
% a callback that reads what it was told about
CBLOG = {};
configureCallback(s, "byte", 4, @(src, evt) sp_cbread(src, evt));
dp(s, sprintf('send %s', dp_hex(1:12)));
pause(1);
dv_pr('byte_reading_calls', 'numel(CBLOG)');
dv_pr('byte_reading_log', 'CBLOG');
flush(s);
% where it runs: a busy loop, between statements, at pause(0), at drawnow
CBLOG = {};
configureCallback(s, "byte", 1, @(src, evt) sp_cblog(src, evt));
dp(s, sprintf('later 100 %s', dp_hex(1)));
t0 = tic;
while toc(t0) < 0.5
end
dv_pr('busy_loop_calls', 'numel(CBLOG)');
x = 1; x = x + 1; %#ok<NASGU>
dv_pr('after_statements_calls', 'numel(CBLOG)');
pause(0);
dv_pr('after_pause0_calls', 'numel(CBLOG)');
drawnow;
dv_pr('after_drawnow_calls', 'numel(CBLOG)');
pause(0.05);
dv_pr('after_pause_calls', 'numel(CBLOG)');
flush(s);
CBLOG = {};
dp(s, sprintf('later 100 %s', dp_hex(1)));
t0 = tic;
while toc(t0) < 0.5
end
drawnow;
dv_pr('drawnow_first_calls', 'numel(CBLOG)');
flush(s);
CBLOG = {};
dp(s, sprintf('later 100 %s', dp_hex(1)));
t0 = tic;
while toc(t0) < 0.5
end
dv_pr('before_read_calls', 'numel(CBLOG)');
read(s, 1, "uint8");
dv_pr('after_read_calls', 'numel(CBLOG)');
pause(0.2);
dv_pr('after_read_pause_calls', 'numel(CBLOG)');
% the event data
CBLOG = {};
configureCallback(s, "byte", 2, @(src, evt) sp_cbevt(src, evt));
dp(s, sprintf('send %s', dp_hex(1:2)));
pause(0.5);
dv_pr('evt_log', 'CBLOG');
flush(s);
CBLOG = {};
configureCallback(s, "terminator", @(src, evt) sp_cbevt(src, evt));
dp(s, sprintf('send %s', dp_hex(['ab' 10])));
pause(0.5);
dv_pr('evt_term_log', 'CBLOG');
flush(s);
% terminator mode: several lines in one chunk, then a line in pieces
CBLOG = {};
configureCallback(s, "terminator", @(src, evt) sp_cblog(src, evt));
dp(s, sprintf('send %s', dp_hex(['a' 10 'b' 10 'c' 10])));
pause(0.5);
dv_pr('term_chunk_calls', 'numel(CBLOG)');
dv_pr('term_chunk_left', 's.NumBytesAvailable');
flush(s);
CBLOG = {};
dp(s, sprintf('chunks 50 1 %s', dp_hex(['xy' 10 'z' 10])));
pause(1);
dv_pr('term_trickle_calls', 'numel(CBLOG)');
flush(s);
CBLOG = {};
configureCallback(s, "terminator", @(src, evt) sp_cbline(src, evt));
dp(s, sprintf('send %s', dp_hex(['l1' 10 'l2' 10])));
pause(0.5);
dv_pr('term_reading_log', 'CBLOG');
flush(s);
% an error inside the callback
configureCallback(s, "byte", 1, @(src, evt) error('probe:cb', 'boom in callback'));
dp(s, sprintf('send %s', dp_hex(1)));
pause(0.5);
dv_pr('cb_error_survived', 'true');
[msg, id] = lastwarn;
dv_pr('cb_error_lastwarn', 'id');
dv_pr('cb_error_lastwarn_msg', 'msg');
configureCallback(s, "off");
flush(s);
% configureCallback off with data pending
CBLOG = {};
configureCallback(s, "byte", 1, @(src, evt) sp_cblog(src, evt));
dp(s, sprintf('later 100 %s', dp_hex(1)));
t0 = tic;
while toc(t0) < 0.5
end
configureCallback(s, "off");
pause(0.3);
dv_pr('off_pending_calls', 'numel(CBLOG)');
flush(s);
% flush resets the byte count crossing
CBLOG = {};
configureCallback(s, "byte", 4, @(src, evt) sp_cblog(src, evt));
dp(s, sprintf('send %s', dp_hex(1:3)));
pause(0.3);
flush(s);
dp(s, sprintf('send %s', dp_hex(1:3)));
pause(0.3);
dv_pr('flush_crossing_calls', 'numel(CBLOG)');
configureCallback(s, "off");
flush(s);
% a timer during a blocking read
CBLOG = {};
tm = timer('ExecutionMode', 'fixedRate', 'Period', 0.1, 'TimerFcn', @(~, ~) sp_cblog([], 'tick'));
start(tm);
s.Timeout = 1;
dp(s, sprintf('later 700 %s', dp_hex(1)));
read(s, 1, "uint8");
dv_pr('timer_during_read', 'numel(CBLOG)');
pause(0.35);
dv_pr('timer_after_read', 'numel(CBLOG) > 0');
stop(tm);
delete(tm);
% ErrorOccurredFcn is not raised by a timeout
s.ErrorOccurredFcn = @(varargin) sp_cblog([], 'error');
CBLOG = {};
read(s, 1, "uint8");
pause(0.2);
dv_pr('errfcn_on_timeout', 'numel(CBLOG)');

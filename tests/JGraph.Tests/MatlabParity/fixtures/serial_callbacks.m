% serial_callbacks.m -- serialport's callbacks (device classes plan, stage D1): configureCallback's
% three forms and their refusals; how often a BytesAvailableFcn runs in "byte" mode (once per Count
% bytes received since the mode was set or the input flushed, however they arrived) and in
% "terminator" mode (once per terminator); where it runs (at pause, pause(0) included, and drawnow;
% never in a busy loop, between statements, or inside a blocking read); its event data; and a mode
% turned off with events still queued. A timer does fire inside a blocking read. The rows whose peer
% trickles its bytes wait for the count with a ten-second deadline, then 0.2 s for any extra call,
% so a peer slowed by a loaded machine cannot end the row early (open item 37).
%
% An error inside the callback is R2025b's warning MATLAB:callback:DynamicPropertyEventError, whose
% text names AsyncIO's internals; JGraph gives the identifier and its own sentence (div=ADR0184).

global DVLOG
s = dv_open();
ix_chk('cc_none', dv_err(@() configureCallback(s)));
ix_chk('cc_off_extra', dv_err(@() configureCallback(s, "off", @disp)));
ix_chk('cc_term_noarg', dv_err(@() configureCallback(s, "terminator")));
ix_chk('cc_byte_3', dv_err(@() configureCallback(s, "byte", @disp)));
ix_chk('cc_term_4', dv_err(@() configureCallback(s, "terminator", 5, @disp)));
ix_chk('cc_bad_mode', dv_err(@() configureCallback(s, "bogus", @disp)));
ix_chk('cc_num_mode', dv_err(@() configureCallback(s, 5, @disp)));
ix_chk('cc_byte_badcount', dv_err(@() configureCallback(s, "byte", -1, @disp)));
ix_chk('cc_byte_zero', dv_err(@() configureCallback(s, "byte", 0, @disp)));
ix_chk('cc_byte_frac', dv_err(@() configureCallback(s, "byte", 1.5, @disp)));
ix_chk('cc_byte_str', dv_err(@() configureCallback(s, "byte", "5", @disp)));
ix_chk('cc_five', dv_err(@() configureCallback(s, "byte", 1, @disp, 5)));
ix_chk('cc_term_badfcn', dv_err(@() configureCallback(s, "terminator", 5)));
ix_chk('cc_term_strfcn', dv_err(@() configureCallback(s, "terminator", "disp")));
configureCallback(s, "off");
configureCallback(s, "term", @disp);
ix_chk('cc_partial_mode', ix_show(s.BytesAvailableFcnMode));
ix_chk('cc_partial_fcn', class(s.BytesAvailableFcn));
configureCallback(s, "byte", 7, @disp);
ix_chk('cc_byte_mode', ix_show(s.BytesAvailableFcnMode));
ix_chk('cc_byte_count', ix_show(s.BytesAvailableFcnCount));
configureCallback(s, "OFF");
ix_chk('cc_off_mode', ix_show(s.BytesAvailableFcnMode));
ix_chk('cc_off_count', ix_show(s.BytesAvailableFcnCount));
ix_chk('cc_off_fcn', isempty(s.BytesAvailableFcn));
configureCallback(s, "terminator", []);
ix_chk('cc_empty_fcn_mode', ix_show(s.BytesAvailableFcnMode));
ix_chk('cc_empty_fcn', isempty(s.BytesAvailableFcn));
configureCallback(s, "off");

% "byte" mode: ten bytes at once, then one at a time, with Count 3
DVLOG = {};
configureCallback(s, "byte", 3, @dv_cblog);
dp(s, sprintf('send %s', dp_hex(1:10)));
pause(1);
ix_chk('byte_chunk_calls', numel(DVLOG));
ix_chk('byte_chunk_log', ix_show(DVLOG));
flush(s);
DVLOG = {};
dp(s, sprintf('chunks 40 1 %s', dp_hex(1:10)));
t0 = tic; while numel(DVLOG) < 3 && toc(t0) < 10, pause(0.05); end
pause(0.2);
ix_chk('byte_trickle_calls', numel(DVLOG));
ix_chk('byte_trickle_log', ix_show(DVLOG));
flush(s);

% a callback that reads what it was told of
DVLOG = {};
configureCallback(s, "byte", 4, @dv_cbread);
dp(s, sprintf('send %s', dp_hex(1:12)));
pause(1);
ix_chk('byte_reading_log', ix_show(DVLOG));
flush(s);

% where it runs
DVLOG = {};
configureCallback(s, "byte", 1, @dv_cblog);
dp(s, sprintf('later 100 %s', dp_hex(1)));
t0 = tic;
while toc(t0) < 0.5
end
ix_chk('busy_loop_calls', numel(DVLOG));
x = 1; x = x + 1; %#ok<NASGU>
ix_chk('after_statements_calls', numel(DVLOG));
pause(0);
ix_chk('after_pause0_calls', numel(DVLOG));
flush(s);
DVLOG = {};
dp(s, sprintf('later 100 %s', dp_hex(1)));
t0 = tic;
while toc(t0) < 0.5
end
drawnow;
ix_chk('after_drawnow_calls', numel(DVLOG));
flush(s);
DVLOG = {};
dp(s, sprintf('later 100 %s', dp_hex(1)));
t0 = tic;
while toc(t0) < 0.5
end
read(s, 1, "uint8");
ix_chk('after_read_calls', numel(DVLOG));
pause(0.2);
ix_chk('after_read_pause_calls', numel(DVLOG));
ix_chk('after_read_pause_log', ix_show(DVLOG));
flush(s);

% the event data
DVLOG = {};
configureCallback(s, "byte", 2, @dv_cbevt);
dp(s, sprintf('send %s', dp_hex(1:2)));
pause(0.5);
ix_chk('evt_log', ix_show(DVLOG));
flush(s);
DVLOG = {};
configureCallback(s, "terminator", @dv_cbevt);
dp(s, sprintf('send %s', dp_hex(['ab' 10])));
pause(0.5);
ix_chk('evt_term_log', ix_show(DVLOG));
flush(s);

% "terminator" mode: three lines at once, then two lines a byte at a time, then a reader
DVLOG = {};
configureCallback(s, "terminator", @dv_cblog);
dp(s, sprintf('send %s', dp_hex(['a' 10 'b' 10 'c' 10])));
pause(0.5);
ix_chk('term_chunk_calls', numel(DVLOG));
flush(s);
DVLOG = {};
dp(s, sprintf('chunks 50 1 %s', dp_hex(['xy' 10 'z' 10])));
t0 = tic; while numel(DVLOG) < 2 && toc(t0) < 10, pause(0.05); end
pause(0.2);
ix_chk('term_trickle_calls', numel(DVLOG));
flush(s);
DVLOG = {};
configureCallback(s, "terminator", @dv_cbline);
dp(s, sprintf('send %s', dp_hex(['l1' 10 'l2' 10])));
pause(1);
ix_chk('term_reading_log', ix_show(DVLOG));
flush(s);
configureTerminator(s, "CR/LF");
DVLOG = {};
configureCallback(s, "terminator", @dv_cblog);
dp(s, sprintf('chunks 30 1 %s', dp_hex(['p' 13 10 'q' 13 'r' 10 13 10])));
t0 = tic; while numel(DVLOG) < 2 && toc(t0) < 10, pause(0.05); end
pause(0.2);
ix_chk('term_crlf_calls', numel(DVLOG));
configureTerminator(s, "LF");
flush(s);

% an error inside the callback is a warning, and the script goes on
configureCallback(s, "byte", 1, @(src, evt) error('fixture:cb', 'boom in callback'));
lastwarn('');
dp(s, sprintf('send %s', dp_hex(1)));
pause(0.5);
[m, id] = lastwarn;
ix_chk('cb_error_id', id);
ix_chk('cb_error_text', m, 'div=ADR0184');
configureCallback(s, "off");
flush(s);

% turned off with an event queued: it never runs
DVLOG = {};
configureCallback(s, "byte", 1, @dv_cblog);
dp(s, sprintf('later 100 %s', dp_hex(1)));
t0 = tic;
while toc(t0) < 0.5
end
configureCallback(s, "off");
pause(0.3);
ix_chk('off_pending_calls', numel(DVLOG));
flush(s);

% flush starts the byte count again
DVLOG = {};
configureCallback(s, "byte", 4, @dv_cblog);
dp(s, sprintf('send %s', dp_hex(1:3)));
pause(0.3);
flush(s);
dp(s, sprintf('send %s', dp_hex(1:3)));
pause(0.3);
ix_chk('flush_crossing_calls', numel(DVLOG));
configureCallback(s, "off");
flush(s);

% a timer fires inside a blocking read; ErrorOccurredFcn is not a timeout's
DVLOG = {};
tm = timer('ExecutionMode', 'fixedRate', 'Period', 0.1, 'TimerFcn', @(~, ~) dv_cblog([], 'tick'));
start(tm);
dp(s, sprintf('later 700 %s', dp_hex(1)));
read(s, 1, "uint8");
ix_chk('timer_during_read', numel(DVLOG) >= 4);
stop(tm);
delete(tm);
s.ErrorOccurredFcn = @(varargin) dv_cblog([], 'error');
DVLOG = {};
[~, w] = dv_warn(@() read(s, 1, "uint8"));
pause(0.2);
ix_chk('errfcn_on_timeout', numel(DVLOG));

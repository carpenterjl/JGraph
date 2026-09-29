% serial_pins_legacy.m -- serialport's pins and break, and the legacy surface it keeps from the
% serial object (device classes plan, stage D1): setRTS and setDTR seen by the peer through the
% null-modem wiring, getpinstatus reading the peer's pins, RTS under hardware flow control,
% serialbreak and its refusals; then the hidden methods (fopen, fclose, fprintf, fwrite, fread,
% fgetl, fgets, fscanf, scanstr, query, binblockread, binblockwrite, flushinput) and properties
% (BytesAvailable, PinStatus, the unsupported ones that warn).

s = dv_open();
ix_chk('status_open', dp(s, 'status'));
setRTS(s, false);
ix_chk('status_rts_off', dp(s, 'status'));
setDTR(s, false);
ix_chk('status_dtr_off', dp(s, 'status'));
setRTS(s, true);
setDTR(s, true);
ix_chk('status_both_on', dp(s, 'status'));
ix_chk('setrts_num', dv_err(@() setRTS(s, 0)));
ix_chk('setrts_str', dv_err(@() setRTS(s, "true")));
ix_chk('setrts_two', dv_err(@() setRTS(s, true, 2)));
ix_chk('setrts_none', dv_err(@() setRTS(s)));
ix_chk('setrts_vec', dv_err(@() setRTS(s, [1 0])));
ix_chk('setdtr_str', dv_err(@() setDTR(s, "on")));
ix_chk('status_unchanged', dp(s, 'status'));
dp(s, 'pins rts=1 dtr=0');
pause(0.2);
p = getpinstatus(s);
ix_chk('pins_class', class(p));
ix_chk('pins_fields', strjoin(fieldnames(p)', ','));
ix_chk('pins_far_rts', ix_show([p.ClearToSend p.DataSetReady p.CarrierDetect p.RingIndicator]));
dp(s, 'pins rts=0 dtr=1');
pause(0.2);
p = getpinstatus(s);
ix_chk('pins_far_dtr', ix_show([p.ClearToSend p.DataSetReady p.CarrierDetect p.RingIndicator]));
ix_chk('pins_arg', dv_err(@() getpinstatus(s, 1)));

% RTS belongs to the driver under hardware flow control (the peer raises its RTS first: with CTS
% low a write would never finish)
dp(s, 'pins rts=1 dtr=0');
pause(0.2);
s.FlowControl = "hardware";
ix_chk('status_hw_flow', dp(s, 'status'));
setRTS(s, false);
ix_chk('status_hw_flow_rts_off', dp(s, 'status'));
s.FlowControl = "none";
setRTS(s, true);

% break
t0 = tic;
serialbreak(s, 300);
ix_chk('break_blocks', toc(t0) >= 0.25);
pause(0.2);
ix_chk('status_after_break', dp(s, 'status'));
ix_chk('break_none', dv_err(@() serialbreak(s)));
ix_chk('break_neg', dv_err(@() serialbreak(s, -1)));
ix_chk('break_frac', dv_err(@() serialbreak(s, 1.5)));
ix_chk('break_str', dv_err(@() serialbreak(s, "10")));
ix_chk('break_two', dv_err(@() serialbreak(s, 10, 20)));
ix_chk('break_zero', dv_err(@() serialbreak(s, 0)));

% the legacy methods
dp(s, 'reset');
flush(s);
[~, w] = dv_warn(@() dv_call(@() fopen(s)));
ix_chk('fopen', w);
[~, w] = dv_warn(@() dv_call(@() fclose(s)));
ix_chk('fclose', w);
ix_chk('fclose_still_valid', isvalid(s));
fprintf(s, "abc");
ix_chk('fprintf', ix_show(dp(s, 'recv')));
fprintf(s, '%d-%s', 5);
ix_chk('fprintf_fmt', ix_show(dp(s, 'recv')));
fprintf(s, '%d\n', 42);
ix_chk('fprintf_fmt_newline', ix_show(dp(s, 'recv')));
fwrite(s, [1 2 3]);
ix_chk('fwrite', ix_show(dp(s, 'recv')));
fwrite(s, 258, 'uint16');
ix_chk('fwrite_prec', ix_show(dp(s, 'recv')));
fwrite(s, 3, 'short');
ix_chk('fwrite_short', ix_show(dp(s, 'recv')));
[~, w] = dv_warn(@() dv_call(@() fwrite(s, 1, 'uint8', 'async')));
ix_chk('fwrite_mode_warn', w);
ix_chk('fwrite_mode', ix_show(dp(s, 'recv')));
dp(s, sprintf('send %s', dp_hex(['line1' 10 'line2' 10 '12 34' 10 'xyz'])));
pause(0.3);
ix_chk('bytesavailable', s.BytesAvailable);
ix_chk('fgetl', ix_show(fgetl(s)));
[l, n] = fgets(s);
ix_chk('fgets', ix_show(double(l)));
ix_chk('fgets_count', n);
ix_chk('fscanf', ix_show(double(fscanf(s))));
ix_chk('fscanf_left', s.NumBytesAvailable);
flush(s);
dp(s, sprintf('send %s', dp_hex(['12 34' 10])));
pause(0.3);
ix_chk('fscanf_fmt', ix_show(fscanf(s, '%d')));
dp(s, sprintf('send %s', dp_hex(['a,b' 10])));
pause(0.3);
ix_chk('scanstr', ix_show(scanstr(s)));
dp(s, sprintf('send %s', dp_hex(1:4)));
pause(0.3);
ix_chk('fread', ix_show(fread(s, 2)));
ix_chk('fread_prec', ix_show(fread(s, 1, 'uint16')));
flush(s);
dp(s, 'echo on');
ix_chk('query', ix_show(double(query(s, "ping"))));
dp(s, 'echo off');
flush(s);
dp(s, sprintf('send %s', dp_hex('#13abc')));
pause(0.3);
[d, n] = binblockread(s);
ix_chk('binblockread', ix_show(d));
ix_chk('binblockread_count', n);
binblockwrite(s, 1:3);
ix_chk('binblockwrite', ix_show(dp(s, 'recv')));
dp(s, sprintf('send %s', dp_hex(1:4)));
pause(0.3);
flushinput(s);
ix_chk('flushinput', s.NumBytesAvailable);
flushoutput(s);
[~, w] = dv_warn(@() dv_call(@() record(s)));
ix_chk('record_warn', w);
[~, w] = dv_warn(@() dv_call(@() readasync(s)));
ix_chk('readasync_warn', w);

% the legacy properties
ix_chk('legacy_errorfcn', isempty(s.ErrorFcn));
ps = s.PinStatus;
ix_chk('legacy_pinstatus_fields', strjoin(fieldnames(ps)', ','));
ix_chk('legacy_inputbuffersize', s.InputBufferSize);
ix_chk('legacy_outputbuffersize', s.OutputBufferSize);
ix_chk('legacy_status', s.Status);
[v, w] = dv_warn(@() s.BytesToOutput);
ix_chk('legacy_bytestooutput', ix_show(v));
ix_chk('legacy_bytestooutput_warn', w);
[v, w] = dv_warn(@() s.RequestToSend);
ix_chk('legacy_rts_warn', w);
ix_chk('legacy_type', dv_err(@() s.Type));
ix_chk('legacy_hidden_listed', any(strcmp(properties(s), 'BytesAvailable')));
ix_chk('instrhwinfo_prop', instrhwinfo(s, "BaudRate"));

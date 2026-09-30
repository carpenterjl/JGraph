% visa_instr.m -- visadev on message-based instruments (device classes plan, stage D4): a HiSLIP
% instrument, TCPIP0::127.0.0.1::hislip0::INSTR, and a raw socket, TCPIP0::127.0.0.1::5025::SOCKET,
% each the peer engine in tools/devices/peer-sim answering *IDN?. What the constructor asks, the
% objects, reads that end at END (HiSLIP) or when nothing more is waiting (the socket), the
% terminator "off" that visalib.TCPIP starts with, EOIMode, visastatus, flush's device clear, and the
% refusals. R2025b records against NI-VISA; JGraph replays against jgraph.internal.visasim.

visa_peer();
r = "TCPIP0::127.0.0.1::hislip0::INSTR";
v = visadev(r);
ix_chk('ctor_sent', ix_show(dp(v, 'recv')));
v.Timeout = 1;

% the object
ix_chk('class', class(v));
ix_chk('properties', ix_show(properties(v)));
ix_chk('methods', ix_show(methods(v)));
props = {'ResourceName','Alias','Vendor','Model','SerialNumber','PreferredVisa','LANName', ...
    'InstrumentAddress','BoardIndex','ByteOrder','Timeout','Terminator','Tag'};
for k = 1:numel(props)
    ix_chk(['prop_' props{k}], ix_show(v.(props{k})));
end
ix_chk('prop_Type', char(v.Type));
ix_chk('prop_EOIMode', char(v.EOIMode));
ix_chk('prop_EOIMode_class', class(v.EOIMode));
ix_chk('eoi_logical', logical(v.EOIMode));
ix_chk('eoi_eq', v.EOIMode == "on");
ix_chk('eoi_eq_true', v.EOIMode == true);
ix_chk('hidden_KeepAlive', char(v.KeepAlive));
ix_chk('hidden_NoDelay', char(v.NoDelay));
ix_chk('hidden_ResourceClass', ix_show(v.ResourceClass));
[x, w] = dv_warn(@() v.EOSCharCode);
ix_chk('hidden_EOSCharCode', ix_show(x));
ix_chk('hidden_EOSCharCode_warn', w);

% reads end at a message's END
dp(v, 'reset');
dp(v, 'send 6869');
pause(0.3);
ix_chk('readline_end', ix_show(readline(v)));
dp(v, 'send 61620A6364');
pause(0.3);
ix_chk('readline_whole_message', ix_show(readline(v)));
dp(v, 'send 0102030405');
pause(0.3);
ix_chk('read2', ix_show(read(v, 2)));
ix_chk('read_rest3', ix_show(read(v, 3)));
dp(v, 'send 0102');
pause(0.3);
[x, w] = dv_warn(@() read(v, 5));
ix_chk('read_short', ix_show(x));
ix_chk('read_short_warn', w);
[x, w] = dv_warn(@() read(v, 2));
ix_chk('read_nothing', ix_show(x));
ix_chk('read_nothing_warn', w);
ix_chk('readline_nothing', dv_err(@() readline(v)));
ix_chk('writeread_idn', ix_show(writeread(v, "*IDN?")));
writeline(v, "*IDN?");
ix_chk('read_idn_head', ix_show(read(v, 5, "char")));
ix_chk('read_idn_rest', ix_show(readline(v)));
dp(v, 'send 0102');
dp(v, 'later 300 030405');
ix_chk('read_after_new_message', ix_show(read(v, 3)));
dp(v, 'send 010203');
pause(0.3);
ix_chk('read_odd_u16', dv_err(@() read(v, 2, "uint16")));
dp(v, 'reset');
write(v, 1:3);
ix_chk('write_recv', ix_show(dp(v, 'recv')));
writeline(v, "abc");
ix_chk('writeline_recv', ix_show(dp(v, 'recv')));

% terminators
configureTerminator(v, "CR");
ix_chk('term_cr', ix_show(v.Terminator));
dp(v, 'send 410D42');
pause(0.3);
ix_chk('readline_cr', ix_show(readline(v)));
[x, w] = dv_warn(@() readline(v));
ix_chk('readline_cr_rest', ix_show(x));
ix_chk('readline_cr_rest_warn', w);
configureTerminator(v, "off", "LF");
ix_chk('term_off', ix_show(v.Terminator));
dp(v, 'send 410A42');
pause(0.3);
ix_chk('readline_off_with_leftover', ix_show(readline(v)));
configureTerminator(v, "LF");
dp(v, 'send 410A420A');
pause(0.3);
ix_chk('readline_lf', ix_show(readline(v)));
ix_chk('readline_lf2', ix_show(readline(v)));
configureTerminator(v, "off", "LF");

% EOIMode
v.EOIMode = "off";
ix_chk('eoi_off', char(v.EOIMode));
dp(v, 'reset');
writeline(v, "x");
ix_chk('eoi_off_recv', ix_show(dp(v, 'recv')));
v.EOIMode = "of";
ix_chk('eoi_partial', char(v.EOIMode));
v.EOIMode = 2;
ix_chk('eoi_two', char(v.EOIMode));
v.EOIMode = false;
ix_chk('eoi_false', char(v.EOIMode));
ix_chk('eoi_bad', dv_err(@() set(v, 'EOIMode', "maybe")));
v.EOIMode = "on";

% status, clear, trigger
dp(v, 'stb 41');
[ready, status] = visastatus(v);
ix_chk('visastatus_ready', ready);
ix_chk('visastatus_status', ix_show(status));
ix_chk('visastatus_one', ix_show(visastatus(v)));
dp(v, 'stb 00');
[~, status] = visastatus(v);
ix_chk('visastatus_status0', ix_show(status));
ix_chk('visastatus_args', dv_err(@() visastatus(v, 1)));
dp(v, 'reset');
flush(v);
ix_chk('clear_after_flush', ix_show(dp(v, 'inst')));
flush(v, "input");
ix_chk('clear_after_flush_input', ix_show(dp(v, 'inst')));
clrdevice(v);
ix_chk('clear_after_clrdevice', ix_show(dp(v, 'inst')));
dp(v, 'send 0102');
pause(0.3);
flush(v);
[x, w] = dv_warn(@() read(v, 1));
ix_chk('read_after_clear', ix_show(x));
ix_chk('visatrigger', dv_err(@() visatrigger(v)));

% binblocks
dp(v, 'reset');
writebinblock(v, 1:3, "uint8");
ix_chk('binblock_recv', ix_show(dp(v, 'recv')));
dp(v, 'send 2332303301020341');
pause(0.3);
ix_chk('readbinblock', ix_show(readbinblock(v)));
ix_chk('readbinblock_after', ix_show(read(v, 1)));
dp(v, 'send 2331354142');
pause(0.3);
[x, w] = dv_warn(@() readbinblock(v));
ix_chk('readbinblock_short', ix_show(x));
ix_chk('readbinblock_short_warn', w);

% a second object, the timeout
ix_chk('second', dv_err(@() visadev(r)));
v.Timeout = 3.5;
ix_chk('timeout', ix_show(v.Timeout));
clear v

% the raw socket
s = visadev("TCPIP0::127.0.0.1::5025::SOCKET");
ix_chk('sock_ctor_sent', ix_show(dp(s, 'recv')));
s.Timeout = 1;
ix_chk('sock_class', class(s));
ix_chk('sock_properties', ix_show(properties(s)));
ix_chk('sock_methods', ix_show(methods(s)));
props = {'ResourceName','Alias','Vendor','Model','SerialNumber','IPAddress','Port','Terminator','Timeout'};
for k = 1:numel(props)
    ix_chk(['sock_prop_' props{k}], ix_show(s.(props{k})));
end
ix_chk('sock_prop_Type', char(s.Type));
ix_chk('sock_prop_EOIMode', char(s.EOIMode));
ix_chk('sock_writeread', ix_show(writeread(s, "*IDN?")));
dp(s, 'reset');
dp(s, 'send 61620A6364');
pause(0.3);
ix_chk('sock_readline', ix_show(readline(s)));
[x, w] = dv_warn(@() readline(s));
ix_chk('sock_readline_partial', ix_show(x));
ix_chk('sock_readline_partial_warn', w);
ix_chk('sock_readline_partial_kept', ix_show(read(s, 2)));
dp(s, 'send 0102');
pause(0.3);
[x, w] = dv_warn(@() read(s, 5));
ix_chk('sock_read_short', ix_show(x));
ix_chk('sock_read_short_warn', w);
[x, w] = dv_warn(@() read(s, 2));
ix_chk('sock_read_nothing', ix_show(x));
ix_chk('sock_read_nothing_warn', w);
ix_chk('sock_readline_nothing', dv_err(@() readline(s)));
ix_chk('sock_visastatus', dv_err(@() visastatus(s)));
ix_chk('sock_flush', dv_err(@() flush(s)));
ix_chk('sock_second', dv_err(@() visadev("TCPIP0::127.0.0.1::5025::SOCKET")));
clear s
s = visadev("TCPIP0::localhost::5025::SOCKET");
ix_chk('sock_localhost_address', ix_show(s.IPAddress));
clear s

% probe_visa_instr: visadev on a HiSLIP instrument (TCPIP0::127.0.0.1::hislip0::INSTR) and a raw
% socket (TCPIP0::127.0.0.1::5025::SOCKET), both the peer engine in tools/devices/peer-sim: what the
% constructor asks the instrument, the objects, END-terminated reads, EOIMode, status, clear, trigger.
visa_peer();
r = "TCPIP0::127.0.0.1::hislip0::INSTR";
t0 = tic;
v = visadev(r);
dv_pr('ctor_took', 'round(toc(t0), 1)');
dv_pr('ctor_sent', 'dp(v, ''recv'')');
dv_pr('class', 'class(v)');
dv_pr('superclasses', 'superclasses(v)');
dv_pr('properties', 'properties(v)');
dv_pr('methods', 'methods(v)');
dv_px('echo', 'v');
dv_px('get', 'get(v)');
props = {'ResourceName','Alias','Vendor','Model','SerialNumber','Type','PreferredVisa','LANName', ...
    'InstrumentAddress','BoardIndex','ByteOrder','Timeout','Terminator','EOIMode','NumBytesWritten', ...
    'ErrorOccurredFcn','UserData','Tag'};
for k = 1:numel(props)
    dv_pr(['prop_' props{k}], ['v.' props{k}]);
end
dv_pr('class_EOIMode', 'class(v.EOIMode)');
hidden = {'KeepAlive','NoDelay','Connected','ResourceClass','EOSMode','EOSCharCode','RsrcName','TransferSize'};
for k = 1:numel(hidden)
    dv_pr(['hidden_' hidden{k}], ['v.' hidden{k}]);
end
dv_pr('attr', 'dv_vattr(v)');

% reads end at END
dp(v, 'reset');
dp(v, 'send 6869');
pause(0.3);
dv_pr('readline_end', 'readline(v)');
dp(v, 'send 61620A6364');
pause(0.3);
dv_pr('readline_end_lf', 'readline(v)');
dp(v, 'send 0102030405');
pause(0.3);
dv_pr('read2', 'read(v, 2)');
dv_pr('read_rest3', 'read(v, 3)');
dp(v, 'send 0102');
pause(0.3);
t0 = tic;
lastwarn('');
dv_pr('read_short', 'read(v, 5)');
dv_pr('read_short_took', 'round(toc(t0), 1)');
[m, id] = lastwarn;
dv_pr('read_short_warn', 'id');
v.Timeout = 1;
t0 = tic;
lastwarn('');
dv_pr('read_nothing', 'read(v, 2)');
dv_pr('read_nothing_took', 'round(toc(t0), 1)');
[m, id] = lastwarn;
dv_pr('read_nothing_warn', 'id');
t0 = tic;
lastwarn('');
dv_pr('readline_nothing', 'readline(v)');
dv_pr('readline_nothing_took', 'round(toc(t0), 1)');
[m, id] = lastwarn;
dv_pr('readline_nothing_warn', 'id');
dv_pr('attr_after', 'dv_vattr(v)');
dv_pr('writeread_idn', 'writeread(v, "*IDN?")');
writeline(v, "*IDN?");
dv_pr('readline_idn', 'readline(v)');
writeline(v, "*IDN?");
dv_pr('read_idn', 'read(v, 5, "char")');
dv_pr('read_idn_rest', 'readline(v)');
dp(v, 'reset');
write(v, 1:3);
dv_pr('write_recv', 'dp(v, ''recv'')');
writeline(v, "abc");
dv_pr('writeline_recv', 'dp(v, ''recv'')');
configureTerminator(v, "CR");
dv_pr('term_cr', 'v.Terminator');
dv_pr('attr_cr', 'dv_vattr(v)');
dp(v, 'send 410D42');
pause(0.3);
dv_pr('readline_cr', 'readline(v)');
dv_pr('readline_cr2', 'readline(v)');
configureTerminator(v, "off", "LF");
dv_pr('term_off', 'v.Terminator');
dv_pr('attr_off', 'dv_vattr(v)');
dp(v, 'send 410A42');
pause(0.3);
dv_pr('readline_off', 'readline(v)');
configureTerminator(v, "LF");
dv_pr('attr_lf', 'dv_vattr(v)');
configureTerminator(v, "off", "LF");

% EOIMode
v.EOIMode = "off";
dv_pr('eoi_off', 'v.EOIMode');
dv_pr('attr_eoi_off', 'dv_vattr(v)');
dp(v, 'reset');
writeline(v, "x");
dv_pr('eoi_off_recv', 'dp(v, ''recv'')');
v.EOIMode = false;
dv_pr('eoi_false', 'char(v.EOIMode)');
v.EOIMode = 1;
dv_pr('eoi_one', 'char(v.EOIMode)');
dv_pr('eoi_bad', 'dv_err(@() set(v, ''EOIMode'', "maybe"))');
dv_pr('attr_eoi_on', 'dv_vattr(v)');
v.KeepAlive = "on";
dv_pr('keepalive_on', 'char(v.KeepAlive)');
v.NoDelay = "off";
dv_pr('nodelay_off', 'char(v.NoDelay)');

% status, clear, trigger
dp(v, 'stb 40');
dv_pr('visastatus1', 'visastatus(v)');
[ready, status] = visastatus(v);
dv_pr('visastatus_ready', 'ready');
dv_pr('visastatus_status', 'status');
dp(v, 'stb 00');
[ready, status] = visastatus(v);
dv_pr('visastatus_ready0', 'ready');
dv_pr('visastatus_status0', 'status');
dp(v, 'reset');
flush(v);
dv_pr('inst_after_flush', 'dp(v, ''inst'')');
flush(v, "input");
dv_pr('inst_after_flush_input', 'dp(v, ''inst'')');
dv_pr('clrdevice', 'dv_err(@() clrdevice(v))');
dv_pr('inst_after_clrdevice', 'dp(v, ''inst'')');
dv_pr('visatrigger', 'dv_err(@() visatrigger(v))');
dv_pr('trigger_legacy', 'dv_err(@() trigger(v))');
dv_pr('inst_after_trigger', 'dp(v, ''inst'')');
dp(v, 'send 0102');
pause(0.3);
flush(v);
dv_pr('after_flush_read', 'dv_err(@() read(v, 1))');

% binblock
dp(v, 'reset');
writebinblock(v, 1:3, "uint8");
dv_pr('binblock_recv', 'dp(v, ''recv'')');
dp(v, 'send 2332303301020341');
pause(0.3);
dv_pr('readbinblock', 'readbinblock(v)');
dv_pr('readbinblock_after', 'read(v, 1)');

% a second object on the same instrument, timeouts
dv_pr('second', 'visadev(r)');
v.Timeout = 3.5;
dv_pr('attr_t35', 'dv_vattr(v)');
dv_pr('timeout_t35', 'v.Timeout');
clear v

% the raw socket
s = visadev("TCPIP0::127.0.0.1::5025::SOCKET");
dv_pr('sock_ctor_sent', 'dp(s, ''recv'')');
dv_pr('sock_class', 'class(s)');
dv_pr('sock_properties', 'properties(s)');
dv_pr('sock_methods', 'methods(s)');
dv_px('sock_echo', 's');
dv_px('sock_get', 'get(s)');
props = {'ResourceName','Alias','Vendor','Model','SerialNumber','Type','IPAddress','Port','Terminator','EOIMode','Timeout'};
for k = 1:numel(props)
    dv_pr(['sock_prop_' props{k}], ['s.' props{k}]);
end
dv_pr('sock_class_Port', 'class(s.Port)');
dv_pr('sock_attr', 'dv_vattr(s)');
dv_pr('sock_writeread', 'writeread(s, "*IDN?")');
dp(s, 'reset');
dp(s, 'send 61620A6364');
pause(0.3);
dv_pr('sock_readline', 'readline(s)');
s.Timeout = 1;
t0 = tic;
dv_pr('sock_readline_partial', 'dv_err(@() readline(s))');
dv_pr('sock_readline_partial_took', 'round(toc(t0), 1)');
dv_pr('sock_visastatus', 'dv_err(@() visastatus(s))');
dv_pr('sock_flush', 'dv_err(@() flush(s))');
dv_pr('sock_inst', 'dp(s, ''inst'')');
dv_pr('sock_keepalive', 'char(s.KeepAlive)');
clear s

% the list with the instruments up, and with a named identification command
dv_pr('list', 'visadevlist("Timeout", 2)');
dv_pr('list_id', 'visadevlist("Timeout", 2, "Identification", ["TCPIP0::127.0.0.1::hislip0::INSTR" "*IDN?"])');
v = visadev("TCPIP0::127.0.0.1::hislip0::INSTR");
dv_pr('list_after_open', 'visadevlist("Timeout", 2)');
dv_pr('find_type', 'size(visadevfind("Type", "tcpip"))');
clear v
dv_pr('ctor_bad_port', 'dv_err(@() visadev("TCPIP0::127.0.0.1::hislip0,4999::INSTR"))');
dv_pr('ctor_lower', 'class(visadev("tcpip0::127.0.0.1::hislip0::instr"))');
dv_pr('ctor_no_board', 'class(visadev("TCPIP::127.0.0.1::hislip0::INSTR"))');

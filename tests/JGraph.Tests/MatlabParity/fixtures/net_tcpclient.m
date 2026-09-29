% net_tcpclient.m -- tcpclient against echotcpip on the loopback (device classes plan, stage D2):
% the object, its properties and setters, reads (the precision's class, read(t) for what is waiting,
% no partial reads: a timeout is an error), writes in the data's own class, lines, binblocks,
% writeread, the byte callback and its ByteAvailableInfo, tcpclientfind, and the constructor's
% refusals. The port is one the OS hands a fresh udpport, so concurrent runs do not collide.

global DVLOG
u0 = udpport; port = u0.LocalPort; delete(u0); clear u0
echotcpip("on", port);
t = tcpclient("127.0.0.1", port, "Timeout", 1);

% the object
ix_chk('class', class(t));
ix_chk('isa_handle', isa(t, 'handle'));
ix_chk('properties', ix_show(properties(t)));
ix_chk('methods', ix_show(methods(t)));
ix_chk('prop_Address', ix_show(t.Address));
ix_chk('prop_Port', t.Port == port);
props = {'NumBytesAvailable','NumBytesWritten','ConnectTimeout','EnableTransferDelay','Timeout','UserData', ...
    'ByteOrder','Terminator','BytesAvailableFcnCount','BytesAvailableFcnMode','Tag'};
for k = 1:numel(props)
    ix_chk(['prop_' props{k}], ix_show(t.(props{k})));
end
% an unset callback is R2025b's 0x0 function_handle, JGraph's [] (div=ADR0185)
ix_chk('prop_BytesAvailableFcn_empty', isempty(t.BytesAvailableFcn));
ix_chk('prop_ErrorOccurredFcn_empty', isempty(t.ErrorOccurredFcn));
ix_chk('prop_BytesAvailableFcn_class', class(t.BytesAvailableFcn), 'div=ADR0185');

% reads and writes
write(t, 1:5);
pause(0.3);
ix_chk('written_double', ix_show(t.NumBytesWritten));
ix_chk('echoed_nba', ix_show(t.NumBytesAvailable));
ix_chk('read_all', ix_show(read(t)));
write(t, 1:5, "uint8");
pause(0.3);
ix_chk('read_count', ix_show(read(t, 2)));
ix_chk('read_count_prec', ix_show(read(t, 1, "uint16")));
ix_chk('read_rest', ix_show(read(t, t.NumBytesAvailable, "uint8")));
ix_chk('read_timeout', dv_err(@() read(t, 3)));
write(t, 1:2, "uint8");
pause(0.3);
ix_chk('read_short', dv_err(@() read(t, 5)));
ix_chk('read_short_left', ix_show(t.NumBytesAvailable));
flush(t);
ix_chk('read_empty', ix_show(read(t)));
ix_chk('read_zero', dv_err(@() read(t, 0)));
ix_chk('read_four_args', dv_err(@() read(t, 1, "uint8", 3)));
ix_chk('read_bad_prec', dv_err(@() read(t, 1, "uint9")));
ix_chk('write_none', dv_err(@() write(t)));
ix_chk('write_logical', dv_err(@() write(t, true)));
write(t, 'AZ');
write(t, int16(-2));
pause(0.3);
ix_chk('write_classes_back', ix_show(read(t)));
write(t, [1 2], "int32");
pause(0.3);
ix_chk('read_int32', ix_show(read(t, 2, "int32")));
t.ByteOrder = "big-endian";
write(t, 258, "uint16");
pause(0.3);
ix_chk('read_be', ix_show(read(t, 2, "uint8")));
t.ByteOrder = "little-endian";

% lines, writeread, binblocks
writeline(t, "hi");
pause(0.3);
ix_chk('readline', ix_show(readline(t)));
[v, w] = dv_warn(@() readline(t));
ix_chk('readline_timeout', ix_show(v));
ix_chk('readline_timeout_warn', w);
ix_chk('writeread', ix_show(writeread(t, "abc")));
configureTerminator(t, "CR/LF");
ix_chk('terminator_crlf', ix_show(t.Terminator));
writeline(t, "x");
pause(0.3);
ix_chk('crlf_bytes', ix_show(read(t, 3, "uint8")));
configureTerminator(t, "LF");
writebinblock(t, 1:3, "uint8");
pause(0.3);
ix_chk('binblock_roundtrip', ix_show(readbinblock(t)));
writebinblock(t, [1 2], "uint16");
pause(0.3);
ix_chk('binblock_uint16', ix_show(readbinblock(t, "uint16")));

% setters
t.Timeout = 2;
ix_chk('timeout_set', ix_show(t.Timeout));
ix_chk('timeout_bad', dv_err(@() set(t, 'Timeout', -1)));
ix_chk('timeout_int', dv_err(@() set(t, 'Timeout', int32(2))));
ix_chk('connecttimeout_set', dv_err(@() set(t, 'ConnectTimeout', 3)));
ix_chk('transferdelay_set', dv_err(@() set(t, 'EnableTransferDelay', false)));
ix_chk('address_set', dv_err(@() set(t, 'Address', "x")));
ix_chk('byteorder_bad', dv_err(@() set(t, 'ByteOrder', "x")));
ix_chk('terminator_set', dv_err(@() set(t, 'Terminator', "CR")));
ix_chk('bafm_set', dv_err(@() set(t, 'BytesAvailableFcnMode', "byte")));
ix_chk('bafc_set', dv_err(@() set(t, 'BytesAvailableFcnCount', 5)));
ix_chk('baf_set', dv_err(@() set(t, 'BytesAvailableFcn', @disp)));
t.Tag = "x";
ix_chk('tag_set', ix_show(t.Tag));
t.UserData = {1, 'a'};
ix_chk('userdata_set', ix_show(t.UserData));

% the byte callback
DVLOG = {};
configureCallback(t, "byte", 2, @dv_netevt);
write(t, 1:4, "uint8");
pause(0.5);
ix_chk('cb_calls', numel(DVLOG) / 5);
ix_chk('cb_first', ix_show(DVLOG(1:min(5, end))));
configureCallback(t, "off");
flush(t);
DVLOG = {};
configureCallback(t, "terminator", @dv_netevt);
writeline(t, "a");
writeline(t, "b");
pause(0.5);
ix_chk('cb_term_calls', numel(DVLOG) / 5);
ix_chk('cb_term_first', ix_show(DVLOG(1:min(5, end))));
configureCallback(t, "off");
flush(t);

% find
ix_chk('find_class', class(tcpclientfind));
ix_chk('find_tag', class(tcpclientfind("Tag", "x")));
ix_chk('find_none', ix_show(tcpclientfind("Tag", "nope")));
ix_chk('find_odd', dv_err(@() tcpclientfind("Tag")));

% the constructor
ix_chk('ctor_none', dv_err(@() tcpclient));
ix_chk('ctor_one', dv_err(@() tcpclient("127.0.0.1")));
ix_chk('ctor_badport', dv_err(@() tcpclient("127.0.0.1", 0)));
ix_chk('ctor_bigport', dv_err(@() tcpclient("127.0.0.1", 70000)));
ix_chk('ctor_strport', dv_err(@() tcpclient("127.0.0.1", "80")));
ix_chk('ctor_numaddr', dv_err(@() tcpclient(127, 80)));
ix_chk('ctor_emptyaddr', dv_err(@() tcpclient("", 80)));
ix_chk('ctor_odd', dv_err(@() tcpclient("127.0.0.1", port, "Timeout")));
ix_chk('ctor_bad_nv', dv_err(@() tcpclient("127.0.0.1", port, "Bogus", 1)));
ix_chk('ctor_bad_timeout', dv_err(@() tcpclient("127.0.0.1", port, "Timeout", "x")));
ix_chk('ctor_partial_name', ix_show(get(tcpclient("127.0.0.1", port, "Time", 3), 'Timeout')));
ix_chk('ctor_transferdelay', ix_show(get(tcpclient("127.0.0.1", port, "EnableTransferDelay", false), 'EnableTransferDelay')));
ix_chk('ctor_transferdelay_bad', dv_err(@() tcpclient("127.0.0.1", port, "EnableTransferDelay", 1)));
ix_chk('ctor_connecttimeout', ix_show(get(tcpclient("127.0.0.1", port, "ConnectTimeout", 5), 'ConnectTimeout')));
ix_chk('ctor_byteorder', ix_show(get(tcpclient("127.0.0.1", port, "ByteOrder", "big"), 'ByteOrder')));
ix_chk('ctor_localhost', ix_show(get(tcpclient("localhost", port), 'Address')));
clear t
echotcpip("off");
ix_chk('after_echo_off', ix_id(@() tcpclient("127.0.0.1", port, "ConnectTimeout", 1)));

% probe_net_tcp: tcpclient and tcpserver on the loopback -- objects, properties, refusals, reads with
% no partial reads, write without a precision, callbacks, connection events, echotcpip.
port = 50231;
dv_pr('echo_on_noport', 'echotcpip("on")');
dv_px('echo_none', 'echotcpip');
dv_px('echo_bad', 'echotcpip("maybe", 5)');
dv_px('echo_off_port', 'echotcpip("off", 5)');
echotcpip("on", port);
t = tcpclient("127.0.0.1", port, "Timeout", 1);
dv_pr('class', 'class(t)');
dv_pr('isa_handle', 'isa(t, ''handle'')');
dv_pr('properties', 'properties(t)');
dv_pr('methods', 'methods(t)');
props = {'Address','Port','NumBytesAvailable','NumBytesWritten','ConnectTimeout','EnableTransferDelay', ...
    'Timeout','UserData','ByteOrder','Terminator','BytesAvailableFcn','BytesAvailableFcnCount', ...
    'BytesAvailableFcnMode','ErrorOccurredFcn','Tag'};
for k = 1:numel(props)
    dv_pr(['prop_' props{k}], ['t.' props{k}]);
end
dv_px('echo', 't');
write(t, 1:5);
pause(0.3);
dv_pr('echoed_nba', 't.NumBytesAvailable');
dv_pr('read_all', 'read(t)');
write(t, 1:5, "uint8");
pause(0.3);
dv_pr('read_count', 'read(t, 2)');
dv_pr('read_count_prec', 'read(t, 1, "uint16")');
dv_pr('read_rest', 'read(t, t.NumBytesAvailable, "uint8")');
lastwarn('');
t0 = tic;
dv_pr('read_timeout', 'read(t, 3)');
dv_pr('read_timeout_took', 'round(toc(t0))');
[m, id] = lastwarn;
dv_pr('read_timeout_warn', 'id');
write(t, 1:2);
pause(0.3);
dv_pr('read_partial', 'read(t, 5)');
dv_pr('read_partial_left', 't.NumBytesAvailable');
flush(t);
dv_pr('read_empty', 'read(t)');
dv_pr('read_none_arg', 'read(t, 0)');
dv_pr('read_too_many', 'read(t, 1, "uint8", 3)');
dv_pr('read_bad_prec', 'read(t, 1, "uint9")');
dv_pr('write_none', 'write(t)');
dv_px('write_logical', 'write(t, true)');
dv_px('write_char', 'write(t, ''AZ'')');
pause(0.3);
dv_pr('write_char_back', 'read(t)');
writeline(t, "hi");
pause(0.3);
dv_pr('readline', 'readline(t)');
lastwarn('');
dv_pr('readline_timeout', 'readline(t)');
[m, id] = lastwarn;
dv_pr('readline_timeout_warn', 'sprintf(''%s ## %s'', id, m)');
dv_pr('writeread', 'writeread(t, "abc")');
writebinblock(t, 1:3, "uint8");
pause(0.3);
dv_pr('binblock_roundtrip', 'readbinblock(t)');
dv_px('timeout_set', 't.Timeout = 2');
dv_px('timeout_bad', 't.Timeout = -1');
dv_px('timeout_int', 't.Timeout = int32(2)');
dv_px('connecttimeout_set', 't.ConnectTimeout = 3');
dv_px('transferdelay_set', 't.EnableTransferDelay = false');
dv_px('address_set', 't.Address = "x"');
dv_px('byteorder_bad', 't.ByteOrder = "x"');
dv_px('terminator_set', 't.Terminator = "CR"');
dv_px('bafm_set', 't.BytesAvailableFcnMode = "byte"');
dv_pr('bafm_after', 't.BytesAvailableFcnMode');
dv_px('bafc_set', 't.BytesAvailableFcnCount = 5');
dv_px('baf_set', 't.BytesAvailableFcn = @disp');
dv_px('tag_set', 't.Tag = "x"');
% callbacks
global CBLOG
CBLOG = {};
configureCallback(t, "byte", 2, @(src, evt) sp_cbevtlog(src, evt));
write(t, 1:4);
pause(0.5);
dv_pr('cb_log', 'CBLOG');
configureCallback(t, "off");
flush(t);
dv_pr('find', 'class(tcpclientfind)');
dv_pr('find_tag', 'tcpclientfind("Tag", "x")');
% ctor refusals
dv_pr('ctor_none', 'tcpclient');
dv_pr('ctor_one', 'tcpclient("127.0.0.1")');
dv_pr('ctor_badport', 'tcpclient("127.0.0.1", 0)');
dv_pr('ctor_bigport', 'tcpclient("127.0.0.1", 70000)');
dv_pr('ctor_strport', 'tcpclient("127.0.0.1", "80")');
dv_pr('ctor_numaddr', 'tcpclient(127, 80)');
dv_pr('ctor_emptyaddr', 'tcpclient("", 80)');
dv_pr('ctor_refused', 'tcpclient("127.0.0.1", 1)');
dv_pr('ctor_odd', 'tcpclient("127.0.0.1", port, "Timeout")');
dv_pr('ctor_bad_nv', 'tcpclient("127.0.0.1", port, "Bogus", 1)');
dv_pr('ctor_bad_timeout', 'tcpclient("127.0.0.1", port, "Timeout", "x")');
dv_pr('ctor_badhost', 'tcpclient("no.such.host.invalid", 80, "ConnectTimeout", 2)');
dv_pr('ctor_partial', 'get(tcpclient("127.0.0.1", port, "Time", 3), ''Timeout'')');
dv_pr('ctor_transferdelay', 'get(tcpclient("127.0.0.1", port, "EnableTransferDelay", false), ''EnableTransferDelay'')');
dv_pr('ctor_transferdelay_bad', 'tcpclient("127.0.0.1", port, "EnableTransferDelay", 1)');
dv_pr('ctor_connecttimeout', 'get(tcpclient("127.0.0.1", port, "ConnectTimeout", 5), ''ConnectTimeout'')');
dv_pr('ctor_localhost', 'get(tcpclient("localhost", port), ''Address'')');
clear t
echotcpip("off");
dv_pr('after_echo_off', 'tcpclient("127.0.0.1", port, "ConnectTimeout", 1)');
% tcpserver
dv_pr('server_none', 'tcpserver');
dv_pr('server_zero', 'tcpserver(0)');
dv_pr('server_odd', 'tcpserver(port, "Timeout")');
dv_pr('server_bad_nv', 'tcpserver(port, "Bogus", 1)');
dv_pr('server_num_str', 'tcpserver(5, "a")');
srv = tcpserver("127.0.0.1", port, "Timeout", 1);
dv_pr('server_class', 'class(srv)');
dv_pr('server_properties', 'properties(srv)');
dv_pr('server_methods', 'methods(srv)');
sprops = {'ServerAddress','ServerPort','Connected','ClientAddress','ClientPort','NumBytesAvailable', ...
    'NumBytesWritten','Timeout','ByteOrder','Terminator','BytesAvailableFcnMode','BytesAvailableFcnCount', ...
    'BytesAvailableFcn','ErrorOccurredFcn','UserData','ConnectionChangedFcn','Tag'};
for k = 1:numel(sprops)
    dv_pr(['sprop_' sprops{k}], ['srv.' sprops{k}]);
end
dv_px('server_write_unconnected', 'write(srv, 1)');
dv_pr('server_read_unconnected', 'read(srv, 1)');
CBLOG = {};
srv.ConnectionChangedFcn = @(src, evt) sp_cbevtlog(src, evt);
c = tcpclient("127.0.0.1", port);
pause(0.5);
dv_pr('server_connected', 'srv.Connected');
dv_pr('server_clientaddr', 'srv.ClientAddress');
dv_pr('server_clientport_matches', 'srv.ClientPort == c.Port');
dv_pr('server_conn_log', 'CBLOG');
write(c, 1:3);
pause(0.3);
dv_pr('server_read', 'read(srv, 3)');
write(srv, 4:6);
pause(0.3);
dv_pr('client_read', 'read(c, 3)');
dv_pr('server_second_client', 'tcpclient("127.0.0.1", port, "ConnectTimeout", 1)');
CBLOG = {};
clear c
pause(0.5);
dv_pr('server_after_disconnect', 'srv.Connected');
dv_pr('server_disc_log', 'CBLOG');
dv_pr('server_find', 'class(tcpserverfind)');
dv_px('server_echo', 'srv');
clear srv

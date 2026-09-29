% net_tcpserver.m -- tcpserver on the loopback (device classes plan, stage D2): the object and its
% properties before a client, the refusals of a write with no client, a client connecting and
% leaving (ConnectionChangedFcn with its ConnectionInfo), reads in double, writes back to the client,
% a second client refused, tcpserverfind, and the constructor's refusals.

global DVLOG
u0 = udpport; port = u0.LocalPort; delete(u0); clear u0

% the constructor's refusals
ix_chk('ctor_none', dv_err(@() tcpserver));
ix_chk('ctor_zero', dv_err(@() tcpserver(0)));
ix_chk('ctor_odd', dv_err(@() tcpserver(port, "Timeout")));
ix_chk('ctor_bad_nv', dv_err(@() tcpserver(port, "Bogus", 1)));
ix_chk('ctor_num_str', dv_err(@() tcpserver(5, "a")));

srv = tcpserver("127.0.0.1", port, "Timeout", 1);
ix_chk('class', class(srv));
ix_chk('properties', ix_show(properties(srv)));
ix_chk('methods', ix_show(methods(srv)));
ix_chk('prop_ServerPort', srv.ServerPort == port);
props = {'ServerAddress','Connected','ClientAddress','ClientPort','NumBytesAvailable','NumBytesWritten', ...
    'Timeout','ByteOrder','Terminator','BytesAvailableFcnMode','BytesAvailableFcnCount', ...
    'UserData','Tag'};
for k = 1:numel(props)
    ix_chk(['prop_' props{k}], ix_show(srv.(props{k})));
end
% an unset callback is R2025b's 0x0 function_handle, JGraph's [] (div=ADR0185)
ix_chk('prop_BytesAvailableFcn_empty', isempty(srv.BytesAvailableFcn));
ix_chk('prop_ErrorOccurredFcn_empty', isempty(srv.ErrorOccurredFcn));
ix_chk('prop_ConnectionChangedFcn_empty', isempty(srv.ConnectionChangedFcn));
ix_chk('prop_BytesAvailableFcn_class', class(srv.BytesAvailableFcn), 'div=ADR0185');
ix_chk('write_unconnected', dv_err(@() write(srv, 1)));
ix_chk('writeline_unconnected', dv_err(@() writeline(srv, "a")));
[v, w] = dv_warn(@() read(srv, 1));
ix_chk('read_unconnected', ix_show(v));
ix_chk('read_unconnected_warn', w);
ix_chk('server_port_set', dv_err(@() set(srv, 'ServerPort', 5)));
ix_chk('connected_set', dv_err(@() set(srv, 'Connected', true)));

% a client comes
DVLOG = {};
srv.ConnectionChangedFcn = @dv_netevt;
c = tcpclient("127.0.0.1", port, "Timeout", 1);
pause(0.5);
ix_chk('connected', ix_show(srv.Connected));
ix_chk('client_address', ix_show(srv.ClientAddress));
ix_chk('client_port', srv.ClientPort == c.Port);
ix_chk('conn_log', ix_show(DVLOG));
write(c, 1:3, "uint8");
pause(0.3);
ix_chk('server_nba', ix_show(srv.NumBytesAvailable));
ix_chk('server_read', ix_show(read(srv, 3)));
write(c, 1:3, "uint8");
pause(0.3);
ix_chk('server_read_uint8', ix_show(read(srv, 3, "uint8")));
write(srv, 4:6);
pause(0.3);
ix_chk('client_read', ix_show(read(c, c.NumBytesAvailable)));
writeline(srv, "hello");
pause(0.3);
ix_chk('client_readline', ix_show(readline(c)));
writeline(c, "back");
pause(0.3);
ix_chk('server_readline', ix_show(readline(srv)));
ix_chk('server_written', ix_show(srv.NumBytesWritten));
ix_chk('second_client', ix_id(@() tcpclient("127.0.0.1", port, "ConnectTimeout", 1)));

% the client leaves
DVLOG = {};
clear c
pause(0.5);
ix_chk('after_disconnect', ix_show(srv.Connected));
ix_chk('after_disconnect_addr', ix_show(srv.ClientAddress));
ix_chk('after_disconnect_port', ix_show(srv.ClientPort));
ix_chk('disc_log', ix_show(DVLOG));

% another client after the first
c2 = tcpclient("127.0.0.1", port, "Timeout", 1);
pause(0.5);
ix_chk('reconnected', ix_show(srv.Connected));
clear c2
pause(0.3);

srv.Tag = "srv";
ix_chk('find_class', class(tcpserverfind));
ix_chk('find_tag', class(tcpserverfind("Tag", "srv")));
ix_chk('find_none', ix_show(tcpserverfind("Tag", "nope")));
clear srv
ix_chk('find_after_clear', ix_show(tcpserverfind));

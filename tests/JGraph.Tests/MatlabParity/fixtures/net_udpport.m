% net_udpport.m -- udpport in byte mode against echoudp on the loopback (device classes plan, stage
% D2): the object and its properties; reads in double through the shared client, with partial reads
% and the transport's warning; writes that must name a destination until one has been named, and
% then reuse it; OutputDatagramSize splitting a write; the setters' refusals; multicast; the byte
% callback; udpportfind; and the constructor's forms and refusals. Ports come from the OS.

global DVLOG
u0 = udpport; port = u0.LocalPort; delete(u0); clear u0
echoudp("on", port);
u = udpport("Timeout", 1);

% the object
ix_chk('class', class(u));
ix_chk('properties', ix_show(properties(u)));
ix_chk('methods', ix_show(methods(u)));
props = {'IPAddressVersion','LocalHost','Tag','NumBytesAvailable','ByteOrder','Timeout','Terminator', ...
    'EnablePortSharing','EnableBroadcast','EnableMulticast','EnableMulticastLoopback','MulticastGroup', ...
    'BytesAvailableFcnMode','BytesAvailableFcnCount','OutputDatagramSize','NumBytesWritten', ...
    'UserData'};
for k = 1:numel(props)
    ix_chk(['prop_' props{k}], ix_show(u.(props{k})));
end
% an unset callback is R2025b's 0x0 function_handle, JGraph's [] (div=ADR0185)
ix_chk('prop_BytesAvailableFcn_empty', isempty(u.BytesAvailableFcn));
ix_chk('prop_ErrorOccurredFcn_empty', isempty(u.ErrorOccurredFcn));
ix_chk('prop_BytesAvailableFcn_class', class(u.BytesAvailableFcn), 'div=ADR0185');
ix_chk('prop_LocalPort', u.LocalPort > 0);

% writes before and after a destination
ix_chk('write_nodest', dv_err(@() write(u, 1:3)));
ix_chk('write_nodest_prec', dv_err(@() write(u, 1:3, "uint8")));
ix_chk('writeline_nodest', dv_err(@() writeline(u, "hi")));
ix_chk('write_none', dv_err(@() write(u)));
ix_chk('write_five', dv_err(@() write(u, 1:3, "uint8", "127.0.0.1", port, 1)));
ix_chk('writeline_two', dv_err(@() writeline(u, "a", "127.0.0.1")));
ix_chk('write_badhost', dv_err(@() write(u, 1, "uint8", "no.such.host.invalid", 5)));
ix_chk('write_prec_as_host', dv_err(@() write(u, 1, "uint8", "127.0.0.1")));
ix_chk('write_portstr', dv_err(@() write(u, 1:3, "127.0.0.1", "50254")));
ix_chk('write_portbig', dv_err(@() write(u, 1:3, "127.0.0.1", 70000)));
write(u, 1:5, "uint8", "127.0.0.1", port);
pause(0.3);
ix_chk('echoed_nba', ix_show(u.NumBytesAvailable));
ix_chk('read', ix_show(read(u, 5)));
ix_chk('read_zero', dv_err(@() read(u, 0)));
ix_chk('read_none', dv_err(@() read(u)));
write(u, 1:5, "uint8", "127.0.0.1", port);
pause(0.3);
ix_chk('read_prec', ix_show(read(u, 2, "uint16")));
ix_chk('read_rest', ix_show(read(u, u.NumBytesAvailable, "uint8")));
[v, w] = dv_warn(@() read(u, 3));
ix_chk('read_timeout', ix_show(v));
ix_chk('read_timeout_warn', w);
write(u, 7, "uint8");
write(u, 1:2);
pause(0.3);
ix_chk('kept_dest', ix_show(read(u, u.NumBytesAvailable)));
write(u, 1:2, "127.0.0.1", port);
pause(0.3);
ix_chk('data_dest', ix_show(read(u, u.NumBytesAvailable)));
[v, w] = dv_warn(@() read(u, 4));
write(u, 9, "uint8");
pause(0.3);
ix_chk('partial_timeout', ix_show(v));
ix_chk('partial_timeout_warn', w);
[v, w] = dv_warn(@() read(u, 3));
ix_chk('partial_some', ix_show(v));
ix_chk('partial_some_warn', w);
writeline(u, "hi", "127.0.0.1", port);
pause(0.3);
ix_chk('readline', ix_show(readline(u)));
writeline(u, "x");
pause(0.3);
ix_chk('readline_kept', ix_show(readline(u)));
ix_chk('written', ix_show(u.NumBytesWritten));
u.OutputDatagramSize = 2;
write(u, 1:5, "uint8");
pause(0.3);
ix_chk('split_back', ix_show(read(u, u.NumBytesAvailable, "uint8")));

% setters
ix_chk('outputsize_bad', dv_err(@() set(u, 'OutputDatagramSize', 70000)));
ix_chk('localport_set', dv_err(@() set(u, 'LocalPort', 5)));
ix_chk('localhost_set', dv_err(@() set(u, 'LocalHost', "1.2.3.4")));
ix_chk('version_set', dv_err(@() set(u, 'IPAddressVersion', "IPV6")));
ix_chk('sharing_set', dv_err(@() set(u, 'EnablePortSharing', true)));
ix_chk('terminator_set', dv_err(@() set(u, 'Terminator', "CR")));
ix_chk('group_set', dv_err(@() set(u, 'MulticastGroup', "239.1.1.1")));
ix_chk('enable_mc_set', dv_err(@() set(u, 'EnableMulticast', true)));
ix_chk('timeout_bad', dv_err(@() set(u, 'Timeout', -1)));
ix_chk('timeout_int', dv_err(@() set(u, 'Timeout', int32(2))));
u.EnableBroadcast = true;
ix_chk('broadcast_after', ix_show(u.EnableBroadcast));
ix_chk('broadcast_bad', dv_err(@() set(u, 'EnableBroadcast', 5)));

% multicast
configureMulticast(u, "226.0.0.1");
ix_chk('mc_group', ix_show(u.MulticastGroup));
ix_chk('mc_enabled', ix_show(u.EnableMulticast));
ix_chk('mc_loop', ix_show(u.EnableMulticastLoopback));
configureMulticast(u, "226.0.0.1", false);
ix_chk('mc_loop_off', ix_show(u.EnableMulticastLoopback));
configureMulticast(u, "off");
ix_chk('mc_group_off', ix_show(u.MulticastGroup));
ix_chk('mc_enabled_off', ix_show(u.EnableMulticast));
ix_chk('mc_bad', dv_err(@() configureMulticast(u, "10.0.0.1")));
ix_chk('mc_none', dv_err(@() configureMulticast(u)));
ix_chk('mc_num', dv_err(@() configureMulticast(u, 5)));
ix_chk('mc_loop_num', dv_err(@() configureMulticast(u, "239.1.1.1", 1)));

% the byte callback
u.OutputDatagramSize = 512;
DVLOG = {};
configureCallback(u, "byte", 3, @dv_netevt);
write(u, 1:6, "uint8");
pause(0.5);
ix_chk('cb_calls', numel(DVLOG) / 5);
ix_chk('cb_first', ix_show(DVLOG(1:min(5, end))));
configureCallback(u, "off");
flush(u);
ix_chk('flush_bad', dv_err(@() flush(u, "sideways")));

u.Tag = "udp";
ix_chk('find_class', class(udpportfind));
ix_chk('find_tag', class(udpportfind("Tag", "udp")));
ix_chk('find_none', ix_show(udpportfind("Tag", "nope")));
clear u

% the constructor
ix_chk('ctor_ipv6', ix_show(get(udpport("IPV6"), 'IPAddressVersion')));
ix_chk('ctor_byte_ipv4', class(udpport("byte", "IPV4")));
ix_chk('ctor_bogus', dv_err(@() udpport("bogus")));
ix_chk('ctor_second_bad', dv_err(@() udpport("byte", 5)));
ix_chk('ctor_odd', dv_err(@() udpport("LocalPort")));
ix_chk('ctor_bad_nv', dv_err(@() udpport("byte", "IPV4", "Bogus", 1)));
ix_chk('ctor_bad_port', dv_err(@() udpport("LocalPort", 70000)));
ix_chk('ctor_nv_first', ix_show(get(udpport("Timeout", 3), 'Timeout')));
ix_chk('ctor_nv_second', ix_show(get(udpport("byte", "Timeout", 3), 'Timeout')));
ix_chk('ctor_port_used', class(udpport("LocalPort", port)));
ix_chk('ctor_port_shared', ix_show(get(udpport("LocalPort", port, "EnablePortSharing", true), 'EnablePortSharing')));
ix_chk('ctor_localhost', ix_show(get(udpport("LocalHost", "127.0.0.1"), 'LocalHost')));
ix_chk('ctor_outsize', ix_show(get(udpport("OutputDatagramSize", 100), 'OutputDatagramSize')));
ix_chk('ctor_tag', ix_show(get(udpport("Tag", "t"), 'Tag')));
echoudp("off");

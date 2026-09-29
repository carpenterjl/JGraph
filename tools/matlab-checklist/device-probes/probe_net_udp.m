% probe_net_udp: udpport in byte and datagram modes on the loopback -- objects, properties,
% refusals, write to an address, reads, callbacks, multicast, echoudp, resolvehost.
global CBLOG
dv_pr('echoudp_none', 'dv_call(@() echoudp)');
dv_px('echoudp_bad', 'echoudp("maybe", 5)');
echoudp("on", 50240);
u = udpport("LocalPort", 50241, "Timeout", 1);
dv_pr('class', 'class(u)');
dv_pr('properties', 'properties(u)');
dv_pr('methods', 'methods(u)');
props = {'IPAddressVersion','LocalHost','LocalPort','Tag','NumBytesAvailable','ByteOrder','Timeout','Terminator', ...
    'EnablePortSharing','EnableBroadcast','EnableMulticast','EnableMulticastLoopback','MulticastGroup', ...
    'BytesAvailableFcnMode','BytesAvailableFcnCount','BytesAvailableFcn','OutputDatagramSize','NumBytesWritten', ...
    'ErrorOccurredFcn','UserData'};
for k = 1:numel(props)
    dv_pr(['prop_' props{k}], ['u.' props{k}]);
end
dv_px('echo', 'u');
write(u, 1:5, "uint8", "127.0.0.1", 50240);
pause(0.3);
dv_pr('echoed_nba', 'u.NumBytesAvailable');
dv_pr('read', 'read(u, 5)');
dv_pr('read_class', 'class(read(u, 0))');
write(u, 1:5, "uint8", "127.0.0.1", 50240);
pause(0.3);
dv_pr('read_prec', 'read(u, 2, "uint16")');
dv_pr('read_rest', 'read(u, u.NumBytesAvailable, "uint8")');
lastwarn('');
dv_pr('read_timeout', 'read(u, 3)');
[m, id] = lastwarn;
dv_pr('read_timeout_warn', 'sprintf(''%s ## %s'', id, m)');
dv_pr('read_all_none', 'read(u)');
dv_px('write_no_dest', 'write(u, 1:3)');
dv_px('write_no_dest_prec', 'write(u, 1:3, "uint8")');
dv_px('write_after_dest', 'write(u, 7, "uint8")');
pause(0.3);
dv_pr('write_after_dest_back', 'read(u, u.NumBytesAvailable, "uint8")');
dv_px('write_bad_port', 'write(u, 1, "uint8", "127.0.0.1", 0)');
dv_px('write_bad_host', 'write(u, 1, "uint8", "no.such.host.invalid", 5)');
dv_px('write_nargs', 'write(u, 1, "uint8", "127.0.0.1")');
dv_px('write_data_dest', 'write(u, 1:2, "127.0.0.1", 50240)');
pause(0.3);
dv_pr('write_data_dest_back', 'read(u, u.NumBytesAvailable)');
writeline(u, "hi", "127.0.0.1", 50240);
pause(0.3);
dv_pr('readline', 'readline(u)');
dv_px('writeline_nodest_ok', 'writeline(u, "x")');
pause(0.3);
flush(u);
dv_px('outputsize', 'u.OutputDatagramSize = 2');
write(u, 1:5, "uint8", "127.0.0.1", 50240);
pause(0.3);
dv_pr('split_back', 'read(u, u.NumBytesAvailable, "uint8")');
dv_px('outputsize_bad', 'u.OutputDatagramSize = 70000');
dv_px('localport_set', 'u.LocalPort = 5');
dv_px('timeout_bad', 'u.Timeout = -1');
dv_px('timeout_int', 'u.Timeout = int32(2)');
dv_px('broadcast_set', 'u.EnableBroadcast = true');
dv_pr('broadcast_after', 'u.EnableBroadcast');
dv_px('broadcast_bad', 'u.EnableBroadcast = 5');
dv_px('multicast_on', 'configureMulticast(u, "226.0.0.1")');
dv_pr('multicast_group', 'u.MulticastGroup');
dv_pr('multicast_enabled', 'u.EnableMulticast');
dv_pr('multicast_loop', 'u.EnableMulticastLoopback');
dv_px('multicast_loop_off', 'configureMulticast(u, "226.0.0.1", false)');
dv_px('multicast_off', 'configureMulticast(u, "off")');
dv_pr('multicast_group_off', 'u.MulticastGroup');
dv_px('multicast_bad', 'configureMulticast(u, "10.0.0.1")');
CBLOG = {};
configureCallback(u, "byte", 3, @(src, evt) sp_cbevtlog(src, evt));
write(u, 1:6, "uint8", "127.0.0.1", 50240);
pause(0.5);
dv_pr('cb_log', 'CBLOG');
configureCallback(u, "off");
dv_pr('find', 'class(udpportfind)');
clear u
% ctor forms
dv_pr('ctor_ipv6', 'get(udpport("IPV6"), ''IPAddressVersion'')');
dv_pr('ctor_byte_ipv4', 'class(udpport("byte", "IPV4"))');
dv_pr('ctor_bogus', 'udpport("bogus")');
dv_pr('ctor_second_bad', 'udpport("byte", 5)');
dv_pr('ctor_odd', 'udpport("LocalPort")');
dv_pr('ctor_bad_nv', 'udpport("byte", "IPV4", "Bogus", 1)');
dv_pr('ctor_bad_port', 'udpport("LocalPort", 70000)');
dv_pr('ctor_nv_first', 'get(udpport("Timeout", 3), ''Timeout'')');
dv_pr('ctor_nv_second', 'get(udpport("byte", "Timeout", 3), ''Timeout'')');
dv_pr('ctor_port_used', 'udpport("LocalPort", 50240)');
dv_pr('ctor_port_shared', 'udpport("LocalPort", 50240, "EnablePortSharing", true)');
% datagram mode
d = udpport("datagram", "LocalPort", 50242, "Timeout", 1);
dv_pr('dg_class', 'class(d)');
dv_pr('dg_properties', 'properties(d)');
dv_pr('dg_methods', 'methods(d)');
dprops = {'NumDatagramsAvailable','DatagramsAvailableFcnMode','DatagramsAvailableFcnCount','DatagramsAvailableFcn', ...
    'OutputDatagramSize','NumBytesWritten'};
for k = 1:numel(dprops)
    dv_pr(['dg_prop_' dprops{k}], ['d.' dprops{k}]);
end
write(d, 1:5, "uint8", "127.0.0.1", 50240);
write(d, 6:7, "uint8", "127.0.0.1", 50240);
pause(0.3);
dv_pr('dg_nda', 'd.NumDatagramsAvailable');
g = read(d, 1);
dv_pr('dg_read_class', 'class(g)');
dv_pr('dg_read_size', 'size(g)');
dv_pr('dg_fields', 'properties(g)');
dv_pr('dg_data', 'g.Data');
dv_pr('dg_sender', 'g.SenderAddress');
dv_pr('dg_senderport', 'g.SenderPort');
dv_px('dg_disp', 'g');
g2 = read(d, 1, "uint16");
dv_pr('dg_data_uint16', 'g2.Data');
write(d, 1:3, "uint8", "127.0.0.1", 50240);
write(d, 4:6, "uint8", "127.0.0.1", 50240);
pause(0.3);
g3 = read(d, 2);
dv_pr('dg_two_size', 'size(g3)');
dv_pr('dg_two_data2', 'g3(2).Data');
lastwarn('');
dv_pr('dg_read_timeout', 'read(d, 1)');
[m, id] = lastwarn;
dv_pr('dg_read_timeout_warn', 'sprintf(''%s ## %s'', id, m)');
dv_pr('dg_read_none', 'read(d)');
dv_px('dg_readline', 'readline(d)');
CBLOG = {};
configureCallback(d, "datagram", 1, @(src, evt) sp_cbevtlog(src, evt));
write(d, 1:2, "uint8", "127.0.0.1", 50240);
pause(0.5);
dv_pr('dg_cb_log', 'CBLOG');
configureCallback(d, "off");
clear d
echoudp("off");
dv_pr('resolvehost', 'resolvehost("localhost")');
[n, a] = resolvehost("localhost");
dv_pr('resolvehost_two', 'a');
dv_pr('resolvehost_addr', 'resolvehost("127.0.0.1", "name")');
dv_pr('resolvehost_bad', 'resolvehost("no.such.host.invalid")');

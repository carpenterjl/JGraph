% net_udp_datagram.m -- udpport in datagram mode against echoudp on the loopback (device classes
% plan, stage D2): the object and its properties; read answering udpport.datagram.Datagram values
% (Data in double, char or string, SenderAddress, SenderPort), fewer than asked with udpport's own
% warning, a datagram that does not divide into the precision refused and consumed; writes; the
% "datagram" callback and its DatagramAvailableInfo; the refusals. Ports come from the OS.

global DVLOG
u0 = udpport; port = u0.LocalPort; delete(u0); clear u0
echoudp("on", port);
d = udpport("datagram", "Timeout", 1);

ix_chk('class', class(d));
ix_chk('properties', ix_show(properties(d)));
ix_chk('methods', ix_show(methods(d)));
props = {'NumDatagramsAvailable','NumDatagramsWritten','DatagramsAvailableFcnMode','DatagramsAvailableFcnCount', ...
    'OutputDatagramSize','IPAddressVersion','LocalHost','ByteOrder','Timeout','Tag'};
for k = 1:numel(props)
    ix_chk(['prop_' props{k}], ix_show(d.(props{k})));
end
ix_chk('prop_fcn_empty', isempty(d.DatagramsAvailableFcn));
ix_chk('no_bytes_written', ix_id(@() d.NumBytesWritten));
ix_chk('no_readline', ix_id(@() readline(d)));

% refusals
ix_chk('write_nodest', dv_err(@() write(d, 1:3)));
ix_chk('read_none', dv_err(@() read(d)));
ix_chk('read_str', dv_err(@() read(d, "a")));
ix_chk('read_zero', ix_show(read(d, 0)));
ix_chk('cb_bad_mode', dv_err(@() configureCallback(d, "byte", 1, @disp)));
ix_chk('cb_nofcn', dv_err(@() configureCallback(d, "datagram", 1)));
ix_chk('cb_zero', dv_err(@() configureCallback(d, "datagram", 0, @disp)));
ix_chk('cb_notfcn', dv_err(@() configureCallback(d, "datagram", 1, 5)));
ix_chk('flush_bad', dv_err(@() flush(d, "sideways")));
ix_chk('set_count', dv_err(@() set(d, 'DatagramsAvailableFcnCount', 2)));
ix_chk('set_mode', dv_err(@() set(d, 'DatagramsAvailableFcnMode', "off")));

% datagrams
write(d, 1:5, "uint8", "127.0.0.1", port);
write(d, 6:7, "uint8", "127.0.0.1", port);
pause(0.3);
ix_chk('nda', ix_show(d.NumDatagramsAvailable));
ix_chk('ndw', ix_show(d.NumDatagramsWritten));
g = read(d, 1);
ix_chk('dg_class', class(g));
ix_chk('dg_size', ix_show(size(g)));
ix_chk('dg_data', ix_show(g.Data));
ix_chk('dg_sender', ix_show(g.SenderAddress));
ix_chk('dg_senderport', g.SenderPort == port);
g2 = read(d, 1, "uint16");
ix_chk('dg_uint16', ix_show(g2.Data));
write(d, 1:3, "uint8");
write(d, 4:6, "uint8");
pause(0.3);
g3 = read(d, 2);
ix_chk('dg_two_size', ix_show(size(g3)));
ix_chk('dg_two_data1', ix_show(g3(1).Data));
ix_chk('dg_two_data2', ix_show(g3(2).Data));
write(d, 65:67, "127.0.0.1", port);
pause(0.3);
y = read(d, 1, "char");
ix_chk('dg_char', ix_show(y.Data));
write(d, 65:67, "127.0.0.1", port);
pause(0.3);
y = read(d, 1, "string");
ix_chk('dg_string', ix_show(y.Data));
write(d, 1:3, "127.0.0.1", port);
pause(0.3);
ix_chk('dg_odd', dv_err(@() read(d, 1, "uint16")));
ix_chk('dg_odd_consumed', ix_show(d.NumDatagramsAvailable));
write(d, uint16([1 2]), "uint16", "127.0.0.1", port);
pause(0.3);
[v, w] = dv_warn(@() read(d, 2, "uint16"));
ix_chk('dg_fewer_class', class(v));
ix_chk('dg_fewer_size', ix_show(size(v)));
ix_chk('dg_fewer_warn', w);
[v, w] = dv_warn(@() read(d, 1));
ix_chk('dg_timeout', ix_show(v));
ix_chk('dg_timeout_warn', w);
[v, w] = dv_warn(@() read(d, 1, "int3"));
ix_chk('dg_badprec_nodata', ix_show(v));
ix_chk('dg_badprec_warn', w);

% the datagram callback
DVLOG = {};
configureCallback(d, "datagram", 1, @dv_netevt);
ix_chk('cb_mode', ix_show(d.DatagramsAvailableFcnMode));
write(d, 1:2, "uint8", "127.0.0.1", port);
write(d, 3:4, "uint8", "127.0.0.1", port);
pause(0.5);
ix_chk('cb_calls', numel(DVLOG) / 5);
ix_chk('cb_first', ix_show(DVLOG(1:min(5, end))));
configureCallback(d, "datagram", 2, @dv_netevt);
DVLOG = {};
write(d, 1, "uint8", "127.0.0.1", port);
write(d, 2, "uint8", "127.0.0.1", port);
write(d, 3, "uint8", "127.0.0.1", port);
pause(0.5);
ix_chk('cb_two_calls', numel(DVLOG) / 5);
configureCallback(d, "off");
ix_chk('cb_off_mode', ix_show(d.DatagramsAvailableFcnMode));
flush(d);
ix_chk('flushed', ix_show(d.NumDatagramsAvailable));
clear d
echoudp("off");

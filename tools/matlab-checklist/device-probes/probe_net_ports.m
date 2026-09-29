% PROBE_NET_PORTS  Who may bind a port another socket holds: two udpports with and without
% EnablePortSharing, a udpport on echoudp's port (and who then gets a datagram sent there), a
% tcpserver on echotcpip's port and the other way round (and who answers a client).
u0 = udpport; p = u0.LocalPort; delete(u0); clear u0
a = udpport("LocalPort", p);
dv_pr('udp_twice_plain', 'class(udpport("LocalPort", p))');
clear a
a = udpport("LocalPort", p, "EnablePortSharing", true);
dv_pr('udp_shared_then_plain', 'class(udpport("LocalPort", p))');
dv_pr('udp_shared_then_shared', 'class(udpport("LocalPort", p, "EnablePortSharing", true))');
clear a
echoudp("on", p);
c = udpport("LocalPort", p, "Timeout", 1);
s = udpport("Timeout", 1);
write(s, 1:3, "uint8", "127.0.0.1", p); pause(0.3);
dv_pr('echo_port_udpport_got', 'c.NumBytesAvailable');
dv_pr('echo_port_sender_got', 's.NumBytesAvailable');
clear c s
echoudp("off");
b = udpport("LocalPort", p);
dv_px('udp_then_echo', 'echoudp("on", p)');
echoudp("off");
clear b

u0 = udpport; q = u0.LocalPort; delete(u0); clear u0
srv = tcpserver("0.0.0.0", q);
echotcpip("on", q);
t = tcpclient("127.0.0.1", q, "Timeout", 1); pause(0.3);
dv_pr('tcp_who_answers_server', 'srv.Connected');
write(t, uint8(1:2)); pause(0.3);
dv_pr('tcp_who_answers_echo', 't.NumBytesAvailable');
clear t srv
echotcpip("off");
echotcpip("on", q);
dv_px('tcp_server_on_echo', 'srv2 = tcpserver("0.0.0.0", q);');
clear srv2
echotcpip("off");
srv3 = tcpserver("0.0.0.0", q);
dv_px('tcp_server_twice', 'srv4 = tcpserver("0.0.0.0", q);');
dv_px('tcp_server_twice_loop', 'srv5 = tcpserver("127.0.0.1", q);');
clear srv3 srv4 srv5

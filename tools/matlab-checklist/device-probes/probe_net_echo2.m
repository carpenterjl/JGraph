% PROBE_NET_ECHO2  echotcpip/echoudp on a port in use, and which port a fractional one opens.
srv = tcpserver("0.0.0.0", 50260);
dv_px('tcp_in_use', 'echotcpip("on", 50260)');
clear srv
echo_off_1 = 0; echotcpip("off");
u = udpport("LocalPort", 50261);
dv_px('udp_in_use', 'echoudp("on", 50261)');
clear u
echo_off_2 = 0; echoudp("off");
echotcpip("on", 50262.6);
dv_px('tcp_frac_up', 't = tcpclient("127.0.0.1", 50263, "ConnectTimeout", 2);');
dv_px('tcp_frac_down', 't = tcpclient("127.0.0.1", 50262, "ConnectTimeout", 2);');
clear t
echotcpip("off");
dv_px('tcp_port_neg', 'echotcpip("on", -1)');
dv_px('tcp_port_nan', 'echotcpip("on", NaN)');
dv_px('tcp_port_vec', 'echotcpip("on", [50264 50265])');
dv_px('tcp_port_int', 'echotcpip("on", int32(50266))');
echotcpip("off");
dv_px('udp_port_str', 'echoudp("on", "50267")');
dv_px('udp_port_vec', 'echoudp("on", [50267 50268])');
echoudp("off");

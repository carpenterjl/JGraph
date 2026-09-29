% PROBE_NET_DGCB  When udpport's "datagram" callback runs: a fresh object with Count 2 and 3, 1, 4
% datagrams; datagrams already waiting when the callback is set; a count change; a read in between.
global CBN
u0 = udpport; port = u0.LocalPort; delete(u0); clear u0
echoudp("on", port);
d = udpport("datagram", "Timeout", 1);
write(d, 0, "uint8", "127.0.0.1", port); pause(0.3); flush(d);
CBN = 0;
configureCallback(d, "datagram", 2, @(s, e) dv_cbn());
for k = 1:3, write(d, k, "uint8"); end
pause(0.5);
dv_pr('fresh_3', 'CBN');
dv_pr('fresh_3_nda', 'd.NumDatagramsAvailable');
write(d, 4, "uint8"); pause(0.5);
dv_pr('fresh_4', 'CBN');
for k = 1:4, write(d, k, "uint8"); end
pause(0.5);
dv_pr('fresh_8', 'CBN');
dv_pr('fresh_8_nda', 'd.NumDatagramsAvailable');
configureCallback(d, "off");
flush(d);

% datagrams waiting before the callback is set
for k = 1:3, write(d, k, "uint8"); end
pause(0.5);
CBN = 0;
configureCallback(d, "datagram", 2, @(s, e) dv_cbn());
dv_pr('waiting_3_at_set', 'CBN');
pause(0.5);
dv_pr('waiting_3_after_pause', 'CBN');
write(d, 9, "uint8"); pause(0.5);
dv_pr('waiting_plus_1', 'CBN');
configureCallback(d, "off");
flush(d);

% one at a time with a pause between
CBN = 0;
configureCallback(d, "datagram", 2, @(s, e) dv_cbn());
for k = 1:5, write(d, k, "uint8"); pause(0.3); end
dv_pr('paced_5', 'CBN');
configureCallback(d, "off");
flush(d);

% a read in between
CBN = 0;
configureCallback(d, "datagram", 2, @(s, e) dv_cbn());
write(d, 1, "uint8"); pause(0.3);
x = read(d, 1);
write(d, 2, "uint8"); pause(0.3);
dv_pr('read_between', 'CBN');
write(d, 3, "uint8"); pause(0.3);
dv_pr('read_between_3', 'CBN');
configureCallback(d, "off");
flush(d);

% count 1 then count 2 without a flush
CBN = 0;
configureCallback(d, "datagram", 1, @(s, e) dv_cbn());
write(d, 1, "uint8"); write(d, 2, "uint8"); pause(0.5);
dv_pr('count1_2', 'CBN');
configureCallback(d, "datagram", 2, @(s, e) dv_cbn());
CBN = 0;
for k = 1:3, write(d, k, "uint8"); end
pause(0.5);
dv_pr('count2_after_count1', 'CBN');
configureCallback(d, "off");
clear d

% byte mode on udpport: once per Count bytes?
u = udpport("Timeout", 1);
write(u, 0, "uint8", "127.0.0.1", port); pause(0.3); flush(u);
CBN = 0;
configureCallback(u, "byte", 4, @(s, e) dv_cbn());
write(u, 1:3, "uint8"); pause(0.3);
write(u, 1:3, "uint8"); pause(0.3);
write(u, 1:3, "uint8"); pause(0.3);
dv_pr('byte_9_by3', 'CBN');
configureCallback(u, "off");
clear u
echoudp("off");

% a second client of a tcpserver, and multicast on this machine
srv = tcpserver("127.0.0.1", port);
c = tcpclient("127.0.0.1", port); pause(0.3);
dv_px('second_client', 'c2 = tcpclient("127.0.0.1", port, "ConnectTimeout", 1);');
clear c; pause(0.5);
dv_px('after_first_left', 'c3 = tcpclient("127.0.0.1", port, "ConnectTimeout", 1);');
pause(0.3);
dv_pr('after_first_left_connected', 'srv.Connected');
clear c3 srv

function dv_cbn()
global CBN
CBN = CBN + 1;
end

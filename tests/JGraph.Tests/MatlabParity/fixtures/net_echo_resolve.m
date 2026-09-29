% net_echo_resolve.m -- echotcpip, echoudp and resolvehost (device classes plan, stage D2): the echo
% servers' syntax refusals from echotcpip.m and echoudp.m, their port checks, a second "on", "off"
% twice, a fractional port opening at its integer part; resolvehost's refusals, its flags, its
% answers for a name, an address, a bad address (with its warning) and a name that does not resolve.

u0 = udpport; port = u0.LocalPort; delete(u0); clear u0

% echotcpip
ix_chk('tcp_none', dv_err(@() echotcpip));
ix_chk('tcp_on_only', dv_err(@() echotcpip("on")));
ix_chk('tcp_three', dv_err(@() echotcpip("on", port, 1)));
ix_chk('tcp_bad_state', dv_err(@() echotcpip("maybe", 5)));
ix_chk('tcp_num_state', dv_err(@() echotcpip(1, port)));
ix_chk('tcp_off_port', dv_err(@() echotcpip("off", 5)));
ix_chk('tcp_port_zero', dv_err(@() echotcpip("on", 0)));
ix_chk('tcp_port_big', dv_err(@() echotcpip("on", 70000)));
ix_chk('tcp_port_str', dv_err(@() echotcpip("on", "50250")));
ix_chk('tcp_port_neg', dv_err(@() echotcpip("on", -1)));
ix_chk('tcp_port_nan', dv_err(@() echotcpip("on", NaN)));
ix_chk('tcp_port_vec', dv_err(@() echotcpip("on", [port port])));
ix_chk('tcp_port_int', dv_err(@() echotcpip("on", int32(port))));
ix_chk('tcp_off_idle', dv_err(@() echotcpip("off")));
ix_chk('tcp_on', dv_err(@() echotcpip("On", port)));
ix_chk('tcp_on_again', strrep(dv_err(@() echotcpip("on", port)), num2str(port), 'PORT'));
ix_chk('tcp_on_other', strrep(dv_err(@() echotcpip("on", port + 1)), num2str(port), 'PORT'));
t = tcpclient("127.0.0.1", port, "Timeout", 1);
write(t, uint8(1:3));
pause(0.3);
ix_chk('tcp_echoed', ix_show(read(t)));
clear t
ix_chk('tcp_prefix_off', dv_err(@() echotcpip("of")));
ix_chk('tcp_off_twice', dv_err(@() echotcpip("off")));
echotcpip("on", port + 0.6);
ix_chk('tcp_frac_down', ix_id(@() tcpclient("127.0.0.1", port, "ConnectTimeout", 2)));
echotcpip("off");
ix_chk('tcp_nargout', ix_id(@() dv_one(@() echotcpip("off"))));

% echoudp
ix_chk('udp_none', dv_err(@() echoudp));
ix_chk('udp_on_only', dv_err(@() echoudp("on")));
ix_chk('udp_three', dv_err(@() echoudp("on", port, 1)));
ix_chk('udp_bad_state', dv_err(@() echoudp("maybe", 5)));
ix_chk('udp_off_port', dv_err(@() echoudp("off", 5)));
ix_chk('udp_port_zero', dv_err(@() echoudp("on", 0)));
ix_chk('udp_port_str', dv_err(@() echoudp("on", "50267")));
ix_chk('udp_port_vec', dv_err(@() echoudp("on", [port port])));
ix_chk('udp_off_idle', dv_err(@() echoudp("off")));
ix_chk('udp_on', dv_err(@() echoudp("on", port)));
ix_chk('udp_on_again', strrep(dv_err(@() echoudp("on", port)), num2str(port), 'PORT'));
u = udpport("Timeout", 1);
write(u, 1:3, "uint8", "127.0.0.1", port);
pause(0.3);
ix_chk('udp_echoed', ix_show(read(u, u.NumBytesAvailable)));
clear u
ix_chk('udp_off', dv_err(@() echoudp("off")));

% resolvehost
ix_chk('rh_none', dv_err(@() resolvehost));
ix_chk('rh_three', dv_err(@() resolvehost("a", "name", 1)));
ix_chk('rh_num', dv_err(@() resolvehost(5)));
ix_chk('rh_flag_num', dv_err(@() resolvehost("localhost", 5)));
ix_chk('rh_flag_bad', dv_err(@() resolvehost("localhost", "who")));
ix_chk('rh_three_out', ix_id(@() dv_three(@resolvehost, "localhost")));
ix_chk('rh_name', ix_show(resolvehost("localhost")));
ix_chk('rh_char', ix_show(resolvehost('localhost')));
[n, a] = resolvehost("localhost");
ix_chk('rh_two_name', ix_show(n));
ix_chk('rh_two_addr', ix_show(a));
ix_chk('rh_flag_addr', ix_show(resolvehost("localhost", "address")));
ix_chk('rh_flag_prefix', ix_show(resolvehost("localhost", "n")));
ix_chk('rh_empty', ix_show(resolvehost("")));
ix_chk('rh_blank', ix_show(resolvehost("  ")));
ix_chk('rh_upper', ix_show(resolvehost("LOCALHOST")));
[v, w] = dv_warn(@() resolvehost("300.1.1.1"));
ix_chk('rh_badip', ix_show(v));
ix_chk('rh_badip_warn', w);
ix_chk('rh_three_dots', ix_show(resolvehost("1.2.3")));
ix_chk('rh_ip_addr', ix_show(resolvehost("127.0.0.1", "address")));
[n, a] = resolvehost("no.such.host.invalid");
ix_chk('rh_bad_name', ix_show(n));
ix_chk('rh_bad_addr', ix_show(a));
[n, a] = resolvehost("127.0.0.1");
ix_chk('rh_ip_is_machine', strcmpi(n, getenv('COMPUTERNAME')));
ix_chk('rh_ip_two_addr', ix_show(a));
ix_chk('rh_ip_name', strcmpi(resolvehost("127.0.0.1", "name"), getenv('COMPUTERNAME')));

function v = dv_one(f)
v = f();
end

function dv_three(f, x)
[a, b, c] = f(x); %#ok<ASGLU>
end

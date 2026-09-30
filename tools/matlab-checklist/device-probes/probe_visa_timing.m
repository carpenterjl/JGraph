% probe_visa_timing: how visadev's synchronous reads end on each kind of resource -- nothing waiting,
% part of what was asked, the rest arriving inside and outside the timeout, more than was asked --
% for readline, read and readbinblock, with the VISA attributes and the leftovers after each.
visa_peer();
names = ["ASRL20::INSTR", "TCPIP0::127.0.0.1::5025::SOCKET", "TCPIP0::127.0.0.1::hislip0::INSTR"];
tags = ["asrl", "sock", "hislip"];
for i = 1:numel(names)
    v = visadev(names(i));
    v.Timeout = 1;
    if tags(i) == "hislip"
        configureTerminator(v, "LF");
    end
    t = char(tags(i));
    dv_vcase(v, [t '_rl_none'], '', '', @readline);
    dv_vcase(v, [t '_rl_partial'], '6364', '', @readline);
    dv_vcase(v, [t '_rl_late_in'], '6364', '500 0A', @readline);
    dv_vcase(v, [t '_rl_late_out'], '6364', '1500 0A', @readline);
    dv_vcase(v, [t '_rl_full'], '63640A', '', @readline);
    dv_vcase(v, [t '_rl_two'], '610A620A', '', @readline);
    dv_vcase(v, [t '_rd_none'], '', '', @(v) read(v, 5));
    dv_vcase(v, [t '_rd_partial'], '0102', '', @(v) read(v, 5));
    dv_vcase(v, [t '_rd_late_in'], '0102', '500 030405', @(v) read(v, 5));
    dv_vcase(v, [t '_rd_late_out'], '0102', '1500 030405', @(v) read(v, 5));
    dv_vcase(v, [t '_rd_more'], '01020304050607', '', @(v) read(v, 5));
    dv_vcase(v, [t '_rd_lf'], '010A020304', '', @(v) read(v, 5));
    dv_vcase(v, [t '_rd_u16_odd'], '010203', '', @(v) read(v, 2, "uint16"));
    dv_vcase(v, [t '_rbb_short'], '2331354142', '', @readbinblock);
    dv_vcase(v, [t '_rbb_none'], '', '', @readbinblock);
    dv_vcase(v, [t '_rbb_bad'], '2341', '', @readbinblock);
    dv_vcase(v, [t '_rbb_lead'], '78782331334142430A', '', @readbinblock);
    dv_vcase(v, [t '_wr_none'], '', '', @(v) writeread(v, "q"));
    dv_vcase(v, [t '_wr_partial'], '', '100 6364', @(v) writeread(v, "q"));
    if tags(i) == "hislip"
        configureTerminator(v, "off", "LF");
        dv_vcase(v, [t '_off_rl_none'], '', '', @readline);
        dv_vcase(v, [t '_off_rl_msg'], '61620A6364', '', @readline);
        dv_vcase(v, [t '_off_rl_late'], '', '500 6162', @readline);
        dv_vcase(v, [t '_off_wr_none'], '', '', @(v) writeread(v, "q"));
        dv_vcase(v, [t '_off_rd_partial'], '0102', '', @(v) read(v, 5));
    end
    clear v
end

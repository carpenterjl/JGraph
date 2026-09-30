% visa_list_ctor.m -- visadevlist, visadevfind and visadev's refusals (device classes plan, stage D4):
% the list's table with and without identification, every refusal of its name-value pairs, the
% constructor's refusals before and after the resource is sought, a name found through the list's
% aliases, "reset", and visadevfind. R2025b records against NI-VISA with the com0com pair and the
% peers visa_peer starts; JGraph replays against jgraph.internal.visasim.

visa_peer();

% the list
L = visadevlist("Timeout", 2);
ix_chk('list_class', class(L));
ix_chk('list_vars', ix_show(L.Properties.VariableNames));
ix_chk('list_rows', ix_show(L.Properties.RowNames));
ix_chk('list_names', ix_show(L.ResourceName));
ix_chk('list_alias', ix_show(L.Alias));
ix_chk('list_vendor', ix_show(L.Vendor));
ix_chk('list_model', ix_show(L.Model));
ix_chk('list_serial', ix_show(L.SerialNumber));
ix_chk('list_type', ix_show(string(L.Type)));
% R2025b's Type is a visalib.InterfaceType column, JGraph's its names (div=ADR0187)
ix_chk('list_type_class', class(L.Type), 'div=ADR0187');
ix_chk('list_type_eq', ix_show(L.Type == "serial"));
L2 = visadevlist("Timeout", 2, "Identification", false);
ix_chk('list_noid_alias', ix_show(L2.Alias));
ix_chk('list_noid_type', ix_show(string(L2.Type)));
ix_chk('list_default', ix_show(size(visadevlist)));
ix_chk('list_duration', ix_show(size(visadevlist("Timeout", seconds(3)))));
ix_chk('list_case', ix_show(size(visadevlist("timeout", 3))));
ix_chk('list_partial', ix_show(size(visadevlist("Time", 3))));
ix_chk('list_pair', ix_show(size(visadevlist("Timeout", 2, "Identification", ["ASRL20::INSTR" "*IDN?"]))));
ix_chk('list_t1', dv_err(@() visadevlist("Timeout", 1)));
ix_chk('list_tstr', dv_err(@() visadevlist("Timeout", "5")));
ix_chk('list_tinf', dv_err(@() visadevlist("Timeout", Inf)));
ix_chk('list_tvec', dv_err(@() visadevlist("Timeout", [3 4])));
ix_chk('list_bogus', dv_err(@() visadevlist("Bogus", 3)));
ix_chk('list_odd', dv_err(@() visadevlist("Timeout")));
ix_chk('list_five', dv_err(@() visadevlist("Timeout", 3, "Identification", true, 4)));
ix_chk('list_id_num', dv_err(@() visadevlist("Timeout", 3, "Identification", 1)));
ix_chk('list_id_vec', dv_err(@() visadevlist("Timeout", 3, "Identification", [true false])));
ix_chk('list_id_1d', dv_err(@() visadevlist("Timeout", 3, "Identification", ["ASRL20::INSTR" "*IDN?" "x"])));
ix_chk('list_id_dup', dv_err(@() visadevlist("Timeout", 3, "Identification", ["ASRL20::INSTR" "*IDN?"; "ASRL20::INSTR" "*IDN?"])));
ix_chk('list_id_badname', dv_err(@() visadevlist("Timeout", 3, "Identification", ["NOPE" "*IDN?"])));
ix_chk('list_id_instr', dv_err(@() visadevlist("Timeout", 3, "Identification", ["TCPIP0::127.0.0.1::hislip0::INSTR" "*IDN?"])));

% the constructor's refusals
ix_chk('ctor_none', dv_err(@() visadev()));
ix_chk('ctor_empty', dv_err(@() visadev("")));
ix_chk('ctor_emptychar', dv_err(@() visadev('')));
ix_chk('ctor_num', dv_err(@() visadev(5)));
ix_chk('ctor_strvec', dv_err(@() visadev(["a" "b"])));
ix_chk('ctor_bad', dv_err(@() visadev("NOPE")));
ix_chk('ctor_gpib', dv_err(@() visadev("GPIB0::5::INSTR")));
ix_chk('ctor_usb', dv_err(@() visadev("USB0::0x1234::0x5678::SN1::INSTR")));
ix_chk('ctor_asrl99', dv_err(@() visadev("ASRL99::INSTR")));
ix_chk('ctor_peer_port', dv_err(@() visadev("ASRL21::INSTR")));
ix_chk('ctor_sock_closed', dv_err(@() visadev("TCPIP0::127.0.0.1::1::SOCKET")));
ix_chk('ctor_vxi11', dv_err(@() visadev("TCPIP0::127.0.0.1::inst0::INSTR")));
ix_chk('ctor_tag_bad', dv_err(@() visadev("ASRL20::INSTR", "Tag", 5)));
ix_chk('ctor_bogus_nv', dv_err(@() visadev("ASRL20::INSTR", "Bogus", 5)));
ix_chk('ctor_odd_nv', dv_err(@() visadev("ASRL20::INSTR", "Tag")));
ix_chk('ctor_sync_num', dv_err(@() visadev("ASRL20::INSTR", 1)));
ix_chk('ctor_sync_two', dv_err(@() visadev("ASRL20::INSTR", true, false)));
try
    r = visadev("reset"); %#ok<NASGU>
    s = 'none';
catch e
    s = [e.identifier ' ## ' ix_flat(e.message)];
end
ix_chk('ctor_reset_output', s);
ix_chk('ctor_reset_statement', dv_err(@() visadev("reset")));

% names, aliases and the second object
v = visadev({'ASRL20::INSTR'});
ix_chk('ctor_cell', class(v));
clear v
v = visadev("COM20", "Tag", "dev1");
ix_chk('alias_name', ix_show(v.ResourceName));
ix_chk('alias_alias', ix_show(v.Alias));
ix_chk('alias_tag', ix_show(v.Tag));
ix_chk('sync_true', class(visadev("TCPIP0::127.0.0.1::5025::SOCKET", true)));
ix_chk('second', dv_err(@() visadev("ASRL20::INSTR")));
ix_chk('second_alias', dv_err(@() visadev("COM20")));
ix_chk('find_tag', class(visadevfind("Tag", "dev1")));
ix_chk('find_tag_same', visadevfind("Tag", "dev1") == v);
ix_chk('find_type', ix_show(size(visadevfind("Type", "serial"))));
ix_chk('find_all', ix_show(size(visadevfind)));
ix_chk('find_none', ix_show(visadevfind("Tag", "nope")));
ix_chk('find_bogus', ix_show(visadevfind("Bogus", 1)));
ix_chk('find_odd', dv_err(@() visadevfind("Tag")));
clear v
ix_chk('find_after_clear', ix_show(visadevfind));
v = visadev("asrl20::instr");
ix_chk('lower_name', ix_show(v.ResourceName));
ix_chk('lower_port', ix_show(v.Port));
clear v
v = visadev("TCPIP::127.0.0.1::hislip0::INSTR");
ix_chk('noboard_name', ix_show(v.ResourceName));
clear v
v = visadev("tcpip0::127.0.0.1::hislip0::instr");
ix_chk('lower_tcpip_name', ix_show(v.ResourceName));
ix_chk('second_upper', dv_err(@() visadev("TCPIP0::127.0.0.1::hislip0::INSTR")));
clear v

% serial_find_save.m -- serialportlist, serialportfind, and the settings a cleared serialport leaves
% for the next serialport() (device classes plan, stage D1). The list rows ask about COM20 and
% COM21 by name, so a machine with other ports records the same lines.

device_peer();
internal.Serialport.clearPreferences();
ports = serialportlist("all");
ix_chk('list_class', class(ports));
ix_chk('list_has_pair', ports(ismember(ports, ["COM20" "COM21"])));
ix_chk('list_default_same', isequal(serialportlist, serialportlist("all")));
avail = serialportlist("available");
ix_chk('available_before', avail(ismember(avail, ["COM20" "COM21"])));
ix_chk('list_char', isequal(serialportlist('all'), ports));
ix_chk('list_partial', isequal(serialportlist("avail"), avail));
ix_chk('list_upper', isequal(serialportlist("ALL"), ports));
ix_chk('list_bogus', dv_err(@() serialportlist("bogus")));
ix_chk('list_number', dv_err(@() serialportlist(1)));
ix_chk('list_two', dv_err(@() serialportlist("all", "available")));

ix_chk('find_none', ix_show(serialportfind));
ix_chk('find_none_tag', ix_show(serialportfind("Tag", "x")));
s = serialport("COM20", 9600, "Timeout", 1, "Tag", "probe");
avail = serialportlist("available");
ix_chk('available_held', avail(ismember(avail, ["COM20" "COM21"])));
f = serialportfind;
ix_chk('find_all_same', f == s);
ix_chk('find_tag', serialportfind("Tag", "probe") == s);
ix_chk('find_tag_nv', serialportfind(Tag="probe") == s);
ix_chk('find_port', serialportfind("Port", "COM20") == s);
ix_chk('find_port_char', serialportfind('Port', 'COM20') == s);
ix_chk('find_baud', serialportfind("BaudRate", 9600) == s);
ix_chk('find_case', serialportfind("tag", "probe") == s);
ix_chk('find_two', serialportfind("Tag", "probe", "Port", "COM20") == s);
ix_chk('find_other', ix_show(serialportfind("Tag", "nothing")));
ix_chk('find_bogus', ix_show(serialportfind("Bogus", 1)));
ix_chk('find_odd', dv_err(@() serialportfind("Tag")));
clear f

% clearing a connected object saves its settings; serialport() reads them
s.BaudRate = 19200;
s.Parity = "even";
s.DataBits = 7;
s.StopBits = 2;
s.FlowControl = "software";
s.ByteOrder = "big-endian";
s.Timeout = 2.5;
configureTerminator(s, "CR", 10);
clear s
ix_chk('find_after_clear', ix_show(serialportfind));
avail = serialportlist("available");
ix_chk('available_after_clear', avail(ismember(avail, ["COM20" "COM21"])));
s = serialport();
ix_chk('noarg_port', ix_show(s.Port));
ix_chk('noarg_baud', ix_show(s.BaudRate));
ix_chk('noarg_parity', ix_show(s.Parity));
ix_chk('noarg_databits', ix_show(s.DataBits));
ix_chk('noarg_stopbits', ix_show(s.StopBits));
ix_chk('noarg_flow', ix_show(s.FlowControl));
ix_chk('noarg_order', ix_show(s.ByteOrder));
ix_chk('noarg_timeout', ix_show(s.Timeout));
ix_chk('noarg_tag', ix_show(s.Tag));
ix_chk('noarg_term', ix_show(s.Terminator));

% delete saves them too, and the deleted object is found no more
s.Tag = "second";
delete(s);
ix_chk('find_after_delete', ix_show(serialportfind));
s = serialport();
ix_chk('noarg_after_delete_tag', ix_show(s.Tag));
clear s
ix_chk('clear_prefs', internal.Serialport.clearPreferences());
ix_chk('noarg_after_clear', dv_err(@() serialport()));
ix_chk('clear_prefs_again', internal.Serialport.clearPreferences());

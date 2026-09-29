% serial_ctor.m -- serialport's constructor (device classes plan, stage D1): its three forms and
% their refusals in R2025b's validation order, the class and bases a script tests, and the
% defaults. The far end of COM20 is the peer engine: peer-sim.exe on com0com's COM21 in R2025b, the
% simulated port jgraph.internal.devicesim registers in JGraph (helpers/device_peer.m).
%
% An unset callback reads as an empty function_handle in R2025b and as [] in JGraph, which has no
% empty function handle; the row that asks its class is div=ADR0184.

s = dv_open();
ix_chk('class', class(s));
ix_chk('isa_handle', isa(s, 'handle'));
ix_chk('isa_internal', isa(s, 'internal.Serialport'));
ix_chk('isa_serialport', isa(s, 'serialport'));
ix_chk('isa_setget', isa(s, 'matlab.mixin.SetGet'));
ix_chk('isobject', isobject(s));
ix_chk('isstruct', isstruct(s));
ix_chk('isvalid', isvalid(s));
ix_chk('size', size(s));
ix_chk('numel', numel(s));
ix_chk('eq_self', s == s);
ix_chk('ne_self', s ~= s);
ix_chk('isequal_self', isequal(s, s));
t = s;
ix_chk('eq_alias', t == s);
t.Tag = "shared";
ix_chk('handle_shared', s.Tag);
clear t

% the defaults (Timeout came from dv_open)
ix_chk('Port', ix_show(s.Port));
ix_chk('BaudRate', ix_show(s.BaudRate));
ix_chk('Parity', ix_show(s.Parity));
ix_chk('DataBits', ix_show(s.DataBits));
ix_chk('StopBits', ix_show(s.StopBits));
ix_chk('FlowControl', ix_show(s.FlowControl));
ix_chk('ByteOrder', ix_show(s.ByteOrder));
ix_chk('Timeout', ix_show(s.Timeout));
ix_chk('Tag', ix_show(s.Tag));
ix_chk('NumBytesAvailable', ix_show(s.NumBytesAvailable));
ix_chk('Terminator', ix_show(s.Terminator));
ix_chk('BytesAvailableFcnMode', ix_show(s.BytesAvailableFcnMode));
ix_chk('BytesAvailableFcnCount', ix_show(s.BytesAvailableFcnCount));
ix_chk('BytesAvailableFcn_empty', isempty(s.BytesAvailableFcn));
ix_chk('BytesAvailableFcn_class', class(s.BytesAvailableFcn), 'div=ADR0184');
ix_chk('ErrorOccurredFcn_empty', isempty(s.ErrorOccurredFcn));
ix_chk('UserData', ix_show(s.UserData));
ix_chk('pins_at_open', dp(s, 'status'));

% R2025b opens with DTR and RTS raised: the peer reads them as its CTS, DSR and DCD
ix_chk('held_port', dv_err(@() serialport("COM20", 9600)));
ix_chk('peer_port', dv_err(@() serialport("COM21", 9600)));
ix_chk('missing_port', dv_err(@() serialport("COM99", 9600)));
clear s

% the forms and their refusals, in R2025b's order
ix_chk('one_arg', dv_err(@() serialport("COM20")));
internal.Serialport.clearPreferences(); % clearing s saved its settings
ix_chk('no_saved', dv_err(@() serialport()));
ix_chk('neg_baud', dv_err(@() serialport("COM20", -5)));
ix_chk('zero_baud', dv_err(@() serialport("COM20", 0)));
ix_chk('frac_baud', dv_err(@() serialport("COM20", 9600.5)));
ix_chk('str_baud', dv_err(@() serialport("COM20", "9600")));
ix_chk('int_baud', dv_err(@() serialport("COM20", int32(9600))));
ix_chk('vec_baud', dv_err(@() serialport("COM20", [9600 9600])));
ix_chk('empty_baud', dv_err(@() serialport("COM20", [])));
ix_chk('empty_port', dv_err(@() serialport("", 9600)));
ix_chk('num_port', dv_err(@() serialport(20, 9600)));
ix_chk('cell_port', dv_err(@() serialport({'COM20'}, 9600)));
ix_chk('odd_nv', dv_err(@() serialport("COM20", 9600, "Parity")));
ix_chk('bad_nv', dv_err(@() serialport("COM20", 9600, "Bogus", 1)));
ix_chk('userdata_nv', dv_err(@() serialport("COM20", 9600, "UserData", 5)));
ix_chk('terminator_nv', dv_err(@() serialport("COM20", 9600, "Terminator", "CR")));
ix_chk('bad_parity', dv_err(@() serialport("COM20", 9600, "Parity", "bogus")));
ix_chk('num_parity', dv_err(@() serialport("COM20", 9600, "Parity", 1)));
ix_chk('empty_parity', dv_err(@() serialport("COM20", 9600, "Parity", "")));
ix_chk('mark_parity', dv_err(@() serialport("COM20", 9600, "Parity", "mark")));
ix_chk('bad_databits', dv_err(@() serialport("COM20", 9600, "DataBits", 9)));
ix_chk('four_databits', dv_err(@() serialport("COM20", 9600, "DataBits", 4)));
ix_chk('vec_databits', dv_err(@() serialport("COM20", 9600, "DataBits", [7 8])));
ix_chk('bad_stopbits', dv_err(@() serialport("COM20", 9600, "StopBits", 3)));
ix_chk('bad_flow', dv_err(@() serialport("COM20", 9600, "FlowControl", "rts")));
ix_chk('bad_order', dv_err(@() serialport("COM20", 9600, "ByteOrder", "middle")));
ix_chk('neg_timeout', dv_err(@() serialport("COM20", 9600, "Timeout", -1)));
ix_chk('zero_timeout', dv_err(@() serialport("COM20", 9600, "Timeout", 0)));
ix_chk('inf_timeout', dv_err(@() serialport("COM20", 9600, "Timeout", Inf)));
ix_chk('vec_timeout', dv_err(@() serialport("COM20", 9600, "Timeout", [1 2])));
ix_chk('bad_tag', dv_err(@() serialport("COM20", 9600, "Tag", 5)));

% accepted forms, each object a temporary closed at the end of its statement
ix_chk('partial_nv', ix_show(get(serialport("COM20", 9600, "Par", "even"), 'Parity')));
ix_chk('lower_nv', ix_show(get(serialport("COM20", 9600, "parity", "odd"), 'Parity')));
ix_chk('nv_syntax', ix_show(get(serialport("COM20", 9600, Parity="odd", DataBits=7), {'Parity', 'DataBits'})));
ix_chk('char_nv', ix_show(get(serialport('COM20', 9600, 'FlowControl', 'software'), 'FlowControl')));
ix_chk('stop15', ix_show(get(serialport("COM20", 9600, "StopBits", 1.5), 'StopBits')));
ix_chk('databits5', ix_show(get(serialport("COM20", 9600, "DataBits", 5), 'DataBits')));
ix_chk('big_endian', ix_show(get(serialport("COM20", 9600, "ByteOrder", "big"), 'ByteOrder')));
ix_chk('timeout_nv', ix_show(get(serialport("COM20", 9600, "Timeout", 2.5), 'Timeout')));
ix_chk('tag_nv', ix_show(get(serialport("COM20", 9600, "Tag", 'dev'), 'Tag')));
ix_chk('repeat_nv', ix_show(get(serialport("COM20", 9600, "Parity", "odd", "Parity", "even"), 'Parity')));
ix_chk('odd_baud', ix_show(get(serialport("COM20", 12345), 'BaudRate')));
ix_chk('lower_port', ix_show(get(serialport("com20", 9600), 'Port')));
ix_chk('reopened', isvalid(serialport("COM20", 9600)));

% a statement's answer is ans, which holds the port until it is cleared
serialport("COM20", 115200);
ix_chk('ans_class', class(ans));
ix_chk('ans_baud', ans.BaudRate);
ix_chk('ans_holds', dv_err(@() serialport("COM20", 9600)));
clear ans
ix_chk('ans_cleared', isvalid(serialport("COM20", 9600)));

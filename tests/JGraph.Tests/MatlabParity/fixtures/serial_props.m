% serial_props.m -- serialport's properties (device classes plan, stage D1): each setter and the
% refusal it raises in R2025b's words, the read-only ones, get and set, isprop, properties and
% methods. The far end is the peer engine (helpers/device_peer.m).

s = dv_open();
ix_chk('properties', strjoin(properties(s)', ','));
ix_chk('fieldnames', strjoin(fieldnames(s)', ','));
ix_chk('methods', strjoin(methods(s)', ','));
ix_chk('isprop_baud', isprop(s, 'BaudRate'));
ix_chk('isprop_bogus', isprop(s, 'Bogus'));
ix_chk('isprop_hidden', isprop(s, 'BytesAvailable'));
ix_chk('ismethod_read', ismethod(s, 'read'));
ix_chk('ismethod_bogus', ismethod(s, 'bogus'));

% BaudRate
s.BaudRate = 115200;
ix_chk('baud_set', ix_show(s.BaudRate));
s.BaudRate = int32(19200);
ix_chk('baud_int32', ix_show(s.BaudRate));
ix_chk('baud_neg', dv_err(@() set(s, 'BaudRate', -1)));
ix_chk('baud_zero', dv_err(@() set(s, 'BaudRate', 0)));
ix_chk('baud_frac', dv_err(@() set(s, 'BaudRate', 9600.5)));
ix_chk('baud_str', dv_err(@() set(s, 'BaudRate', "9600")));
ix_chk('baud_vec', dv_err(@() set(s, 'BaudRate', [1 2])));
ix_chk('baud_inf', dv_err(@() set(s, 'BaudRate', Inf)));
ix_chk('baud_kept', ix_show(s.BaudRate));
s.BaudRate = 9600;

% Timeout
s.Timeout = 2.5;
ix_chk('timeout_set', ix_show(s.Timeout));
s.Timeout = single(2);
ix_chk('timeout_single', ix_show(s.Timeout));
ix_chk('timeout_int', dv_err(@() set(s, 'Timeout', int32(3))));
ix_chk('timeout_zero', dv_err(@() set(s, 'Timeout', 0)));
ix_chk('timeout_inf', dv_err(@() set(s, 'Timeout', Inf)));
ix_chk('timeout_neg', dv_err(@() set(s, 'Timeout', -1)));
ix_chk('timeout_nan', dv_err(@() set(s, 'Timeout', NaN)));
ix_chk('timeout_vec', dv_err(@() set(s, 'Timeout', [1 2])));
ix_chk('timeout_str', dv_err(@() set(s, 'Timeout', "5")));
ix_chk('timeout_empty', dv_err(@() set(s, 'Timeout', [])));
ix_chk('timeout_logical', dv_err(@() set(s, 'Timeout', true)));
ix_chk('timeout_kept', ix_show(s.Timeout));
s.Timeout = 1;

% Parity, FlowControl, ByteOrder
s.Parity = 'even';
ix_chk('parity_char', ix_show(s.Parity));
s.Parity = "ODD";
ix_chk('parity_upper', ix_show(s.Parity));
s.Parity = "n";
ix_chk('parity_partial', ix_show(s.Parity));
ix_chk('parity_bad', dv_err(@() set(s, 'Parity', "bogus")));
ix_chk('parity_num', dv_err(@() set(s, 'Parity', 1)));
ix_chk('parity_empty', dv_err(@() set(s, 'Parity', "")));
ix_chk('parity_cell', dv_err(@() set(s, 'Parity', {'none'})));
ix_chk('parity_mark', dv_err(@() set(s, 'Parity', "mark")));
s.FlowControl = "hard";
ix_chk('flow_partial', ix_show(s.FlowControl));
s.FlowControl = "software";
ix_chk('flow_software', ix_show(s.FlowControl));
s.FlowControl = "none";
ix_chk('flow_bad', dv_err(@() set(s, 'FlowControl', "rts")));
ix_chk('flow_num', dv_err(@() set(s, 'FlowControl', 0)));
s.ByteOrder = "big";
ix_chk('order_partial', ix_show(s.ByteOrder));
s.ByteOrder = "little-endian";
ix_chk('order_bad', dv_err(@() set(s, 'ByteOrder', "middle")));
ix_chk('order_num', dv_err(@() set(s, 'ByteOrder', 1)));

% DataBits, StopBits
s.DataBits = int8(7);
ix_chk('databits_int8', ix_show(s.DataBits));
s.DataBits = 5;
ix_chk('databits_5', ix_show(s.DataBits));
s.DataBits = 8;
ix_chk('databits_9', dv_err(@() set(s, 'DataBits', 9)));
ix_chk('databits_4', dv_err(@() set(s, 'DataBits', 4)));
ix_chk('databits_frac', dv_err(@() set(s, 'DataBits', 7.5)));
ix_chk('databits_str', dv_err(@() set(s, 'DataBits', "8")));
ix_chk('databits_vec', dv_err(@() set(s, 'DataBits', [7 8])));
ix_chk('databits_kept', ix_show(s.DataBits));
s.StopBits = 2;
ix_chk('stopbits_2', ix_show(s.StopBits));
s.StopBits = 1.5;
ix_chk('stopbits_15', ix_show(s.StopBits));
s.StopBits = 1;
ix_chk('stopbits_3', dv_err(@() set(s, 'StopBits', 3)));
ix_chk('stopbits_0', dv_err(@() set(s, 'StopBits', 0)));
ix_chk('stopbits_str', dv_err(@() set(s, 'StopBits', "1")));

% Tag, UserData, ErrorOccurredFcn
s.Tag = 'charTag';
ix_chk('tag_char', ix_show(s.Tag));
ix_chk('tag_num', dv_err(@() set(s, 'Tag', 5)));
ix_chk('tag_cell', dv_err(@() set(s, 'Tag', {'a'})));
ix_chk('tag_strarr', dv_err(@() set(s, 'Tag', ["a" "b"])));
s.UserData = {1, 'two'};
ix_chk('userdata', ix_show(s.UserData));
s.UserData = [];
s.ErrorOccurredFcn = @(varargin) disp('err');
ix_chk('errfcn_class', class(s.ErrorOccurredFcn));
s.ErrorOccurredFcn = [];
ix_chk('errfcn_empty', isempty(s.ErrorOccurredFcn));
ix_chk('errfcn_bad', dv_err(@() set(s, 'ErrorOccurredFcn', 5)));
ix_chk('errfcn_str', dv_err(@() set(s, 'ErrorOccurredFcn', "disp")));

% read-only ones, by dot and by set
ix_chk('port_set', dv_err(@() set(s, 'Port', "COM21")));
ix_chk('nba_set', dv_err(@() set(s, 'NumBytesAvailable', 3)));
ix_chk('nbw_set', dv_err(@() set(s, 'NumBytesWritten', 3)));
ix_chk('term_set', dv_err(@() set(s, 'Terminator', "CR")));
ix_chk('bafm_set', dv_err(@() set(s, 'BytesAvailableFcnMode', "byte")));
ix_chk('bafc_set', dv_err(@() set(s, 'BytesAvailableFcnCount', 5)));
ix_chk('baf_set', dv_err(@() set(s, 'BytesAvailableFcn', @disp)));
try
    s.Port = "COM21";
    ix_chk('port_dot', 'none');
catch e
    ix_chk('port_dot', [e.identifier ' ## ' ix_flat(e.message)]);
end
try
    s.Terminator = "CR";
    ix_chk('term_dot', 'none');
catch e
    ix_chk('term_dot', [e.identifier ' ## ' ix_flat(e.message)]);
end
try
    s.Bogus = 1;
    ix_chk('bogus_dot_set', 'none');
catch e
    ix_chk('bogus_dot_set', [e.identifier ' ## ' ix_flat(e.message)]);
end
try
    v = s.Bogus; %#ok<NASGU>
    ix_chk('bogus_dot_get', 'none');
catch e
    ix_chk('bogus_dot_get', [e.identifier ' ## ' ix_flat(e.message)]);
end
try
    v = s.baudrate; %#ok<NASGU>
    ix_chk('dot_lower', 'none');
catch e
    ix_chk('dot_lower', [e.identifier ' ## ' ix_flat(e.message)]);
end

% get and set
g = get(s);
ix_chk('get_class', class(g));
ix_chk('get_fields', strjoin(fieldnames(g)', ','));
ix_chk('get_one', ix_show(get(s, 'BaudRate')));
ix_chk('get_lower', ix_show(get(s, 'baudrate')));
ix_chk('get_cell', ix_show(get(s, {'BaudRate', 'Parity'})));
ix_chk('get_bogus', dv_err(@() get(s, 'Bogus')));
set(s, 'Parity', 'even', 'DataBits', 7);
ix_chk('set_multi', ix_show(get(s, {'Parity', 'DataBits'})));
set(s, 'parity', 'odd');
ix_chk('set_lower', ix_show(s.Parity));
ix_chk('set_bad', dv_err(@() set(s, 'Parity', 'bogus')));
ix_chk('set_readonly', dv_err(@() set(s, 'Port', 'COM1')));
ix_chk('set_bogus', dv_err(@() set(s, 'Bogus', 1)));

% counters
ix_chk('nbw_class', class(s.NumBytesWritten));
before = s.NumBytesWritten;
write(s, 1:5, "uint8");
ix_chk('nbw_delta', s.NumBytesWritten - before);
dp(s, 'recv');

% delete
delete(s);
ix_chk('deleted_valid', isvalid(s));
ix_chk('deleted_prop', dv_err(@() s.BaudRate));
ix_chk('deleted_read', dv_err(@() read(s, 1, "uint8")));
ix_chk('deleted_set', dv_err(@() set(s, 'BaudRate', 9600)));
ix_chk('deleted_get', dv_err(@() get(s, 'Port')));
ix_chk('deleted_eq', s == s);
ix_chk('deleted_reopen', isvalid(serialport("COM20", 9600)));

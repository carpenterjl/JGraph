% bt_spp.m -- bluetooth on the ESP32's serial port channel (device classes plan, stage D3): the
% object and its properties, reads and writes through the shared client with the transportlib and
% GenericClient identifiers the channel client keeps, lines, callbacks, the constructor's refusals
% with a paired device, bluetooth() after a clear, and the connection that exists. R2025b records
% against tools/devices/esp32-peer paired as JGraphPeer; JGraph replays against btsim (bt_peer).

global DVLOG
bt_peer();
b = bluetooth("JGraphPeer", 1, "Timeout", 1);
dp(b, 'reset');
flush(b);

% the object
ix_chk('class', class(b));
ix_chk('properties', ix_show(properties(b)));
ix_chk('methods', ix_show(methods(b)));
ix_chk('name', ix_show(b.Name));
ix_chk('address_form', strlength(b.Address) == 12);
props = {'Channel','NumBytesAvailable','NumBytesWritten','ByteOrder','Timeout','Terminator', ...
    'BytesAvailableFcnCount','BytesAvailableFcnMode','UserData'};
for k = 1:numel(props)
    ix_chk(['prop_' props{k}], ix_show(b.(props{k})));
end
ix_chk('prop_BytesAvailableFcn_empty', isempty(b.BytesAvailableFcn));
ix_chk('remote_name', ix_show(b.RemoteName));

% writes and reads
write(b, 1:4, "uint8");
ix_chk('written', ix_show(b.NumBytesWritten));
ix_chk('recv', ix_show(dp(b, 'recv')));
dp(b, 'send 0102030405');
pause(0.3);
ix_chk('nba', ix_show(b.NumBytesAvailable));
ix_chk('read', ix_show(read(b, 2)));
ix_chk('read_prec', ix_show(read(b, 1, "uint16")));
ix_chk('read_rest', ix_show(read(b, b.NumBytesAvailable)));
[v, w] = dv_warn(@() read(b, 3));
ix_chk('read_timeout', ix_show(v));
ix_chk('read_timeout_warn', w);
ix_chk('read_zero', dv_err(@() read(b, 0)));
ix_chk('read_none', dv_err(@() read(b)));
ix_chk('read_bad_prec', dv_err(@() read(b, 1, "uint9")));
ix_chk('write_none', dv_err(@() write(b)));
ix_chk('write_cell', dv_err(@() write(b, {1})));

% lines
dp(b, 'echo on');
writeline(b, "hello");
pause(0.3);
ix_chk('readline', ix_show(readline(b)));
[v, w] = dv_warn(@() readline(b));
ix_chk('readline_timeout', ix_show(v));
ix_chk('readline_timeout_warn', w);
configureTerminator(b, "CR");
writeline(b, "x");
pause(0.3);
ix_chk('cr_bytes', ix_show(read(b, 2, "uint8")));
configureTerminator(b, "LF");
dp(b, 'echo off');

% callbacks
DVLOG = {};
configureCallback(b, "byte", 2, @dv_netevt);
dp(b, 'send 01020304');
pause(0.5);
ix_chk('cb_calls', numel(DVLOG) / 5);
ix_chk('cb_first', ix_show(DVLOG(1:min(5, end))));
configureCallback(b, "off");
flush(b);

% setters
ix_chk('timeout_small', dv_err(@() set(b, 'Timeout', 0.5)));
ix_chk('timeout_neg', dv_err(@() set(b, 'Timeout', -1)));
b.Timeout = 2;
ix_chk('timeout_set', ix_show(b.Timeout));
ix_chk('byteorder_bad', dv_err(@() set(b, 'ByteOrder', "x")));
ix_chk('terminator_set', dv_err(@() set(b, 'Terminator', "CR")));
ix_chk('name_set', dv_err(@() set(b, 'Name', "x")));
ix_chk('channel_set', dv_err(@() set(b, 'Channel', 2)));

% the constructor with a paired device
ix_chk('exists', dv_err(@() bluetooth("JGraphPeer")));
clear b
pause(1);
ix_chk('bad_channel', dv_err(@() bluetooth("JGraphPeer", 300)));
ix_chk('frac_channel', dv_err(@() bluetooth("JGraphPeer", 1.5)));
ix_chk('str_channel', dv_err(@() bluetooth("JGraphPeer", "1")));
ix_chk('bad_nv', dv_err(@() bluetooth("JGraphPeer", 1, "Bogus", 1)));
ix_chk('odd_nv', dv_err(@() bluetooth("JGraphPeer", 1, "Timeout")));
ix_chk('bad_timeout', dv_err(@() bluetooth("JGraphPeer", 1, "Timeout", 0.5)));
b2 = bluetooth();
ix_chk('last_name', ix_show(b2.Name));
ix_chk('last_channel', ix_show(b2.Channel));
b2.ByteOrder = "big-endian";
ix_chk('byteorder_set', ix_show(b2.ByteOrder));
clear b2

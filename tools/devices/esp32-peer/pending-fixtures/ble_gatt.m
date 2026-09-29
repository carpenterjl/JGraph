% ble_gatt.m -- ble on the ESP32's GATT server (device classes plan, stage D3): the scan's entry for
% the peer, the connection and its Services and Characteristics tables, characteristic objects chosen
% by UUID, number and name, reads, writes (types, precisions, the data range), subscriptions and
% DataAvailableFcn, descriptors, and the refusals. R2025b records against tools/devices/esp32-peer;
% JGraph replays against btsim (bt_peer).

global N
bt_peer();
B = blelist("Name", "JGraphPeer", "Timeout", 3);
ix_chk('scan_height', height(B));
ix_chk('scan_name', ix_show(B.Name(1)));
ix_chk('scan_type', ix_show(B.Advertisement(1).Type));
ix_chk('scan_complete_uuids', ix_show(B.Advertisement(1).CompleteServiceUUIDs));
ix_chk('scan_manufacturer', ix_show(B.Advertisement(1).ManufacturerSpecificData));

p = ble("JGraphPeer");
ix_chk('class', class(p));
ix_chk('properties', ix_show(properties(p)));
ix_chk('methods', ix_show(methods(p)));
ix_chk('name', ix_show(p.Name));
ix_chk('connected', ix_show(p.Connected));
ix_chk('service_names', ix_show(p.Services.ServiceName));
ix_chk('service_uuids', ix_show(p.Services.ServiceUUID));
ix_chk('char_services', ix_show(p.Characteristics.ServiceUUID));
ix_chk('char_names', ix_show(p.Characteristics.CharacteristicName));
ix_chk('char_uuids', ix_show(p.Characteristics.CharacteristicUUID));
ix_chk('attributes', ix_show(p.Characteristics.Attributes));
ix_chk('exists', dv_err(@() ble("JGraphPeer")));

% characteristic objects
c = characteristic(p, "Battery Service", "Battery Level");
ix_chk('c_class', class(c));
ix_chk('c_properties', ix_show(properties(c)));
ix_chk('c_methods', ix_show(methods(c)));
ix_chk('c_name', ix_show(c.Name));
ix_chk('c_uuid', ix_show(c.UUID));
ix_chk('c_attributes', ix_show(c.Attributes));
ix_chk('c_same_object', c == characteristic(p, "180F", "2A19"));
ix_chk('c_by_number', c == characteristic(p, 6159, 10777));
ix_chk('c_by_long', c == characteristic(p, "0000180F-0000-1000-8000-00805F9B34FB", "00002A19-0000-1000-8000-00805f9b34fb"));
[v, t] = read(c);
ix_chk('c_read', ix_show(v));
ix_chk('c_read_time', class(t));
ix_chk('c_read_mode', dv_err(@() read(c, "oldest")));
ix_chk('c_read_badmode', dv_err(@() read(c, "sideways")));
ix_chk('c_write', dv_err(@() write(c, 1)));
ix_chk('bad_service', dv_err(@() characteristic(p, "Heart", "zz")));
ix_chk('unknown_service', dv_err(@() characteristic(p, "1800", "2A00")));
ix_chk('unknown_char', dv_err(@() characteristic(p, "180F", "2A00")));
ix_chk('custom_name', dv_err(@() characteristic(p, "Custom", "FFE1")));
ix_chk('ambiguous_char', dv_err(@() characteristic(p, "180D", "Heart Rate")));
ix_chk('two_args', dv_err(@() characteristic(p, "180F")));

% the echo characteristic
x = characteristic(p, "FFE0", "FFE1");
ix_chk('x_name', ix_show(x.Name));
ix_chk('x_attributes', ix_show(x.Attributes));
ix_chk('x_descriptor_names', ix_show(x.Descriptors.DescriptorName));
ix_chk('x_descriptor_uuids', ix_show(x.Descriptors.DescriptorUUID));
write(x, [1 2 3]);
ix_chk('x_read_back', ix_show(read(x)));
write(x, 258, "uint16");
ix_chk('x_uint16', ix_show(read(x)));
write(x, 1, "WithoutResponse", "uint32");
pause(0.2);
ix_chk('x_uint32', ix_show(read(x)));
write(x, 'AB');
ix_chk('x_char', ix_show(read(x)));
ix_chk('x_range', dv_err(@() write(x, 256)));
ix_chk('x_range16', dv_err(@() write(x, 65536, "uint16")));
ix_chk('x_frac', dv_err(@() write(x, 1.5)));
ix_chk('x_logical', dv_err(@() write(x, true)));
ix_chk('x_int16', dv_err(@() write(x, int16(5))));
ix_chk('x_dup', dv_err(@() write(x, 1, "uint8", "uint16")));
ix_chk('x_badopt', dv_err(@() write(x, 1, "sideways")));
ix_chk('x_oldest_unsubscribed', dv_err(@() read(x, "oldest")));
ix_chk('x_badsub', dv_err(@() subscribe(x, "indication")));
N = 0;
x.DataAvailableFcn = @(src, evt) dv_bump();
ix_chk('x_fcn_class', class(x.DataAvailableFcn));
write(x, 7);
write(x, 8);
pause(0.5);
ix_chk('x_callbacks', N);
ix_chk('x_oldest', ix_show(read(x, "oldest")));
ix_chk('x_latest', ix_show(read(x, "latest")));
ix_chk('x_badfcn', dv_err(@() set(x, 'DataAvailableFcn', @(a) a)));
x.DataAvailableFcn = [];
unsubscribe(x);

% descriptors
d = descriptor(x, "Characteristic User Description");
ix_chk('d_class', class(d));
ix_chk('d_uuid', ix_show(d.UUID));
ix_chk('d_read', char(read(d)));
ix_chk('d_same', d == descriptor(x, "2901"));
cccd = descriptor(x, "2902");
ix_chk('cccd_read', ix_show(read(cccd)));
write(cccd, [1 0]);
ix_chk('cccd_after', ix_show(read(cccd)));
write(cccd, [0 0]);
ix_chk('d_bad', dv_err(@() descriptor(x, "2903")));
ix_chk('d_none', dv_err(@() descriptor(c, "2901")));

% heart rate notifications
h = characteristic(p, "Heart Rate", "Heart Rate Measurement");
ix_chk('h_read_class', class(read(h)));
v = read(h, "oldest");
ix_chk('h_first', v(1));
ix_chk('h_second', v(2) >= 60 && v(2) < 100);
unsubscribe(h);
clear h x c d cccd p

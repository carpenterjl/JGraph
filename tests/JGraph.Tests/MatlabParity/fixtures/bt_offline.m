% bt_offline.m -- bluetooth, bluetoothlist, ble and blelist without a device to connect to (device
% classes plan, stage D3): every refusal that comes before a connection, and the shapes of the two
% lists with the radio on. R2025b records against this machine's radio, JGraph against the simulated
% one (bt_peer), so no row depends on which devices are near: only classes, names and refusals.
% R2025b's Channel column is a categorical, which JGraph holds as a cell (div=ADR0186).
% Recording needs a classic device in range (the ESP32 peer serves), or bluetoothlist answers [].

bt_peer();

% bluetoothlist
ix_chk('btlist_short', dv_err(@() bluetoothlist("Timeout", 2)));
ix_chk('btlist_str', dv_err(@() bluetoothlist("Timeout", "5")));
ix_chk('btlist_num', dv_err(@() bluetoothlist(5)));
ix_chk('btlist_three', dv_err(@() bluetoothlist("Timeout", 5, 1)));
ix_chk('btlist_missing', dv_err(@() bluetoothlist("Timeout")));
ix_chk('btlist_bad_name', dv_err(@() bluetoothlist("Bogus", 5)));
L = bluetoothlist("Timeout", 5);
ix_chk('btlist_class', class(L));
ix_chk('btlist_vars', ix_show(L.Properties.VariableNames));
ix_chk('btlist_name_class', class(L.Name));
ix_chk('btlist_address_class', class(L.Address));
ix_chk('btlist_status_class', class(L.Status));
ix_chk('btlist_channel_class', class(L.Channel), 'div=ADR0186');
ix_chk('btlist_address_form', all(strlength(L.Address) == 12));
ix_chk('btlist_address_hex', all(~cellfun(@isempty, regexp(cellstr(L.Address), '^[0-9A-F]{12}$', 'once'))));

% bluetooth
ix_chk('bt_seven', dv_err(@() bluetooth(1, 2, 3, 4, 5, 6, 7)));
ix_chk('bt_num', dv_err(@() bluetooth(5)));
ix_chk('bt_cell', dv_err(@() bluetooth({'a'})));
ix_chk('bt_strarr', dv_err(@() bluetooth(["a" "b"])));
ix_chk('bt_unknown', dv_err(@() bluetooth("NoSuchJGraphDevice")));
ix_chk('bt_unknown_addr', dv_err(@() bluetooth("00:11:22:33:44:55")));
ix_chk('bt_unknown_dash', dv_err(@() bluetooth("00-11-22-33-44-55")));
ix_chk('bt_unknown_plain', dv_err(@() bluetooth("001122334455")));
ix_chk('bt_unknown_char', dv_err(@() bluetooth('NoSuchJGraphDevice', 1)));

% blelist
ix_chk('blelist_badname', dv_err(@() blelist("Name", 5)));
ix_chk('blelist_emptyname', dv_err(@() blelist("Name", "")));
ix_chk('blelist_timeout_zero', dv_err(@() blelist("Timeout", 0)));
ix_chk('blelist_timeout_big', dv_err(@() blelist("Timeout", 10485)));
ix_chk('blelist_timeout_neg', dv_err(@() blelist("Timeout", -1)));
ix_chk('blelist_timeout_str', dv_err(@() blelist("Timeout", "3")));
ix_chk('blelist_timeout_vec', dv_err(@() blelist("Timeout", [1 2])));
ix_chk('blelist_badnv', dv_err(@() blelist("random", 1)));
ix_chk('blelist_badnv_alone', dv_err(@() blelist("random")));
ix_chk('blelist_missing', dv_err(@() blelist("name")));
ix_chk('blelist_missing_timeout', dv_err(@() blelist("Timeout")));
ix_chk('blelist_num_key', dv_err(@() blelist(20)));
ix_chk('blelist_services_empty', dv_err(@() blelist("Services", [])));
ix_chk('blelist_services_bad', dv_err(@() blelist("Services", "zz")));
ix_chk('blelist_services_frac', dv_err(@() blelist("Services", 1.5)));
ix_chk('blelist_services_big', dv_err(@() blelist("Services", 131071)));
ix_chk('blelist_services_5hex', dv_err(@() blelist("Services", "1180D")));
ix_chk('blelist_services_8hex', dv_err(@() blelist("Services", "1234180D")));
ix_chk('blelist_services_custom', dv_err(@() blelist("Services", "Custom")));
ix_chk('blelist_services_type', dv_err(@() blelist("Services", {true})));
[v, w] = dv_warn(@() blelist("Name", "zzzzqqJGraph", "Timeout", 1));
ix_chk('blelist_noname', ix_show(v));
ix_chk('blelist_noname_warn', w);
B = blelist("Timeout", 2);
ix_chk('blelist_class', class(B));
ix_chk('blelist_vars', ix_show(B.Properties.VariableNames));
ix_chk('blelist_index_class', class(B.Index));
ix_chk('blelist_index_counts', isequal(B.Index', 1:height(B)));
ix_chk('blelist_name_class', class(B.Name));
ix_chk('blelist_address_class', class(B.Address));
ix_chk('blelist_rssi_class', class(B.RSSI));
ix_chk('blelist_rssi_sorted', issorted(-B.RSSI));
ix_chk('blelist_adv_class', class(B.Advertisement));
ix_chk('blelist_adv_fields', ix_show(fieldnames(B.Advertisement)));
ix_chk('blelist_adv_type_class', class(B.Advertisement(1).Type));

% ble
ix_chk('ble_none', dv_err(@() ble));
ix_chk('ble_num', dv_err(@() ble(5)));
ix_chk('ble_empty', dv_err(@() ble("")));
ix_chk('ble_empty_char', dv_err(@() ble('')));
ix_chk('ble_cell', dv_err(@() ble({'a'})));
ix_chk('ble_two', dv_err(@() ble("a", "b")));
ix_chk('ble_unknown', dv_err(@() ble("zzzzqqJGraph")));

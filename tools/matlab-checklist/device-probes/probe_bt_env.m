% PROBE_BT_ENV  What this machine's Bluetooth looks like to R2025b, and the refusals that need no device.
dv_px('btlist', 'L = bluetoothlist("Timeout", 5)');
dv_pr('btlist_class', 'class(L)');
try, dv_pr('btlist_vars', 'L.Properties.VariableNames'); catch, end
try, dv_pr('btlist_types', 'varfun(@class, L, ''OutputFormat'', ''cell'')'); catch, end
dv_px('btlist_short', 'bluetoothlist("Timeout", 2)');
dv_px('btlist_num', 'bluetoothlist(5)');
dv_px('btlist_three', 'bluetoothlist("Timeout", 5, 1)');
dv_px('bt_none', 'b = bluetooth');
dv_px('bt_num', 'b = bluetooth(5)');
dv_px('bt_unknown', 'b = bluetooth("NoSuchDevice")');
dv_px('bt_addr_unknown', 'b = bluetooth("00:11:22:33:44:55")');
dv_px('bt_badchan', 'b = bluetooth("NoSuchDevice", 300)');
dv_px('bt_seven', 'b = bluetooth(1,2,3,4,5,6,7)');
dv_px('blelist', 'B = blelist("Timeout", 3)');
dv_pr('blelist_class', 'class(B)');
try, dv_pr('blelist_vars', 'B.Properties.VariableNames'); catch, end
try, dv_pr('blelist_types', 'varfun(@class, B, ''OutputFormat'', ''cell'')'); catch, end
dv_px('blelist_badname', 'blelist("Name", 5)');
dv_px('blelist_badtimeout', 'blelist("Timeout", 0)');
dv_px('blelist_bignum', 'blelist("Timeout", 1000)');
dv_px('blelist_badnv', 'blelist("random", 1)');
dv_px('blelist_missing', 'blelist("name")');
dv_px('blelist_badservice', 'blelist("Services", [])');
dv_px('blelist_noname', 'blelist("Name", "zzzzqq", "Timeout", 2)');
dv_px('ble_none', 'ble');
dv_px('ble_num', 'ble(5)');
dv_px('ble_empty', 'ble("")');
dv_px('ble_unknown', 'ble("zzzzqq")');

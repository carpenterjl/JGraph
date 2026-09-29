% PROBE_BT_SHAPE  The shapes of bluetoothlist's and blelist's answers on real radios.
L = bluetoothlist("Timeout", 5);
dv_pr('bt_height', 'height(L)');
dv_pr('bt_status_raw', 'L.Status(1)');
dv_pr('bt_channel_raw', 'L.Channel(1)');
dv_pr('bt_channel_cats', 'categories(L.Channel)');
dv_pr('bt_address_raw', 'L.Address(1)');
dv_pr('bt_name_raw', 'L.Name(1)');
B = blelist("Timeout", 3);
dv_pr('ble_height_positive', 'height(B) > 0');
a = B.Advertisement(1);
dv_pr('adv_class', 'class(a)');
dv_pr('adv_fields', 'fieldnames(a)');
f = fieldnames(a);
for k = 1:numel(f)
    dv_pr(['adv_' f{k}], ['class(a.' f{k} ')']);
    dv_pr(['adv_' f{k} '_val'], ['a.' f{k}]);
end
dv_pr('ble_index', 'B.Index(1:min(3,end))''');
dv_pr('ble_rssi_class', 'class(B.RSSI)');
dv_pr('ble_sorted_by_rssi', 'issorted(-B.RSSI)');
named = B(B.Name ~= "", :);
dv_pr('ble_named_first', 'named.Name(1)');
dv_px('ble_filter', 'C = blelist("Name", extractBefore(named.Name(1), 3), "Timeout", 3)');
dv_px('ble_services_hr', 'blelist("Services", "180D", "Timeout", 2)');
dv_pr('ble_services_num', 'class(blelist("Services", 0x180D, "Timeout", 2))');
dv_px('ble_services_bad', 'blelist("Services", "zz", "Timeout", 2)');
dv_px('ble_timeout_neg', 'blelist("Timeout", -1)');
dv_px('ble_timeout_str', 'blelist("Timeout", "3")');
dv_px('ble_timeout_vec', 'blelist("Timeout", [1 2])');
dv_px('ble_three', 'blelist("Timeout")');
dv_px('ble_connect_tv', 'b = ble("007C2D247300")');
dv_px('bt_connect_tv', 'b = bluetooth("[TV] SME ATG")');

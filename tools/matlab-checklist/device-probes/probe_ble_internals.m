% PROBE_BLE_INTERNALS  blelib's p-coded constants, its UUID resolver and its data-range check.
C = 'matlabshared.blelib.internal.Constants';
names = {'DefaultScanTimeout','MinScanTimeout','MaxScanTimeout','WriteTypes','WritePrecisions', ...
    'SupportedReadModesNotifyOnly','SupportedReadModesReadOnly','ClientCharacteristicConfigurationUUID'};
for k = 1:numel(names)
    dv_pr(['const_' names{k}], [C '.' names{k}]);
end
try
    mc = meta.class.fromName(C);
    dv_pr('const_all', '{mc.PropertyList.Name}');
catch
end
I = 'matlabshared.blelib.internal.ServicesCharacteristicsDescriptorsInfo.getInstance';
dv_pr('svc_short', [I '.getServiceUUID("180D")']);
dv_pr('svc_lower', [I '.getServiceUUID("180d")']);
dv_pr('svc_num', [I '.getServiceUUID(0x180D)']);
dv_pr('svc_dec', [I '.getServiceUUID(6157)']);
dv_pr('svc_name', [I '.getServiceUUID("Heart Rate")']);
dv_pr('svc_name_lower', [I '.getServiceUUID("heart rate")']);
dv_pr('svc_long', [I '.getServiceUUID("0000180D-0000-1000-8000-00805F9B34FB")']);
dv_pr('svc_long_lower', [I '.getServiceUUID("0000180d-0000-1000-8000-00805f9b34fb")']);
dv_pr('svc_custom', [I '.getServiceUUID("EF680100-9B35-4933-9B10-52FFA9740042")']);
dv_pr('svc_unknown_long', [I '.getServiceUUID("12345678-1234-1234-1234-123456789ABC")']);
dv_pr('svc_unknown_short', [I '.getServiceUUID("FFE0")']);
dv_pr('svc_32bit', [I '.getServiceUUID("0000FFE0")']);
dv_pr('svc_bad', [I '.getServiceUUID("zz")']);
dv_pr('svc_badnum', [I '.getServiceUUID(-1)']);
dv_pr('svc_prefix_name', [I '.getServiceUUID("Heart")']);
dv_pr('shortest_long', [I '.getShortestUUID("0000180D-0000-1000-8000-00805F9B34FB")']);
dv_pr('shortest_custom', [I '.getShortestUUID("12345678-1234-1234-1234-123456789ABC")']);
dv_pr('shortest_short', [I '.getShortestUUID("180D")']);
dv_pr('info_svc', [I '.getServiceInfoByUUID("180D")']);
dv_pr('info_svc_unknown', [I '.getServiceInfoByUUID("FFE0")']);
dv_pr('info_svc_long_unknown', [I '.getServiceInfoByUUID("12345678-1234-1234-1234-123456789ABC")']);
dv_pr('info_chr', [I '.getCharacteristicInfoByUUID("180D", "2A37")']);
dv_pr('info_chr_unknown', [I '.getCharacteristicInfoByUUID("180D", "FFE1")']);
dv_pr('info_chr_other_svc', [I '.getCharacteristicInfoByUUID("FFE0", "2A37")']);
dv_pr('info_desc', [I '.getDescriptorInfoByUUID("2902")']);
dv_pr('info_desc_unknown', [I '.getDescriptorInfoByUUID("ABCD")']);
dv_pr('desc_uuid_name', [I '.getDescriptorUUID("Client Characteristic Configuration")']);
dv_pr('desc_uuid_num', [I '.getDescriptorUUID(0x2902)']);
V = 'matlabshared.blelib.internal.validateDataRange';
dv_pr('range_ok', [V '([1 2 255], "uint8")']);
dv_pr('range_over', [V '(256, "uint8")']);
dv_pr('range_neg', [V '(-1, "uint8")']);
dv_pr('range_frac', [V '(1.5, "uint8")']);
dv_pr('range_int8', [V '(-128, "int8")']);
dv_pr('range_uint16', [V '([1 258], "uint16")']);
dv_pr('range_int16', [V '(-2, "int16")']);
dv_pr('range_uint32', [V '(1, "uint32")']);
dv_pr('range_single', [V '(1.5, "single")']);
dv_pr('range_double', [V '(1.5, "double")']);
dv_pr('range_char', [V '(''AB'', "uint8")']);
dv_pr('range_string', [V '("AB", "uint8")']);
dv_pr('range_logical', [V '(true, "uint8")']);
dv_pr('range_cell', [V '({1}, "uint8")']);
dv_pr('range_empty', [V '([], "uint8")']);
dv_pr('range_int8data', [V '(int8(-1), "uint8")']);
dv_pr('range_nan', [V '(NaN, "uint8")']);
dv_pr('range_matrix', [V '([1 2; 3 4], "uint8")']);
dv_pr('range_big', [V '(1:600, "uint8")']);
dv_pr('evt_class', 'matlabshared.blelib.internal.DataAvailableEventData');
dv_pr('evt_props', 'properties(matlabshared.blelib.internal.DataAvailableEventData)');
dv_pr('substatus', 'enumeration(''matlabshared.blelib.internal.SubscriptionStatus'')');

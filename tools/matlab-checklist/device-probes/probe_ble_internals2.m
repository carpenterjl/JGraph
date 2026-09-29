% PROBE_BLE_INTERNALS2  The info structs' values and more of the data-range check.
I = 'matlabshared.blelib.internal.ServicesCharacteristicsDescriptorsInfo.getInstance';
q = {'getServiceInfoByUUID("180D")', 'getServiceInfoByUUID("FFE0")', ...
    'getServiceInfoByUUID("12345678-1234-1234-1234-123456789ABC")', ...
    'getServiceInfoByUUID("EF680100-9B35-4933-9B10-52FFA9740042")', ...
    'getCharacteristicInfoByUUID("180D", "2A37")', 'getCharacteristicInfoByUUID("180D", "FFE1")', ...
    'getCharacteristicInfoByUUID("FFE0", "2A37")', 'getCharacteristicInfoByUUID("1800", "2A00")', ...
    'getCharacteristicInfoByUUID("180D", "12345678-1234-1234-1234-123456789ABC")', ...
    'getDescriptorInfoByUUID("2902")', 'getDescriptorInfoByUUID("ABCD")', ...
    'getDescriptorInfoByUUID("12345678-1234-1234-1234-123456789ABC")'};
for k = 1:numel(q)
    dv_pr(sprintf('info%d_uuid', k), [I '.' q{k} '.UUID']);
    dv_pr(sprintf('info%d_name', k), [I '.' q{k} '.Name']);
end
dv_pr('chr_uuid_name', [I '.getCharacteristicUUID("Heart Rate Measurement")']);
dv_pr('chr_uuid_num', [I '.getCharacteristicUUID(0x2A37)']);
dv_pr('chr_uuid_bad', [I '.getCharacteristicUUID("zz")']);
dv_pr('desc_uuid_bad', [I '.getDescriptorUUID("zz")']);
dv_pr('svc_ambiguous', [I '.getServiceUUID("Device")']);
dv_pr('svc_char_vec', [I '.getServiceUUID(''180D'')']);
dv_pr('svc_17bit', [I '.getServiceUUID(0x1FFFF)']);
dv_pr('svc_frac', [I '.getServiceUUID(1.5)']);
dv_pr('svc_uint16', [I '.getServiceUUID(uint16(6157))']);
dv_pr('svc_5hex', [I '.getServiceUUID("1180D")']);
dv_pr('svc_8hex', [I '.getServiceUUID("1234180D")']);
V = 'matlabshared.blelib.internal.validateDataRange';
dv_pr('range_uint8class', [V '(uint8(5), "uint8")']);
dv_pr('range_int16class', [V '(int16(5), "uint8")']);
dv_pr('range_uint16class', [V '(uint16(258), "uint16")']);
dv_pr('range_uint16over', [V '(65536, "uint16")']);
dv_pr('range_uint64', [V '(2^53, "uint64")']);
dv_pr('range_uint64class', [V '(intmax("uint64"), "uint64")']);
dv_pr('range_column', [V '([1;2], "uint8")']);
dv_pr('range_uint32over', [V '(2^32, "uint32")']);
dv_pr('range_uint16msg', [V '(-1, "uint16")']);
dv_pr('range_uint32msg', [V '(-1, "uint32")']);
dv_pr('range_uint64msg', [V '(-1, "uint64")']);
dv_pr('range_charhigh', [V '(char(300), "uint8")']);
dv_pr('range_strarr', [V '(["A" "B"], "uint8")']);
dv_pr('range_complex', [V '(1+2i, "uint8")']);
dv_pr('range_inf', [V '(Inf, "uint8")']);

function h = dp_hex(bytes)
% DP_HEX  The bytes of a double row, a char row or a string as the hex text the device's commands take.
if isstring(bytes)
    bytes = char(bytes);
end
h = sprintf('%02X', double(bytes));
end

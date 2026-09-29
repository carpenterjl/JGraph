% serial_io.m -- serialport's read and write (device classes plan, stage D1): what each precision
% writes and reads in both byte orders, the casts (rounding, saturation, NaN, a code above 255),
% partial reads and their warning, the argument refusals of the client and the transport, and
% flush. The peer logs what arrives (dp(s, 'recv')) and sends what it is told (dp(s, 'send HEX')).

s = dv_open();

% what each precision writes
precs = {'uint8','int8','uint16','int16','uint32','int32','uint64','int64','single','double','char','string'};
for k = 1:numel(precs)
    write(s, [1 -2 300 2.5], precs{k});
    ix_chk(['wrote_' precs{k}], ix_show(dp(s, 'recv')));
end
write(s, 'AZ', "uint8");
ix_chk('wrote_char_data', ix_show(dp(s, 'recv')));
write(s, "AZ", "uint8");
ix_chk('wrote_string_data', ix_show(dp(s, 'recv')));
write(s, "AZ", "char");
ix_chk('wrote_string_char', ix_show(dp(s, 'recv')));
write(s, [NaN Inf -Inf], "uint8");
ix_chk('wrote_nan', ix_show(dp(s, 'recv')));
write(s, [NaN Inf -Inf 40000], "int16");
ix_chk('wrote_nan_int16', ix_show(dp(s, 'recv')));
write(s, int8([-1 5]), "uint8");
ix_chk('wrote_int8_data', ix_show(dp(s, 'recv')));
write(s, [1;2;3], "uint8");
ix_chk('wrote_column', ix_show(dp(s, 'recv')));
write(s, 1+2i, "uint8");
ix_chk('wrote_complex', ix_show(dp(s, 'recv')));
write(s, char(955), "char");
ix_chk('wrote_high_char', ix_show(dp(s, 'recv')));
write(s, [2^40 -2^40], "uint32");
ix_chk('wrote_saturated', ix_show(dp(s, 'recv')));
write(s, [0.5 1.5 -0.5 -1.5], "int8");
ix_chk('wrote_rounded', ix_show(dp(s, 'recv')));
write(s, 1, "UINT8");
ix_chk('wrote_upper_prec', ix_show(dp(s, 'recv')));
s.ByteOrder = "big-endian";
write(s, [1 258], "uint16");
ix_chk('wrote_be_uint16', ix_show(dp(s, 'recv')));
write(s, 1.5, "double");
ix_chk('wrote_be_double', ix_show(dp(s, 'recv')));
write(s, -2, "single");
ix_chk('wrote_be_single', ix_show(dp(s, 'recv')));
write(s, 'AB', "char");
ix_chk('wrote_be_char', ix_show(dp(s, 'recv')));
s.ByteOrder = "little-endian";

% write's refusals
ix_chk('write_strarr', dv_err(@() write(s, ["A" "Z"], "string")));
ix_chk('write_logical', dv_err(@() write(s, [true false], "uint8")));
ix_chk('write_matrix', dv_err(@() write(s, [1 2; 3 4], "uint8")));
ix_chk('write_empty', dv_err(@() write(s, [], "uint8")));
ix_chk('write_cell', dv_err(@() write(s, {1}, "uint8")));
ix_chk('write_struct', dv_err(@() write(s, struct('a', 1), "uint8")));
ix_chk('write_no_prec', dv_err(@() write(s, 1)));
ix_chk('write_bad_prec', dv_err(@() write(s, 1, "uint9")));
ix_chk('write_prec_num', dv_err(@() write(s, 1, 8)));
ix_chk('write_prec_partial', dv_err(@() write(s, 1, "uint")));
ix_chk('write_too_many', dv_err(@() write(s, 1, "uint8", 2)));
ix_chk('write_output', ix_try(@() write(s, 1, "uint8")));
ix_chk('nothing_sent', ix_show(dp(s, 'recv')));

% reading
flush(s);
dp(s, sprintf('send %s', dp_hex(0:9)));
pause(0.3);
ix_chk('nba', s.NumBytesAvailable);
ix_chk('read_uint8', ix_show(read(s, 3, "uint8")));
ix_chk('nba_after', s.NumBytesAvailable);
ix_chk('read_char', ix_show(double(read(s, 2, "char"))));
ix_chk('read_char_class', class(read(s, 1, "char")));
ix_chk('read_string_class', class(read(s, 1, "string")));
ix_chk('read_rest', ix_show(read(s, s.NumBytesAvailable, "uint8")));
dp(s, sprintf('send %s', dp_hex([1 0 255 255 0 1 0 0 0 0 0 0 248 63])));
pause(0.3);
ix_chk('read_uint16', ix_show(read(s, 2, "uint16")));
ix_chk('read_int16', ix_show(read(s, 1, "int16")));
ix_chk('read_double', ix_show(read(s, 1, "double")));
dp(s, sprintf('send %s', dp_hex([0 1 255 254])));
pause(0.3);
s.ByteOrder = "big-endian";
ix_chk('read_be_uint16', ix_show(read(s, 1, "uint16")));
ix_chk('read_be_int16', ix_show(read(s, 1, "int16")));
s.ByteOrder = "little-endian";
precs = {'int8','uint32','int32','uint64','int64','single'};
for k = 1:numel(precs)
    dp(s, sprintf('send %s', dp_hex(255 * ones(1, 8))));
    pause(0.2);
    ix_chk(['read_' precs{k}], ix_show(read(s, 1, precs{k})));
    flush(s, "input");
end
dp(s, sprintf('send %s', dp_hex([0 0 192 127 0 0 128 255])));
pause(0.2);
ix_chk('read_single_nan', ix_show(read(s, 2, "single")));
dp(s, sprintf('send %s', dp_hex([104 233 108 108 111])));
pause(0.2);
ix_chk('read_char_high', ix_show(double(read(s, 5, "char"))));
dp(s, sprintf('send %s', dp_hex(double('ab'))));
pause(0.2);
ix_chk('read_string', ix_show(read(s, 2, "string")));
flush(s);

% partial reads and the warning
dp(s, sprintf('send %s', dp_hex(1:3)));
pause(0.3);
[v, w] = dv_warn(@() read(s, 5, "uint8"));
ix_chk('partial', ix_show(v));
ix_chk('partial_warn', w);
[v, w] = dv_warn(@() read(s, 2, "uint8"));
ix_chk('none', ix_show(v));
ix_chk('none_warn', w);
[v, w] = dv_warn(@() read(s, 2, "char"));
ix_chk('none_char', ix_show(v));
[v, w] = dv_warn(@() read(s, 2, "string"));
ix_chk('none_string', ix_show(v));
dp(s, sprintf('send %s', dp_hex(double('ab'))));
pause(0.3);
[v, w] = dv_warn(@() read(s, 3, "string"));
ix_chk('partial_string', ix_show(v));
ix_chk('partial_string_warn', w);
dp(s, sprintf('send %s', dp_hex(1:3)));
pause(0.3);
ix_chk('partial_uint16', dv_err(@() read(s, 2, "uint16")));
ix_chk('partial_uint16_left', s.NumBytesAvailable);
dp(s, sprintf('later 300 %s', dp_hex(7:9)));
t0 = tic;
ix_chk('read_later', ix_show(read(s, 3, "uint8")));
ix_chk('read_later_fast', toc(t0) < 0.9);

% read's refusals
ix_chk('read_zero', dv_err(@() read(s, 0, "uint8")));
ix_chk('read_neg', dv_err(@() read(s, -1, "uint8")));
ix_chk('read_frac', dv_err(@() read(s, 1.5, "uint8")));
ix_chk('read_inf', dv_err(@() read(s, Inf, "uint8")));
ix_chk('read_nan', dv_err(@() read(s, NaN, "uint8")));
ix_chk('read_str_count', dv_err(@() read(s, "2", "uint8")));
ix_chk('read_vec_count', dv_err(@() read(s, [1 2], "uint8")));
ix_chk('read_no_prec', dv_err(@() read(s, 2)));
ix_chk('read_no_count', dv_err(@() read(s)));
ix_chk('read_bad_prec', dv_err(@() read(s, 1, "uint9")));
ix_chk('read_prec_num', dv_err(@() read(s, 1, 8)));
ix_chk('read_too_many', dv_err(@() read(s, 1, "uint8", 3)));

% flush
dp(s, sprintf('send %s', dp_hex(1:4)));
pause(0.3);
flush(s, "input");
ix_chk('flush_input', s.NumBytesAvailable);
dp(s, sprintf('send %s', dp_hex(1:4)));
pause(0.3);
flush(s, "output");
ix_chk('flush_output_keeps_input', s.NumBytesAvailable);
flush(s);
ix_chk('flush_both', s.NumBytesAvailable);
flush(s, "in");
flush(s, "INPUT");
flush(s, 'output');
ix_chk('flush_bad', dv_err(@() flush(s, "bogus")));
ix_chk('flush_num', dv_err(@() flush(s, 1)));
ix_chk('flush_empty', dv_err(@() flush(s, "")));
ix_chk('flush_too_many', dv_err(@() flush(s, "input", "output")));

% a large transfer both ways, and the dot form
s.Timeout = 5;
data = mod(0:9999, 256);
write(s, data, "uint8");
ix_chk('big_write', isequal(dp(s, 'recv'), data));
dp(s, sprintf('send %s', dp_hex(data)));
ix_chk('big_read', isequal(read(s, 10000, "uint8"), data));
dp(s, sprintf('send %s', dp_hex(1:2)));
pause(0.3);
ix_chk('dot_method', ix_show(s.read(2, "uint8")));
s.write(5, "uint8");
ix_chk('dot_write', ix_show(dp(s, 'recv')));

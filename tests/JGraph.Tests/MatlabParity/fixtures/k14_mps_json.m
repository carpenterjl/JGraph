% record: -noFigureWindows
% Compiler and SDK plan, stage K14: the JSON of MATLAB Production Server - mps.json.encode in its
% small and large notations, with NaN and Inf as strings or objects, pretty-printed; the number
% format, both notations' shapes for every class, the types it refuses; mps.json.decode of both
% notations, its coercions and every refusal's identifier and message; encoderequest and
% decoderesponse. Nothing here needs a server. Text is kept to ASCII: a non-ASCII char is shown as
% its code points.
beep('off');

% --- the number format (small notation of a scalar is the bare number) ----------------------------
nums = {0, -0, 1, -1, 5, 10, 100, 1000, 12345, 99999, 100000, 123456, 999999, 1000000, 1234567, 9999999, ...
    10000000, 12345678, 123456789, 1e9, 1e10, 1e15, 2^53, 2^53 + 2, 1e16, 1e17, 1e20, 1e21, 1e22, 1e23, 1e100, ...
    realmax, 0.5, 0.25, 0.1, 0.2, 0.1 + 0.2, 1/3, 2/3, 1e-1, 1e-2, 1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1.5e-7, 1e-10, ...
    1e-300, 5e-324, realmin, 123.456, 1234.5678, 123456.789, 1234567.891, 0.000123456789, 100.5, 1e6 + 0.5, ...
    3.14159, pi, exp(1), sqrt(2), -pi, 1e5 + 0.1, 65536, 1/7, 1e-15, 0.1 * 3, 7.0000000000000009, ...
    1.2345678901234567, 9.87654321e15, 2.5e-5, 0.00001234, -123456789, -1e-7, 1e7 + 1, 12345678.9, 1e300, 1.5e300};
for k = 1:numel(nums)
    chk(sprintf('n_%02d', k), @() mps.json.encode(nums{k}));
end
singles = {single(pi), single(0.1), single(1e10), single(123456789), single(1e-10), single(3), single(16777217), ...
    single(1e21), single(1e-45), single(realmax('single')), single(0.5), single(1234.5), single(1e6), single(1e7), single(999999)};
for k = 1:numel(singles)
    chk(sprintf('s_%02d', k), @() mps.json.encode(singles{k}));
end
ints = {int8(-128), int8(127), uint8(255), int16(32767), uint16(65535), int32(-2147483648), uint32(4294967295), ...
    intmin('int64'), intmax('int64'), intmax('uint64'), int64(-9007199254740993), int64(0), uint64(0), int32(0)};
for k = 1:numel(ints)
    chk(sprintf('i_%02d', k), @() mps.json.encode(ints{k}));
end
chk('n_nan', @() mps.json.encode(NaN));
chk('n_inf', @() mps.json.encode(Inf));
chk('n_ninf', @() mps.json.encode(-Inf));
chk('n_nan_obj', @() mps.json.encode(NaN, 'NanInfType', 'object'));
chk('n_nan_large', @() mps.json.encode(NaN, 'Format', 'large'));
chk('n_nan_large_obj', @() mps.json.encode(NaN, 'Format', 'large', 'NanInfType', 'object'));
chk('n_row_nan_obj', @() mps.json.encode([1 NaN Inf -Inf], 'NanInfType', 'object'));
chk('n_row_nan_large_obj', @() mps.json.encode([1 NaN Inf -Inf], 'Format', 'large', 'NanInfType', 'object'));
chk('n_single_nan', @() mps.json.encode(single([NaN Inf])));
chk('n_single_nan_obj', @() mps.json.encode(single([NaN Inf]), 'NanInfType', 'object'));
chk('n_complex_nan', @() mps.json.encode(complex(NaN, Inf)));
chk('n_complex_nan_obj', @() mps.json.encode(complex(NaN, Inf), 'NanInfType', 'object'));
chk('n_sparse_nan', @() mps.json.encode(sparse([NaN 0])));
chk('n_sparse_nan_obj', @() mps.json.encode(sparse([NaN 0]), 'NanInfType', 'object'));
chk('n_struct_nan', @() mps.json.encode(struct('a', NaN)));
chk('n_struct_nan_obj', @() mps.json.encode(struct('a', NaN), 'NanInfType', 'object'));
chk('n_cell_nan', @() mps.json.encode({NaN}));
chk('n_datetime_nat_ts', @() mps.json.encode(NaT, 'NanInfType', 'object'));

% --- every class in both notations -------------------------------------------------------------
vals = {
 'd_scalar', 5
 'd_row', [1 2 3]
 'd_col', [1; 2; 3]
 'd_2x3', [1 2 3; 4 5 6]
 'd_2x2x2', reshape(1:8, 2, 2, 2)
 'd_1x1x3', reshape(1:3, 1, 1, 3)
 'd_2x1x1', [1; 2]
 'd_0x0', []
 'd_1x0', zeros(1, 0)
 'd_0x3', zeros(0, 3)
 'd_0x0x2', zeros(0, 0, 2)
 'd_neg0', [-0 0]
 'c_scalar', 1 + 2i
 'c_row', [1+2i 3-4i]
 'c_2x2', [1+2i 3-4i; 5i 6]
 'c_zero_imag', complex(1, 0)
 'c_empty', complex(zeros(0, 2))
 'c_single', single(1 + 2i)
 'c_int16', complexInt16()
 'c_nan', complex(NaN, 1)
 'f_single', single([1.5 2])
 'f_int8', int8([-1 2])
 'f_uint8', uint8(200)
 'f_int32_2x2', int32([1 2; 3 4])
 'f_int64_big', int64(9007199254740993)
 'f_uint64_max', intmax('uint64')
 'f_int8_3d', int8(reshape(1:8, 2, 2, 2))
 'f_int8_empty', int8(zeros(0, 1))
 'l_scalar', true
 'l_row', [true false true]
 'l_2x2', logical([1 0; 0 1])
 'l_0x0', logical([])
 'l_3d', true(1, 1, 2)
 'ch_row', 'hello'
 'ch_2x3', ['abc'; 'def']
 'ch_empty', ''
 'ch_1x0', char(zeros(1, 0))
 'ch_0x1', char(zeros(0, 1))
 'ch_quote', 'a"b\c'
 'ch_slash', 'a/b'
 'ch_controls', sprintf('a\tb\nc\rd\be\ff')
 'ch_ctl1', [char(1) char(31) char(127)]
 'ch_col', ['a'; 'b']
 'ch_2x2x2', reshape('abcdefgh', 2, 2, 2)
 'ch_nul', ['a' char(0) 'b']
 'ch_space', ' '
 'st_scalar', "abc"
 'st_row', ["a" "bc"]
 'st_2x2', ["a" "b"; "c" "d"]
 'st_missing', string(missing)
 'st_mixed_missing', ["a" missing "c"]
 'st_empty', ""
 'st_0x0', strings(0, 0)
 'st_1x0', strings(1, 0)
 'st_col', ["a"; "b"]
 'st_quote', "a""b\c"
 'cl_row', {1, 'a', true}
 'cl_col', {1; 'a'}
 'cl_2x2', {1 2; 3 4}
 'cl_empty', {}
 'cl_1x0', cell(1, 0)
 'cl_nested', {{1, 2}, {'x'}}
 'cl_of_empty', {[]}
 'cl_of_empty_cell', {{}}
 'cl_3d', reshape({1, 2, 3, 4}, 1, 2, 2)
 'cl_struct', {struct('a', 1)}
 'cl_string', {"s"}
 'cl_nan', {NaN, Inf}
 'sa_scalar', struct('a', 1, 'b', 'x')
 'sa_1x2', struct('a', {1, 2})
 'sa_2x1', struct('a', {1; 2})
 'sa_2x2', struct('a', {1 2; 3 4})
 'sa_nofields', struct()
 'sa_0x0', struct([])
 'sa_0x1', struct('a', {})
 'sa_nofields_1x2', repmat(struct(), 1, 2)
 'sa_nested', struct('s', struct('t', [1 2]))
 'sa_nested_array', struct('a', {struct('b', 1), struct('b', 2)})
 'sa_matrix', struct('m', [1 2; 3 4])
 'sa_cell', struct('c', {{1, 'a'}})
 'sa_string', struct('s', "str")
 'sa_logical', struct('l', [true false])
 'sa_empty_field', struct('e', [])
 'sa_order', struct('zeta', 1, 'alpha', 2, 'Mid', 3)
 'sa_3d', repmat(struct('a', 1), 1, 1, 2)
 'sp_2x2', sparse([1 0; 0 2])
 'sp_3x4', sparse(2, 3, 7, 3, 4)
 'sp_two', sparse([1 3], [2 4], [5 6], 3, 4)
 'sp_logical', sparse(logical([0 1; 1 0]))
 'sp_complex', complexSparse()
 'sp_empty', sparse(2, 2)
 'sp_0x0', sparse([])
 'sp_col_order', sparse([0 3; 4 0])
 'sp_neg', sparse([-1.5 0 2])
 'en_scalar', matlab.lang.OnOffSwitchState.on
 'en_row', [matlab.lang.OnOffSwitchState.on matlab.lang.OnOffSwitchState.off]
 'en_2x2', repmat(matlab.lang.OnOffSwitchState.off, 2, 2)
 'en_empty', matlab.lang.OnOffSwitchState.empty
 'dt_scalar', datetime(2020, 1, 2)
 'dt_time', datetime(2020, 1, 2, 3, 4, 5)
 'dt_row', datetime(2020, 1, 1:2)
 'dt_nat', NaT
 'dt_utc', datetime(2020, 1, 1, 'TimeZone', 'UTC')
 'dt_zone', datetime(2020, 7, 1, 12, 0, 0, 'TimeZone', 'America/New_York')
 'dt_format', datetime(2020, 1, 2, 'Format', 'uuuu/MM/dd')
 'dt_empty', datetime.empty(0, 1)
 'dt_ms', datetime(2020, 1, 2, 0, 0, 0.5)
 'dt_2x2', repmat(datetime(2020, 1, 2), 2, 2)
 'dt_struct', struct('when', datetime(2020, 1, 2))
 'dt_cell', {datetime(2020, 1, 2)}
 'u_fh', @sin
 'u_fh_anon', @(x) x
 'u_table', table([1; 2], {'a'; 'b'})
 'u_map', containers.Map({'k'}, {1})
 'u_categorical', categorical({'a', 'b'})
 'u_dictionary', dictionary("a", 1)
 'u_duration', seconds(90)
 'u_calendar', calmonths(1)
 'u_timetable', timetable(seconds(1:2)', [1; 2])
 'u_cell_table', {table(1)}
 'u_struct_fh', struct('f', @sin)
 'u_string_in_cell_fh', {'a', @sin}
 'u_inputparser', made(@() inputParser)
 'u_mexception', MException('a:b', 'c')
};
for k = 1:size(vals, 1)
    [key, v] = vals{k, :};
    if strcmp(key, 'u_inputparser')
        % The refusal on one line: both engines refuse, JGraph naming the stand-in's class (a divergence).
        chk(['e_' key '_small'], @() encq(v, {}));
        chk(['e_' key '_large'], @() encq(v, {'Format', 'large'}));
    else
        chk(['e_' key '_small'], @() ascii(mps.json.encode(v)));
        chk(['e_' key '_large'], @() ascii(mps.json.encode(v, 'Format', 'large')));
    end
    chk(['e_' key '_round'], @() round2(v));
end
chk('e_unicode_small', @() mat2str(double(mps.json.encode(char([945 946 8364])))));
chk('e_unicode_large', @() mat2str(double(mps.json.encode(char([945 946 8364]), 'Format', 'large'))));
chk('e_unicode_string', @() mat2str(double(mps.json.encode(string(char([233 8364]))))));
chk('e_unicode_surrogate', @() mat2str(double(mps.json.encode(char([55357 56832])))));
chk('e_unicode_round', @() round2(char([945 946 8364])));
chk('e_unicode_class', @() class(mps.json.encode('x')));
chk('e_string_in', @() mps.json.encode("x", 'Format', "large"));

% --- pretty printing -----------------------------------------------------------------------------
pretty = {
 'p_scalar', 5
 'p_row', [1 2]
 'p_2x2', [1 2; 3 4]
 'p_struct', struct('a', [1 2], 'b', 'x')
 'p_nested', struct('s', struct('t', {{1, 'a'}}))
 'p_cell', {1, {2, 3}}
 'p_empty', []
 'p_emptystruct', struct()
 'p_emptycell', {}
 'p_string', "abc"
 'p_sparse', sparse([1 0; 0 2])
 'p_complex', 1 + 2i
 'p_nan', [1 NaN]
 'p_struct_array', struct('a', {1, 2})
 'p_char', 'x'
 'p_logical', [true false]
};
for k = 1:size(pretty, 1)
    [key, v] = pretty{k, :};
    chk([key '_small'], @() lines(mps.json.encode(v, 'PrettyPrint', true)));
    chk([key '_large'], @() lines(mps.json.encode(v, 'PrettyPrint', true, 'Format', 'large')));
end
chk('p_nan_obj', @() lines(mps.json.encode([1 NaN], 'PrettyPrint', true, 'NanInfType', 'object')));
chk('p_class', @() class(mps.json.encode(5, 'PrettyPrint', true)));

% --- the options --------------------------------------------------------------------------------
chk('o_none', @() mps.json.encode());
chk('o_format_bad', @() mps.json.encode(5, 'Format', 'tiny'));
chk('o_format_upper', @() mps.json.encode([1 2], 'Format', 'LARGE'));
chk('o_format_prefix', @() mps.json.encode([1 2], 'Format', 'l'));
chk('o_format_prefix_s', @() mps.json.encode([1 2], 'Format', 'S'));
chk('o_format_string', @() mps.json.encode([1 2], "Format", "large"));
chk('o_format_number', @() mps.json.encode(5, 'Format', 1));
chk('o_format_empty', @() mps.json.encode(5, 'Format', ''));
chk('o_nan_bad', @() mps.json.encode(5, 'NanInfType', 'text'));
chk('o_nan_upper', @() mps.json.encode(NaN, 'NanInfType', 'OBJECT'));
chk('o_nan_prefix', @() mps.json.encode(NaN, 'NanInfType', 'o'));
chk('o_nan_case', @() mps.json.encode(NaN, 'naninftype', 'object'));
chk('o_nan_fullcase', @() mps.json.encode(NaN, 'NaNInfType', 'object'));
chk('o_pretty_number', @() mps.json.encode(5, 'PrettyPrint', 1));
chk('o_pretty_text', @() mps.json.encode(5, 'PrettyPrint', 'true'));
chk('o_pretty_vector', @() mps.json.encode(5, 'PrettyPrint', [true true]));
chk('o_pretty_false', @() mps.json.encode([1 2], 'PrettyPrint', false));
chk('o_unknown', @() mps.json.encode(5, 'Indent', 2));
chk('o_missing_value', @() mps.json.encode(5, 'Format'));
chk('o_name_number', @() mps.json.encode(5, 7, 'large'));
chk('o_prefix_name', @() mps.json.encode([1 2], 'Form', 'large'));
chk('o_twice', @() mps.json.encode([1 2], 'Format', 'large', 'Format', 'small'));
chk('o_decode_none', @() mps.json.decode());
chk('o_decode_two', @() mps.json.decode('5', '6'));
chk('o_decode_number', @() mps.json.decode(5));
chk('o_decode_cell', @() mps.json.decode({'5'}));
chk('o_decode_string', @() dec("[[1,2]]"));
chk('o_decode_stringarray', @() mps.json.decode(["5" "6"]));
chk('o_decode_charmatrix', @() mps.json.decode(['5'; '6']));
chk('o_decode_empty', @() mps.json.decode(''));
chk('o_decode_missing', @() mps.json.decode(string(missing)));
chk('o_decode_class', @() class(mps.json.decode("5")));
chk('o_decode_nargout', @() nargoutq('mps.json.decode'));  % R2025b refuses a built-in method's count; JGraph answers
chk('o_encode_nargout', @() nargout('mps.json.encode'));
chk('o_request_nargout', @() nargout('mps.json.encoderequest'));
chk('o_response_nargout', @() nargout('mps.json.decoderesponse'));

% --- decode: small notation -----------------------------------------------------------------------
small = {
 'x_5', '5'
 'x_neg', '-5'
 'x_frac', '1.5'
 'x_exp', '1E2'
 'x_exp_lower', '1e-2'
 'x_exp_plus', '1e+2'
 'x_exp_big', '1e400'
 'x_exp_negbig', '-1e400'
 'x_denormal', '4.9e-324'
 'x_tiny', '1e-400'
 'x_neg0', '-0'
 'x_long', '12345678901234567890'
 'x_long_frac', '0.12345678901234567890123'
 'x_zero', '0'
 'x_leadzero', '01'
 'x_dotstart', '.5'
 'x_plus', '+1'
 'x_hex', '0x10'
 'x_dotend', '1.'
 'x_dotexp', '1.e5'
 'x_expend', '1e'
 'x_expsign', '1e+'
 'x_expdot', '1e1.5'
 'x_nan_word', 'NaN'
 'x_inf_word', 'Infinity'
 'x_true', 'true'
 'x_false', 'false'
 'x_tru', 'tru'
 'x_True', 'True'
 'x_null', 'null'
 'x_nul', 'nul'
 'x_empty_arr', '[]'
 'x_arr_1', '[1]'
 'x_arr_123', '[1,2,3]'
 'x_arr_row', '[[1,2,3]]'
 'x_arr_col', '[[1],[2],[3]]'
 'x_arr_2x2', '[[1,2],[3,4]]'
 'x_arr_3d', '[[[1,2],[3,4]],[[5,6],[7,8]]]'
 'x_arr_1x1x1', '[[[1]]]'
 'x_arr_4d', '[[[[1,2]]]]'
 'x_arr_1x1x2', '[[[1,2]]]'
 'x_arr_empty_in', '[[]]'
 'x_arr_empty_in2', '[[],[]]'
 'x_arr_empty_deep', '[[[]]]'
 'x_arr_ragged', '[[1,2],[3]]'
 'x_arr_ragged2', '[[1,2],[3,4,5]]'
 'x_arr_depths', '[1,[2,3]]'
 'x_arr_depths2', '[[1],[[2]]]'
 'x_arr_mixed', '[1,"a"]'
 'x_arr_mixed_bool', '[[true,1]]'
 'x_arr_mixed_bool2', '[1,true]'
 'x_arr_bool', '[true,false]'
 'x_arr_bool_row', '[[true,false]]'
 'x_arr_bool_2x2', '[[true,false],[false,true]]'
 'x_arr_strings', '["a","bc"]'
 'x_arr_string1', '["a"]'
 'x_arr_null', '[null]'
 'x_arr_nan', '[{"mwdata":"NaN"}]'
 'x_arr_nan_row', '[[{"mwdata":"Inf"},2]]'
 'x_arr_nan_str', '[{"mwdata":"NaN"},"a"]'
 'x_arr_nan_bool', '[{"mwdata":"NaN"},true]'
 'x_arr_nan_obj_bad', '[{"mwdata":"nan"}]'
 'x_arr_nan_obj_extra', '[{"mwdata":"NaN","x":1}]'
 'x_arr_objs', '[{"a":1},{"a":2}]'
 'x_arr_objs2', '[{"a":1},{"b":2}]'
 'x_arr_trailing', '[1,]'
 'x_arr_leading', '[,1]'
 'x_arr_unclosed', '[1,2'
 'x_arr_space', ' [ 1 , 2 ] '
 'x_arr_newline', sprintf('[\n1,\n2\n]')
 'x_str', '"abc"'
 'x_str_empty', '""'
 'x_str_nan', '"NaN"'
 'x_str_escapes', '"a\nb\tc\"d\\e\/f\bg\fh\ri"'
 'x_str_unicode', '"Aé"'
 'x_str_unicode_bad', '"\u12"'
 'x_str_unicode_bad2', '"\uZZZZ"'
 'x_str_escape_bad', '"\q"'
 'x_str_surrogate', '"😀"'
 'x_str_lone_surrogate', '"\ud83d"'
 'x_str_unclosed', '"abc'
 'x_str_raw_newline', sprintf('"a\nb"')
 'x_str_raw_tab', sprintf('"a\tb"')
 'x_str_control', ['"a' char(1) 'b"']
 'x_obj_empty', '{}'
 'x_obj', '{"a":1,"b":"x"}'
 'x_obj_nested', '{"a":[1,2],"b":{"c":true}}'
 'x_obj_arrays', '{"a":[[1,2],[3,4]],"b":[]}'
 'x_obj_empty_field', '{"a":[]}'
 'x_obj_empty_obj', '{"a":{}}'
 'x_obj_null', '{"a":null}'
 'x_obj_dup', '{"a":1,"a":2}'
 'x_obj_order', '{"zeta":1,"alpha":2,"Mid":3}'
 'x_obj_keyword', '{"end":1}'
 'x_obj_nonstring_key', '{1:2}'
 'x_obj_missing_value', '{"a":}'
 'x_obj_missing_colon', '{"a" 1}'
 'x_obj_trailing', '{"a":1,}'
 'x_obj_unclosed', '{"a":1'
 'x_obj_large_in_small', '{"a":{"mwdata":[1,2],"mwsize":[1,2],"mwtype":"double"}}'
 'x_obj_large_in_small_struct', '{"a":{"mwdata":{"b":[1,2]},"mwsize":[1,2],"mwtype":"struct"}}'
 'x_obj_nan_in_small', '{"a":{"mwdata":"NaN"}}'
 'x_obj_struct_arr', '{"mwdata":{"a":[1,2]},"mwsize":[1,2],"mwtype":"struct"}'
 'x_trailing_garbage', '5 x'
 'x_two_values', '5 6'
 'x_only_space', '   '
 'x_comment', '/* c */ 5'
};
for k = 1:size(small, 1)
    [key, js] = small{k, :};
    chk(key, @() dec(js));
end

% --- decode: large notation -------------------------------------------------------------------------
large = {
 'y_double', '{"mwdata":[1,2,3,4],"mwsize":[2,2],"mwtype":"double"}'
 'y_double_3d', '{"mwdata":[1,2,3,4,5,6,7,8],"mwsize":[2,2,2],"mwtype":"double"}'
 'y_double_scalar', '{"mwdata":[5],"mwsize":[1,1],"mwtype":"double"}'
 'y_double_bare', '{"mwdata":5,"mwsize":[1,1],"mwtype":"double"}'
 'y_double_empty', '{"mwdata":[],"mwsize":[0,0],"mwtype":"double"}'
 'y_double_1x0', '{"mwdata":[],"mwsize":[1,0],"mwtype":"double"}'
 'y_double_0x0x2', '{"mwdata":[],"mwsize":[0,0,2],"mwtype":"double"}'
 'y_double_nan', '{"mwdata":["NaN","Inf","-Inf"],"mwsize":[1,3],"mwtype":"double"}'
 'y_double_nan_obj', '{"mwdata":[{"mwdata":"NaN"},1],"mwsize":[1,2],"mwtype":"double"}'
 'y_double_nan_lower', '{"mwdata":["nan"],"mwsize":[1,1],"mwtype":"double"}'
 'y_double_text', '{"mwdata":["x"],"mwsize":[1,1],"mwtype":"double"}'
 'y_double_bool', '{"mwdata":[true],"mwsize":[1,1],"mwtype":"double"}'
 'y_double_null', '{"mwdata":[null],"mwsize":[1,1],"mwtype":"double"}'
 'y_double_nested', '{"mwdata":[[1]],"mwsize":[1,1],"mwtype":"double"}'
 'y_double_obj', '{"mwdata":{"real":[1],"imag":[2]},"mwsize":[1,1],"mwtype":"double","mwcomplex":true}'
 'y_double_mismatch', '{"mwdata":[1,2],"mwsize":[1,1],"mwtype":"double"}'
 'y_double_mismatch2', '{"mwdata":[1],"mwsize":[1,2],"mwtype":"double"}'
 'y_double_order', '{"mwtype":"double","mwsize":[2,1],"mwdata":[1,2]}'
 'y_double_extra', '{"mwdata":[1,2,3],"mwsize":[1,3],"mwtype":"double","extra":5}'
 'y_double_no_type', '{"mwdata":[1],"mwsize":[1,1]}'
 'y_double_no_size', '{"mwdata":[1],"mwtype":"double"}'
 'y_double_no_data', '{"mwsize":[1,1],"mwtype":"double"}'
 'y_type_only', '{"mwtype":"double"}'
 'y_size_only', '{"mwsize":[1,1]}'
 'y_data_only', '{"mwdata":[1]}'
 'y_data_nan_only', '{"mwdata":"NaN"}'
 'y_data_inf_only', '{"mwdata":"Inf"}'
 'y_data_ninf_only', '{"mwdata":"-Inf"}'
 'y_data_other_only', '{"mwdata":"x"}'
 'y_size_text', '{"mwdata":[1],"mwsize":"x","mwtype":"double"}'
 'y_size_one', '{"mwdata":[1],"mwsize":[1],"mwtype":"double"}'
 'y_size_neg', '{"mwdata":[1],"mwsize":[-1,1],"mwtype":"double"}'
 'y_size_frac', '{"mwdata":[1],"mwsize":[1.5,1],"mwtype":"double"}'
 'y_size_empty', '{"mwdata":[],"mwsize":[],"mwtype":"double"}'
 'y_size_bool', '{"mwdata":[1],"mwsize":[true,true],"mwtype":"double"}'
 'y_size_nested', '{"mwdata":[1],"mwsize":[[1,1]],"mwtype":"double"}'
 'y_size_trailing1', '{"mwdata":[1,2],"mwsize":[1,2,1],"mwtype":"double"}'
 'y_type_number', '{"mwdata":[1],"mwsize":[1,1],"mwtype":5}'
 'y_type_empty', '{"mwdata":[1],"mwsize":[1,1],"mwtype":""}'
 'y_type_case', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"Double"}'
 'y_type_unknown', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"nosuchclass_k14"}'
 'y_type_cellword', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"sparse"}'
 'y_complex_number', '{"mwdata":[1,2],"mwsize":[1,1],"mwtype":"double","mwcomplex":1}'
 'y_complex_text', '{"mwdata":[1,2],"mwsize":[1,1],"mwtype":"double","mwcomplex":"true"}'
 'y_complex_false', '{"mwdata":[1,2],"mwsize":[1,2],"mwtype":"double","mwcomplex":false}'
 'y_complex_ok', '{"mwcomplex":true,"mwdata":[1,2],"mwsize":[1,1],"mwtype":"double"}'
 'y_complex_row', '{"mwcomplex":true,"mwdata":[1,2,3,-4],"mwsize":[1,2],"mwtype":"double"}'
 'y_complex_odd', '{"mwcomplex":true,"mwdata":[1,2,3],"mwsize":[1,2],"mwtype":"double"}'
 'y_complex_short', '{"mwcomplex":true,"mwdata":[1],"mwsize":[1,1],"mwtype":"double"}'
 'y_complex_pairs', '{"mwcomplex":true,"mwdata":[{"real":1,"imag":2}],"mwsize":[1,1],"mwtype":"double"}'
 'y_complex_nan', '{"mwcomplex":true,"mwdata":["NaN",1],"mwsize":[1,1],"mwtype":"double"}'
 'y_complex_int8', '{"mwcomplex":true,"mwdata":[1,2],"mwsize":[1,1],"mwtype":"int8"}'
 'y_complex_logical', '{"mwcomplex":true,"mwdata":[true,false],"mwsize":[1,1],"mwtype":"logical"}'
 'y_complex_char', '{"mwcomplex":true,"mwdata":["ab"],"mwsize":[1,2],"mwtype":"char"}'
 'y_complex_single', '{"mwcomplex":true,"mwdata":[1,2],"mwsize":[1,1],"mwtype":"single"}'
 'y_complex_empty', '{"mwcomplex":true,"mwdata":[],"mwsize":[0,0],"mwtype":"double"}'
 'y_single', '{"mwdata":[1.5,2],"mwsize":[1,2],"mwtype":"single"}'
 'y_single_nan', '{"mwdata":[{"mwdata":"NaN"}],"mwsize":[1,1],"mwtype":"single"}'
 'y_single_big', '{"mwdata":[1e40],"mwsize":[1,1],"mwtype":"single"}'
 'y_int8', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_text', '{"mwdata":["x"],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_bool', '{"mwdata":[true],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_frac', '{"mwdata":[1.5],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_over', '{"mwdata":[200],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_nan', '{"mwdata":["NaN"],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_nan_obj', '{"mwdata":[{"mwdata":"NaN"}],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_inf', '{"mwdata":["Inf"],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_exp', '{"mwdata":[1e2],"mwsize":[1,1],"mwtype":"int8"}'
 'y_int8_neg0', '{"mwdata":[-0],"mwsize":[1,1],"mwtype":"int8"}'
 'y_uint8_300', '{"mwdata":[300],"mwsize":[1,1],"mwtype":"uint8"}'
 'y_uint8_neg', '{"mwdata":[-1],"mwsize":[1,1],"mwtype":"uint8"}'
 'y_uint8_frac', '{"mwdata":[1.0],"mwsize":[1,1],"mwtype":"uint8"}'
 'y_int32_frac', '{"mwdata":[1.7],"mwsize":[1,1],"mwtype":"int32"}'
 'y_int32_over', '{"mwdata":[3e9],"mwsize":[1,1],"mwtype":"int32"}'
 'y_int64_big', '{"mwdata":[9007199254740993],"mwsize":[1,1],"mwtype":"int64"}'
 'y_int64_over', '{"mwdata":[1e19],"mwsize":[1,1],"mwtype":"int64"}'
 'y_int64_neg', '{"mwdata":[-9223372036854775808],"mwsize":[1,1],"mwtype":"int64"}'
 'y_uint64_max', '{"mwdata":[18446744073709551615],"mwsize":[1,1],"mwtype":"uint64"}'
 'y_uint64_over', '{"mwdata":[18446744073709551616],"mwsize":[1,1],"mwtype":"uint64"}'
 'y_uint64_exp', '{"mwdata":[1e19],"mwsize":[1,1],"mwtype":"uint64"}'
 'y_logical', '{"mwdata":[true,false],"mwsize":[1,2],"mwtype":"logical"}'
 'y_logical_empty', '{"mwdata":[],"mwsize":[0,0],"mwtype":"logical"}'
 'y_char', '{"mwdata":["ab"],"mwsize":[1,2],"mwtype":"char"}'
 'y_char_matrix', '{"mwdata":["adbecf"],"mwsize":[2,3],"mwtype":"char"}'
 'y_char_3d', '{"mwdata":["abcdefgh"],"mwsize":[2,2,2],"mwtype":"char"}'
 'y_char_rows', '{"mwdata":["ab","cd"],"mwsize":[2,2],"mwtype":"char"}'
 'y_char_empty', '{"mwdata":[""],"mwsize":[0,0],"mwtype":"char"}'
 'y_char_1x0', '{"mwdata":[""],"mwsize":[1,0],"mwtype":"char"}'
 'y_char_unicode', '{"mwdata":["α"],"mwsize":[1,1],"mwtype":"char"}'
 'y_char_null_in', '{"mwdata":["a\u0000b"],"mwsize":[1,3],"mwtype":"char"}'
 'y_string', '{"mwdata":["a","b"],"mwsize":[1,2],"mwtype":"string"}'
 'y_string_1', '{"mwdata":["ab"],"mwsize":[1,1],"mwtype":"string"}'
 'y_string_missing', '{"mwdata":[{"mwdata":"missing"}],"mwsize":[1,1],"mwtype":"string"}'
 'y_string_missing_mixed', '{"mwdata":["a",{"mwdata":"missing"}],"mwsize":[1,2],"mwtype":"string"}'
 'y_string_empty', '{"mwdata":[],"mwsize":[0,0],"mwtype":"string"}'
 'y_string_2x2', '{"mwdata":["a","c","b","d"],"mwsize":[2,2],"mwtype":"string"}'
 'y_cell', '{"mwdata":[{"mwdata":[1],"mwsize":[1,1],"mwtype":"double"},{"mwdata":["x"],"mwsize":[1,1],"mwtype":"char"}],"mwsize":[1,2],"mwtype":"cell"}'
 'y_cell_small_items', '{"mwdata":[1,2],"mwsize":[1,2],"mwtype":"cell"}'
 'y_cell_small_mixed', '{"mwdata":[1,"a",true,[1,2],{"a":1}],"mwsize":[1,5],"mwtype":"cell"}'
 'y_cell_nested_small', '{"mwdata":[[1,2]],"mwsize":[1,1],"mwtype":"cell"}'
 'y_cell_empty_item', '{"mwdata":[[]],"mwsize":[1,1],"mwtype":"cell"}'
 'y_cell_empty', '{"mwdata":[],"mwsize":[0,0],"mwtype":"cell"}'
 'y_cell_2x2', '{"mwdata":[1,3,2,4],"mwsize":[2,2],"mwtype":"cell"}'
 'y_struct', '{"mwdata":{"a":[{"mwdata":[1],"mwsize":[1,1],"mwtype":"double"}]},"mwsize":[1,1],"mwtype":"struct"}'
 'y_struct_small_fields', '{"mwdata":{"a":[1,2]},"mwsize":[1,2],"mwtype":"struct"}'
 'y_struct_small_scalar', '{"mwdata":{"a":[1]},"mwsize":[1,1],"mwtype":"struct"}'
 'y_struct_empty', '{"mwdata":{},"mwsize":[1,1],"mwtype":"struct"}'
 'y_struct_0x0', '{"mwdata":{},"mwsize":[0,0],"mwtype":"struct"}'
 'y_struct_arr_data', '{"mwdata":[],"mwsize":[0,0],"mwtype":"struct"}'
 'y_struct_arr_data2', '{"mwdata":[{"a":1}],"mwsize":[1,1],"mwtype":"struct"}'
 'y_struct_2x2', '{"mwdata":{"a":[1,3,2,4]},"mwsize":[2,2],"mwtype":"struct"}'
 'y_struct_nested', '{"mwdata":{"s":[{"mwdata":{"t":[{"mwdata":[1,2],"mwsize":[1,2],"mwtype":"double"}]},"mwsize":[1,1],"mwtype":"struct"}]},"mwsize":[1,1],"mwtype":"struct"}'
 'y_sparse', '{"mwdata":[[0,1],[0,1],[1,2]],"mwsize":[2,2],"mwsparse":true,"mwtype":"double"}'
 'y_sparse_3x4', '{"mwdata":[[1],[2],[7]],"mwsize":[3,4],"mwsparse":true,"mwtype":"double"}'
 'y_sparse_empty', '{"mwdata":[[],[],[]],"mwsize":[2,2],"mwsparse":true,"mwtype":"double"}'
 'y_sparse_logical', '{"mwdata":[[1,0],[0,1]],"mwsize":[2,2],"mwsparse":true,"mwtype":"logical"}'
 'y_sparse_complex', '{"mwcomplex":true,"mwdata":[[0,1],[0,1],[1,2,3,0]],"mwsize":[2,2],"mwsparse":true,"mwtype":"double"}'
 'y_sparse_false', '{"mwdata":[1,2],"mwsize":[1,2],"mwsparse":false,"mwtype":"double"}'
 'y_sparse_int8', '{"mwdata":[[0],[0],[1]],"mwsize":[1,1],"mwsparse":true,"mwtype":"int8"}'
 'y_sparse_char', '{"mwdata":[[0],[0],[1]],"mwsize":[1,1],"mwsparse":true,"mwtype":"char"}'
 'y_enum', '{"mwdata":["on"],"mwsize":[1,1],"mwtype":"matlab.lang.OnOffSwitchState"}'
 'y_enum_row', '{"mwdata":["on","off"],"mwsize":[1,2],"mwtype":"matlab.lang.OnOffSwitchState"}'
 'y_enum_bad', '{"mwdata":["maybe"],"mwsize":[1,1],"mwtype":"matlab.lang.OnOffSwitchState"}'
 'y_enum_number', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"matlab.lang.OnOffSwitchState"}'
 'y_datetime', '{"mwdata":{"TimeStamp":1.5779232E+12},"mwmetadata":{"Format":"dd-MMM-uuuu","TimeZone":""},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_large', '{"mwdata":{"TimeStamp":{"mwdata":[1.5779232E+12],"mwsize":[1,1],"mwtype":"double"}},"mwmetadata":{"Format":{"mwdata":["dd-MMM-uuuu"],"mwsize":[1,11],"mwtype":"char"},"TimeZone":{"mwdata":[""],"mwsize":[0,0],"mwtype":"char"}},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_row', '{"mwdata":{"TimeStamp":[[1.5778368E+12,1.5779232E+12]]},"mwmetadata":{"Format":"dd-MMM-uuuu","TimeZone":""},"mwsize":[1,2],"mwtype":"datetime"}'
 'y_datetime_utc', '{"mwdata":{"TimeStamp":1.5778368E+12},"mwmetadata":{"Format":"dd-MMM-uuuu","TimeZone":"UTC"},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_nat', '{"mwdata":{"TimeStamp":{"mwdata":"NaN"}},"mwmetadata":{"Format":"dd-MMM-uuuu HH:mm:ss","TimeZone":""},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_format', '{"mwdata":{"TimeStamp":1.5779232E+12},"mwmetadata":{"Format":"uuuu","TimeZone":""},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_no_meta', '{"mwdata":{"TimeStamp":1.5779232E+12},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_no_zone', '{"mwdata":{"TimeStamp":1.5779232E+12},"mwmetadata":{"Format":"dd-MMM-uuuu"},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_logical_number', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"logical"}'
 'y_logical_text', '{"mwdata":["true"],"mwsize":[1,1],"mwtype":"logical"}'
 'y_logical_null', '{"mwdata":[null],"mwsize":[1,1],"mwtype":"logical"}'
 'y_char_bare', '{"mwdata":"ab","mwsize":[1,2],"mwtype":"char"}'
 'y_char_number', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"char"}'
 'y_char_empty_arr', '{"mwdata":[],"mwsize":[0,0],"mwtype":"char"}'
 'y_char_short', '{"mwdata":["a"],"mwsize":[1,2],"mwtype":"char"}'
 'y_char_long', '{"mwdata":["abc"],"mwsize":[1,2],"mwtype":"char"}'
 'y_string_bare', '{"mwdata":"ab","mwsize":[1,1],"mwtype":"string"}'
 'y_string_number', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"string"}'
 'y_cell_null_item', '{"mwdata":[null],"mwsize":[1,1],"mwtype":"cell"}'
 'y_cell_bare', '{"mwdata":1,"mwsize":[1,1],"mwtype":"cell"}'
 'y_cell_mismatch', '{"mwdata":[1,2,3],"mwsize":[1,2],"mwtype":"cell"}'
 'y_struct_bare_field', '{"mwdata":{"a":1},"mwsize":[1,1],"mwtype":"struct"}'
 'y_struct_field_mismatch', '{"mwdata":{"a":[1,2,3]},"mwsize":[1,2],"mwtype":"struct"}'
 'y_struct_fields_differ', '{"mwdata":{"a":[1,2],"b":[1]},"mwsize":[1,2],"mwtype":"struct"}'
 'y_struct_0x0_fields', '{"mwdata":{"a":[]},"mwsize":[0,0],"mwtype":"struct"}'
 'y_struct_field_text', '{"mwdata":{"a":"x"},"mwsize":[1,1],"mwtype":"struct"}'
 'y_struct_field_null', '{"mwdata":{"a":null},"mwsize":[1,1],"mwtype":"struct"}'
 'y_struct_field_empty', '{"mwdata":{"a":[[]]},"mwsize":[1,1],"mwtype":"struct"}'
 'y_struct_field_nan', '{"mwdata":{"a":[{"mwdata":"NaN"}]},"mwsize":[1,1],"mwtype":"struct"}'
 'y_enum_empty', '{"mwdata":[],"mwsize":[0,0],"mwtype":"matlab.lang.OnOffSwitchState"}'
 'y_datetime_bad_zone', '{"mwdata":{"TimeStamp":1.5779232E+12},"mwmetadata":{"Format":"dd-MMM-uuuu","TimeZone":"Nowhere/Here"},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_bad_format', '{"mwdata":{"TimeStamp":1.5779232E+12},"mwmetadata":{"Format":"QQQQ-zz","TimeZone":""},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_mismatch', '{"mwdata":{"TimeStamp":[[1,2]]},"mwmetadata":{"Format":"dd-MMM-uuuu","TimeZone":""},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_bad_ts', '{"mwdata":{"TimeStamp":"x"},"mwmetadata":{"Format":"dd-MMM-uuuu","TimeZone":""},"mwsize":[1,1],"mwtype":"datetime"}'
 'y_datetime_arr_data', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"datetime"}'
 'y_metadata_double', '{"mwdata":[1],"mwmetadata":{"x":1},"mwsize":[1,1],"mwtype":"double"}'
 'y_type_fh', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"function_handle"}'
 'y_type_table', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"table"}'
 'y_type_duration', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"duration"}'
 'y_type_map', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"containers.Map"}'
 'y_type_categorical', '{"mwdata":["x"],"mwsize":[1,1],"mwtype":"categorical"}'
 'y_type_complexword', '{"mwdata":[1],"mwsize":[1,1],"mwtype":"complex"}'
 'y_type_inputparser', '{"mwdata":["x"],"mwsize":[1,1],"mwtype":"inputParser"}'
};
for k = 1:size(large, 1)
    [key, js] = large{k, :};
    if any(strcmp(key, {'y_char_3d', 'y_sparse_complex'}))
        chk(key, @() decq(js));  % JGraph holds neither (ADR 0270): one line, a divergence
    else
        chk(key, @() dec(js));
    end
end

% --- encoderequest and decoderesponse ---------------------------------------------------------------
chk('r_req_default', @() mps.json.encoderequest({[1 2 3], 'abc'}));
chk('r_req_nargout', @() mps.json.encoderequest({[1 2 3], 'abc'}, 'Nargout', 2));
chk('r_req_nargout0', @() mps.json.encoderequest({1}, 'Nargout', 0));
chk('r_req_nargout_frac', @() mps.json.encoderequest({1}, 'Nargout', 2.5));
chk('r_req_nargout_vec', @() mps.json.encoderequest({1}, 'Nargout', [1 2]));
chk('r_req_nargout_neg', @() mps.json.encoderequest({1}, 'Nargout', -1));
chk('r_req_nargout_text', @() mps.json.encoderequest({1}, 'Nargout', '2'));
chk('r_req_nargout_int', @() mps.json.encoderequest({1}, 'Nargout', int8(3)));
chk('r_req_nargout_logical', @() mps.json.encoderequest({1}, 'Nargout', true));
chk('r_req_nargout_empty', @() mps.json.encoderequest({1}, 'Nargout', []));
chk('r_req_large', @() mps.json.encoderequest({magic(2)}, 'Format', 'large', 'OutputFormat', 'large', 'OutputNanInfType', 'object'));
chk('r_req_nan_obj', @() mps.json.encoderequest({NaN}, 'NanInfType', 'object'));
chk('r_req_empty', @() mps.json.encoderequest({}));
chk('r_req_not_cell', @() mps.json.encoderequest([1 2]));
chk('r_req_struct', @() mps.json.encoderequest(struct('a', 1)));
chk('r_req_none', @() mps.json.encoderequest());
chk('r_req_cell_col', @() mps.json.encoderequest({1; 2}));
chk('r_req_cell_2x2', @() mps.json.encoderequest({1 2; 3 4}));
chk('r_req_nested', @() mps.json.encoderequest({{1, 2}}));
chk('r_req_pretty', @() lines(mps.json.encoderequest({[1 2], struct('a', 1)}, 'PrettyPrint', true)));
chk('r_req_pretty_large', @() lines(mps.json.encoderequest({[1 2]}, 'PrettyPrint', true, 'Format', 'large', 'Nargout', 3)));
chk('r_req_bad_option', @() mps.json.encoderequest({1}, 'Mode', 'large'));
chk('r_req_bad_format', @() mps.json.encoderequest({1}, 'OutputFormat', 'medium'));
chk('r_req_prefix', @() mps.json.encoderequest({1}, 'Output', 'large'));
chk('r_req_prefix_ambiguous', @() mps.json.encoderequest({1}, 'OutputN', 'object'));
chk('r_req_unsupported', @() mps.json.encoderequest({@sin}));
chk('r_req_class', @() class(mps.json.encoderequest({1})));
chk('r_res_lhs', @() desc(mps.json.decoderesponse('{"lhs":[[[1,2,3,4]],"abc"]}')));
chk('r_res_lhs_large', @() desc(mps.json.decoderesponse('{"lhs":[{"mwdata":[1,2],"mwsize":[1,2],"mwtype":"double"}]}')));
chk('r_res_lhs_empty', @() desc(mps.json.decoderesponse('{"lhs":[]}')));
chk('r_res_lhs_scalar', @() desc(mps.json.decoderesponse('{"lhs":5}')));
chk('r_res_lhs_obj', @() desc(mps.json.decoderesponse('{"lhs":{"a":1}}')));
chk('r_res_lhs_struct_items', @() desc(mps.json.decoderesponse('{"lhs":[{"a":1},{"b":[1,2]}]}')));
chk('r_res_lhs_nan', @() desc(mps.json.decoderesponse('{"lhs":[{"mwdata":"NaN"}]}')));
chk('r_res_lhs_extra', @() desc(mps.json.decoderesponse('{"lhs":[1],"x":2}')));
chk('r_res_lhs_bad_item', @() desc(mps.json.decoderesponse('{"lhs":[[1,"a"]]}')));
chk('r_res_error', @() desc(mps.json.decoderesponse('{"error":{"type":"matlaberror","id":"a:b","message":"boom","stack":[{"file":"f.m","name":"f","line":3}]}}')));
chk('r_res_error_stack', @() desc(mps.json.decoderesponse('{"error":{"type":"matlaberror","id":"a:b","message":"boom","stack":[{"file":"f.m","name":"f","line":3}]}}').stack));
chk('r_res_error_nostack', @() desc(mps.json.decoderesponse('{"error":{"type":"matlaberror","id":"a:b","message":"boom","stack":[]}}')));
chk('r_res_error_min', @() desc(mps.json.decoderesponse('{"error":{"message":"boom"}}')));
chk('r_res_error_text', @() desc(mps.json.decoderesponse('{"error":"boom"}')));
chk('r_res_error_both', @() desc(mps.json.decoderesponse('{"lhs":[1],"error":{"message":"boom"}}')));
chk('r_res_neither', @() mps.json.decoderesponse('{"x":1}'));
chk('r_res_array', @() mps.json.decoderesponse('[1]'));
chk('r_res_number', @() mps.json.decoderesponse('5'));
chk('r_res_bad_json', @() mps.json.decoderesponse('{"lhs":'));
chk('r_res_empty', @() mps.json.decoderesponse(''));
chk('r_res_not_text', @() mps.json.decoderesponse(5));
chk('r_res_cell', @() mps.json.decoderesponse({'{"lhs":[1]}'}));
chk('r_res_string', @() desc(mps.json.decoderesponse("{""lhs"":[1]}")));
chk('r_res_none', @() mps.json.decoderesponse());
chk('r_res_two', @() mps.json.decoderesponse('{"lhs":[1]}', 1));

% --- helpers --------------------------------------------------------------------------------------
function s = ascii(s)
% Text as it is when it is ASCII, else its code points.
if any(s > 127)
    s = mat2str(double(s));
end
end

function s = lines(s)
% Pretty-printed text as one line: each line's indent counted, the lines joined with ' / '.
parts = splitlines(s);
for k = 1:numel(parts)
    n = numel(parts{k}) - numel(strtrim(parts{k}));
    parts{k} = sprintf('%d>%s', n, strtrim(parts{k}));
end
s = strjoin(parts, ' / ');
end

function s = encq(v, opts)
% The encoding, or its refusal, on one line.
try
    s = ascii(mps.json.encode(v, opts{:}));
catch e
    s = [e.identifier ' : ' e.message];
end
end

function s = round2(v)
% What a value comes back as through both notations: class and size, and whether it is the same.
try
    a = mps.json.decode(mps.json.encode(v));
    b = mps.json.decode(mps.json.encode(v, 'Format', 'large'));
    s = sprintf('%s %s eq=%d | %s %s eq=%d', class(a), mat2str(size(a)), isequaln(a, v), class(b), mat2str(size(b)), isequaln(b, v));
catch e
    s = [e.identifier ' : ' e.message];
end
end

function v = complexInt16()
% int16(3 + 4i) is a complex integer, which JGraph has not (ADR 0266): its three rows diverge.
try
    v = int16(3 + 4i);
catch e
    v = e.identifier;
end
end

function v = complexSparse()
% sparse([1+2i 0; 0 3]) is a complex sparse array, which JGraph has not (ADR 0266): its three rows diverge.
try
    v = sparse([1+2i 0; 0 3]);
catch e
    v = e.identifier;
end
end

function v = made(f)
% The value f makes, or the refusal itself as an MException, which the encoder refuses too: JGraph has no
% inputParser (its rows diverge, each printing a refusal).
try
    v = f();
catch e
    v = MException(e.identifier, '%s', e.message);
end
end

function chk(name, fn)
% u2_chk, with the rule div=ADR0270 on the lines ADR 0270 records as divergences: a complex integer, a complex
% sparse array, a complex array that is empty or has zero imaginary parts, a char array of three dimensions
% and a logical sparse array, which JGraph holds otherwise (ADR 0266); int64 and uint64 past 2^53 and the
% intmin/intmax of int64 and uint64, doubles in JGraph (ADR 0174, open item 199); a string in a struct field,
% char in JGraph (open item 201); inputParser, which JGraph has not; and nargout of mps.json.decode, which
% R2025b refuses as a built-in method's.
diverging = {'e_c_empty_small', 'e_c_empty_large', 'e_c_int16_small', 'e_c_int16_large', 'e_c_int16_round', ...
    'e_c_zero_imag_small', 'e_c_zero_imag_large', 'e_ch_2x2x2_round', 'e_f_int64_big_small', 'e_f_int64_big_large', ...
    'e_f_uint64_max_small', 'e_f_uint64_max_large', 'e_f_uint64_max_round', 'e_sa_string_small', 'e_sa_string_large', ...
    'e_sp_complex_small', 'e_sp_complex_large', 'e_sp_complex_round', 'e_sp_logical_small', 'e_sp_logical_large', ...
    'e_sp_logical_round', 'e_u_inputparser_small', 'e_u_inputparser_large', 'e_u_inputparser_round', ...
    'i_08', 'i_09', 'i_10', 'i_11', 'o_decode_nargout', 'y_char_3d', 'y_complex_empty', 'y_int64_big', ...
    'y_sparse_complex', 'y_sparse_logical', 'y_uint64_max', 'y_uint64_over'};
if ~any(strcmp(name, diverging))
    u2_chk(name, fn);
    return;
end
try
    v = fn();
    fprintf('CHK|%s|%s|div=ADR0270\n', name, u2_text(v));
catch err
    fprintf('CHK|%s|%s|div=ADR0270\n', name, err.identifier);
    fprintf('CHK|%s_msg|%s|div=ADR0270\n', name, u2_text(err.message));
end
end

function s = dec(js)
% A decoded value: its class, size, realness, and its large-notation text, which shows every element.
d = mps.json.decode(js);
s = desc(d);
end

function s = decq(js)
% dec, with a refusal as the value's line rather than an error.
try
    s = dec(js);
catch e
    s = ['ERR ' e.identifier];
end
end

function s = nargoutq(name)
% nargout of a name as text, or the refusal's identifier, on one line.
try
    s = num2str(nargout(name));
catch e
    s = ['ERR ' e.identifier];
end
end

function s = desc(d)
s = sprintf('%s %s', class(d), mat2str(size(d)));
if isnumeric(d)
    s = sprintf('%s real=%d sparse=%d', s, isreal(d), issparse(d));
end
try
    s = [s ' ' ascii(mps.json.encode(d, 'Format', 'large'))];
catch
    if isstruct(d) && isscalar(d)
        f = fieldnames(d);
        s = [s ' fields=' strjoin(f', ',')];
        for k = 1:numel(f)
            try
                s = [s ' ' f{k} '=' ascii(mps.json.encode(d.(f{k}), 'Format', 'large'))]; %#ok<AGROW>
            catch
                s = [s ' ' f{k} '=<' class(d.(f{k})) '>']; %#ok<AGROW>
            end
        end
    else
        s = [s ' <nonencodable>'];
    end
end
end

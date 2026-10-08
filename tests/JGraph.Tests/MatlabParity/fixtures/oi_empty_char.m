% Open item 31 (ADR 0219): the 1-by-0 char is not ''. Nothing picked from a char row keeps the row's
% orientation, char of an empty code array keeps its shape, sprintf always answers a row, and a bracket
% of empties answers the empty char's shape; isequal, strcmp and switch tell 1-by-0 from 0-by-0.
% Probe probe_b9 (open-items scratch).

x = 'abc';
e10 = blanks(0);
u9b_chk('blanks_zero', @() e10);
u9b_chk('index_range', @() x(1:0));
u9b_chk('index_empty', @() x([]));
u9b_chk('index_zeros_1x0', @() x(zeros(1, 0)));
u9b_chk('index_zeros_0x1', @() x(zeros(0, 1)));
u9b_chk('index_mask', @() x(x == 'z'));
u9b_chk('index_logical_false', @() x(false(1, 3)));
u9b_chk('col_index_range', @() x(:, 1:0));
u9b_chk('two_d_range', @() x(1, 2:1));
u9b_chk('end_range', @() x(end+1:end));
u9b_chk('char_zeros_1x0', @() char(zeros(1, 0)));
u9b_chk('char_zeros_0x0', @() char([]));
u9b_chk('char_zeros_0x3', @() size(char(zeros(0, 3))));
u9b_chk('repmat_1x0', @() repmat('a', 1, 0));
u9b_chk('repmat_0x1', @() size(repmat('a', 0, 1)));
u9b_chk('repmat_str_1x0', @() repmat('ab', 1, 0));
u9b_chk('sprintf_empty', @() sprintf(''));
u9b_chk('sprintf_s_empty', @() sprintf('%s', ''));
u9b_chk('sprintf_s_1x0', @() sprintf('%s', e10));
u9b_chk('num2str_empty', @() num2str([]));
u9b_chk('strcat_empty', @() strcat('', ''));
u9b_chk('strcat_1x0', @() strcat(e10, e10));
u9b_chk('upper_1x0', @() upper(e10));
u9b_chk('fliplr_1x0', @() fliplr(e10));
u9b_chk('deblank_spaces', @() deblank('   '));
u9b_chk('strtrim_spaces', @() strtrim('   '));
u9b_chk('strjoin_empty', @() strjoin({}, ','));
u9b_chk('regexprep_all', @() regexprep('a', 'a', ''));
u9b_chk('strrep_all', @() strrep('aa', 'a', ''));
u9b_chk('erase_all', @() erase('ab', {'a', 'b'}));
u9b_chk('cat_1x0_quote', @() [e10; '']);
u9b_chk('cat_quote_1x0', @() [''; e10]);
u9b_chk('hcat_1x0_1x0', @() [e10, e10]);
u9b_chk('vcat_1x0_1x0', @() size([e10; e10]));
u9b_chk('hcat_1x0_quote', @() [e10, '']);
u9b_chk('hcat_quote_1x0', @() ['', e10]);
u9b_chk('hcat_ab_1x0', @() ['ab', e10]);
u9b_chk('grow_from_1x0', @() [x(1:0), 'a', 'b']);
u9b_chk('mat2str_1x0', @() mat2str(e10));
u9b_chk('string_1x0', @() string(e10));
u9b_chk('char_string_empty', @() char(""));
u9b_chk('char_cell_empty', @() char({''}));
u9b_chk('cellstr_1x0', @() cellstr(e10));
u9b_chk('isequal_1x0_quote', @() isequal(e10, ''));
u9b_chk('isequal_quote_1x0', @() isequal('', e10));
u9b_chk('isequal_1x0_1x0', @() isequal(e10, x(1:0)));
u9b_chk('isequal_quote_empty', @() isequal('', []));
u9b_chk('strcmp_1x0_quote', @() strcmp(e10, ''));
u9b_chk('strcmp_1x0_range', @() strcmp(x(1:0), e10));
u9b_chk('isempty_1x0', @() isempty(e10));
u9b_chk('length_1x0', @() length(e10));
u9b_chk('ismember_1x0', @() ismember(e10, {''}));
u9b_chk('switch_1x0', @() switch_of(e10));
u9b_chk('switch_quote', @() switch_of(''));
u9b_chk('double_1x0', @() double(e10));
u9b_chk('struct_field_1x0', @() getfield(struct('a', e10), 'a'));
u9b_chk('strtok_empty', @() strtok(''));

function r = switch_of(v)
switch v
    case ''
        r = 'matched quote';
    otherwise
        r = 'no match';
end
end

% record: -noFigureWindows
% U3 of the app-building plan (ADR 0200): Extent, textwrap and listfonts. What a font measures
% belongs to the machine and to each engine's own text, so Extent is pinned by its rules — whole
% points, which lines count, what no text measures, how it reads in other units — and never by a
% width. textwrap's wrap to a count of characters is pinned whole.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
styles = {'pushbutton', 'togglebutton', 'radiobutton', 'checkbox', 'edit', 'text', 'slider', 'frame', 'listbox', 'popupmenu'};
whole = @(e) all(abs(e(3:4) * 0.75 - round(e(3:4) * 0.75)) < 1e-9) && all(e(1:2) == 0);

% --- no text, and whole points ------------------------------------------------------------------
for s = 1:numel(styles)
    c = uicontrol(f, 'Style', styles{s});
    fprintf('CHK|empty_%s|%s|exact\n', styles{s}, u2_text(c.Extent));
    c.String = 'Hello World'; one = c.Extent;
    c.String = {'Hello'; 'Hello World'; 'x'}; three = c.Extent;
    c.String = 'Hello'; first = c.Extent;
    fprintf('CHK|whole_points_%s|%d %d|exact\n', styles{s}, whole(one), whole(three));
    % A text control measures every line; the rest measure the first.
    fprintf('CHK|lines_counted_%s|%d %d|exact\n', styles{s}, isequal(three, first), three(4) > one(4) && three(3) == one(3));
    c.String = {'Hello'; 'Hello World'; 'x'}; c.Max = 3; many = c.Extent;
    fprintf('CHK|lines_counted_multiple_%s|%d|exact\n', styles{s}, many(4) > one(4) && many(3) == one(3));
    delete(c);
end

% --- what counts as a line ----------------------------------------------------------------------
t = uicontrol(f, 'Style', 'text');
t.String = ' '; space = t.Extent;
t.String = {''}; blank = t.Extent;
t.String = {}; none = t.Extent;
t.String = {'', ''}; two = t.Extent;
t.String = ['  '; '  ']; pad = t.Extent;
fprintf('CHK|blank_line_is_a_space|%d|exact\n', isequal(blank, space));
fprintf('CHK|no_lines|%s|exact\n', u2_text(none));
fprintf('CHK|two_blank_lines|%d %d|exact\n', two(3) == space(3), two(4) > space(4));
fprintf('CHK|padded_matrix|%d|exact\n', pad(4) == two(4) && pad(3) >= two(3));
t.String = 'a'; a1 = t.Extent; t.String = 'aa'; a2 = t.Extent; t.String = repmat('a', 1, 40); a40 = t.Extent;
fprintf('CHK|wider_with_more|%d %d|exact\n', a2(3) > a1(3), a40(3) > 10 * a1(3));
fprintf('CHK|one_line_height|%d|exact\n', a1(4) == a40(4));
t.FontSize = 20; big = t.Extent;
fprintf('CHK|bigger_font|%d %d|exact\n', big(3) > a40(3), big(4) > a40(4));
t.FontSize = 8;
t.String = 'a|b|c'; bars = t.Extent; t.String = 'a'; t.Style = 'popupmenu'; t.String = 'a|b|c'; item = t.Extent;
fprintf('CHK|a_list_measures_its_first_item|%d %d|exact\n', isequal(item, a1), bars(3) > a1(3));
delete(t);

% --- in other units -----------------------------------------------------------------------------
c = uicontrol(f, 'Style', 'text', 'String', 'Hello World');
px = c.Extent;
c.Units = 'points'; fprintf('CHK|extent_points|%d|exact\n', max(abs(c.Extent(3:4) - px(3:4) * 0.75)) < 1e-9);
c.Units = 'characters'; fprintf('CHK|extent_characters|%d|exact\n', max(abs(c.Extent(3:4) - px(3:4) ./ [5.6 15])) < 1e-9);
c.Units = 'normalized'; fprintf('CHK|extent_normalized|%d|exact\n', max(abs(c.Extent(3:4) - px(3:4) ./ [560 420])) < 1e-9);
c.Units = 'inches'; fprintf('CHK|extent_inches|%d|exact\n', max(abs(c.Extent(3:4) - px(3:4) / 96)) < 1e-9);
c.Units = 'centimeters'; fprintf('CHK|extent_centimeters|%d|exact\n', max(abs(c.Extent(3:4) - px(3:4) / 96 * 2.54)) < 1e-9);
fprintf('CHK|extent_origin|%s|exact\n', u2_text(c.Extent(1:2)));
delete(c);
p = uipanel(f, 'Units', 'pixels', 'Position', [10 10 200 100]);
c = uicontrol(p, 'Style', 'text', 'String', 'Hello World', 'Units', 'normalized');
fprintf('CHK|extent_normalized_in_panel|%d|exact\n', max(abs(c.Extent(3:4) - px(3:4) ./ [198 98])) < 1e-9);
delete(p);

% --- textwrap to a count of characters ----------------------------------------------------------
long = 'The quick brown fox jumps over the lazy dog and keeps on running far away';
cases = {
    {{long}, 20}
    {{long}, 10}
    {{'abcdefghijklmnopqrstuvwxyz'}, 5}
    {{'a', 'b'}, 20}
    {{''}, 20}
    {{}, 20}
    {"a b c d e f g h", 3}
    {{'ab abcdefghij'}, 5}
    {{'a  b   c'}, 4}
    {{'  lead'}, 4}
    {{'trail  '}, 10}
    {{'abcd efghi jk'}, 4}
    {{'abcde fghij'}, 5}
    {{'abcde fghij'}, 6}
    {{'one two'}, 100}
    {{'one two'}, 1}
    {{'one two'}, 0}
    {{'x'}, 3.7}
    {{sprintf('a\tb c')}, 3}
    {{'a', '', 'b'}, 5}
    {{'  '}, 5}
    {{'aaa bbb ccc'; 'ddd eee'}, 7}
    {['abc def'; 'ghi jkl'], 3}
    {'abc def', 3}
    {{'abc def'}, [3 4]}
    {{'abc def'}, 'x'}
    {{5}, 3}
    {{'abc', "def ghi"}, 3}
    };
for k = 1:numel(cases)
    u2_chk(sprintf('wrap_columns_%d', k), @() textwrap(cases{k}{1}, cases{k}{2}));
end
u2_chk('wrap_two_outputs', @() u3_second(@() textwrap({'abc def'}, 3)));

% --- textwrap to a control ----------------------------------------------------------------------
c = uicontrol(f, 'Style', 'text', 'Position', [20 20 100 60]);
u2_chk('wrap_one_argument', @() textwrap(c));
u2_chk('wrap_no_arguments', @() textwrap());
u2_chk('wrap_number', @() textwrap(5.5, {long}));
u2_chk('wrap_figure', @() textwrap(f, {long}));
u2_chk('wrap_char', @() textwrap(c, long));
u2_chk('wrap_char_matrix', @() textwrap(c, ['abc'; 'def']));
u2_chk('wrap_columns_zero', @() textwrap(c, {'one two'}, 0));
u2_chk('wrap_three_outputs', @() u3_third(c));
u2_chk('wrap_empty_paragraph', @() textwrap(c, {''}));
u2_chk('wrap_no_paragraphs', @() textwrap(c, {}));
u2_chk('wrap_spaces_kept', @() textwrap(c, {'a   b', '  c'}));
u2_chk('wrap_string_array', @() textwrap(c, ["one two", "three"]));
u2_chk('wrap_with_columns', @() textwrap(c, {long}, 15));
[lines, pos] = textwrap(c, {long, 'second para'});
fprintf('CHK|wrap_words_kept|%d|exact\n', isequal(strjoin(lines', ' '), [long ' second para']));
fprintf('CHK|wrap_more_than_one_line|%d|exact\n', numel(lines) > 2);
fits = true; t = uicontrol(f, 'Style', 'text', 'Visible', 'off');
for k = 1:numel(lines)
    t.String = lines{k}; e = t.Extent; fits = fits && e(3) <= 100;
end
fprintf('CHK|wrap_lines_fit|%d|exact\n', fits);
t.String = lines; e = t.Extent;
fprintf('CHK|wrap_position_is_the_extent|%d %d|exact\n', isequal(pos(1:2), [20 20]), max(abs(pos(3:4) - e(3:4))) < 1e-9);
fprintf('CHK|wrap_leaves_string|%s|exact\n', u2_text(c.String));
[~, pos] = textwrap(c, {});
fprintf('CHK|wrap_position_of_nothing|%s|exact\n', u2_text(pos));
[lines, pos] = textwrap(c, {'Supercalifragilisticexpialidocious and more'});
fprintf('CHK|wrap_long_word_whole|%s %d|exact\n', lines{1}, pos(3) > 100);
c.Position = [20 20 0 60];
u2_chk('wrap_zero_width', @() textwrap(c, {'The quick brown fox'}));
c.Position = [20 20 100 60]; c.Units = 'normalized';
[lines, pos] = textwrap(c, {long}); c.Units = 'pixels'; t.String = lines; e = t.Extent;
fprintf('CHK|wrap_position_in_units|%d|exact\n', max(abs(pos(3:4) - e(3:4) ./ [560 420])) < 1e-9);
x = uicontrol(f); delete(x);
u2_chk('wrap_deleted', @() textwrap(x, {'a b'}));
delete(c); delete(t);

% --- listfonts ----------------------------------------------------------------------------------
L = listfonts;
[~, order] = sort(lower(L));
fprintf('CHK|listfonts_shape|%s %d %d|exact\n', class(L), size(L, 2), numel(L) > 10);
fprintf('CHK|listfonts_sorted|%d|exact\n', isequal(order(:)', 1:numel(L)) || issorted(lower(L)));
fprintf('CHK|listfonts_has|%d %d %d|exact\n', any(strcmp(L, 'Arial')), any(strcmp(L, 'Courier New')), numel(unique(L)) == numel(L));
c = uicontrol(f, 'FontName', 'ZZNotAFont');
L2 = listfonts(c);
fprintf('CHK|listfonts_of_a_control|%d %d|exact\n', numel(L2) - numel(L), any(strcmp(L2, 'ZZNotAFont')));
c.FontName = 'Arial';
fprintf('CHK|listfonts_of_a_listed_font|%d|exact\n', numel(listfonts(c)) - numel(L));
u2_chk('listfonts_figure', @() numel(listfonts(f)) - numel(L));
u2_chk('listfonts_number', @() numel(listfonts(5.5)) - numel(L));
u2_chk('listfonts_text', @() numel(listfonts('a')) - numel(L));
u2_chk('listfonts_two', @() listfonts(1, 2));
delete(f);

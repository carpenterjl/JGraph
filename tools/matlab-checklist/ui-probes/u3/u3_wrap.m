function u3_wrap
% U3 probe: textwrap's column form in detail, its outputs, and the order of 'String' and 'Style'
% among a uicontrol's options. Headless: run-probe.ps1 -Name u3_wrap.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
cases = {
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
    {{' '}, 5}
    {{'aaa bbb ccc'; 'ddd eee'}, 7}
    {['abc def'; 'ghi jkl'], 3}
    {'abc def', 3}
    {{'abc def'}, [3 4]}
    {{'abc def'}, 'x'}
    {{5}, 3}
    {{'abc', "def ghi"}, 3}
    };
for k = 1:numel(cases)
    a = cases{k};
    tryp(sprintf('textwrap(%s,%s)', v2s(a{1}), v2s(a{2})), @() textwrap(a{1}, a{2}));
end
tryp('two outputs, no handle', @() two({'abc def'}, 3));
c = uicontrol(f, 'Style', 'text', 'Position', [20 20 100 60]);
tryp('handle, empty paragraph', @() tw(c, {''}));
tryp('handle, {}', @() tw(c, {}));
tryp('handle, spaces', @() tw(c, {'a   b', '  c'}));
tryp('handle, string array', @() tw(c, ["one two", "three"]));
tryp('handle, char matrix', @() tw(c, ['abc'; 'def']));
tryp('handle, cols 0', @() tw3(c, {'one two'}, 0));
tryp('handle, three outputs', @() three(c, {'one two'}));
tryp('handle, deleted', @() deleted(f));
c.Units = 'normalized';
tryp('handle normalized units', @() tw(c, {'The quick brown fox jumps over the lazy dog'}));
c.Units = 'pixels'; c.Position = [20 20 0 60];
tryp('handle zero width', @() tw(c, {'The quick brown fox'}));
delete(c);

%% String before Style
a = uicontrol(f, 'String', 'x|y|z', 'Style', 'popupmenu'); fprintf('String then Style popupmenu: %s\n', v2s(a.String));
b = uicontrol(f, 'Style', 'popupmenu', 'String', 'x|y|z'); fprintf('Style then String: %s\n', v2s(b.String));
b.Style = 'text'; fprintf('  then Style text: %s\n', v2s(b.String));
d = uicontrol(f, 'Style', 'text', 'String', 'x|y|z'); d.Style = 'listbox'; fprintf('text then listbox: %s\n', v2s(d.String));
e = uicontrol(f, 'Style', 'listbox', 'String', {'a|b', 'c'}); fprintf('cell with a bar: %s\n', v2s(e.String));
g = uicontrol(f, 'Style', 'listbox', 'String', "p|q"); fprintf('string scalar with a bar: %s\n', v2s(g.String));
h = uicontrol(f, 'Style', 'listbox', 'String', 'ab|c|'); fprintf('ragged bars: %s\n', v2s(h.String));
h.String = '|'; fprintf('one bar: %s\n', v2s(h.String));
h.String = 'a||b'; fprintf('two bars: %s\n', v2s(h.String));
h.String = sprintf('a|b\nc'); fprintf('bar and newline: %s\n', v2s(h.String));
h.Style = 'edit'; h.String = sprintf('a\n\nb'); fprintf('edit, blank line: %s\n', v2s(h.String));
h.String = sprintf('a\r\nb'); fprintf('edit, CRLF: %s size=%s\n', v2s(h.String), mat2str(size(h.String)));
h.String = sprintf('ab\n'); fprintf('edit, trailing newline: %s size=%s\n', v2s(h.String), mat2str(size(h.String)));
h.String = {sprintf('a\nb'), 'c'}; fprintf('cell holding a newline: %s\n', v2s(h.String));

%% Extent of blank text
t = uicontrol(f, 'Style', 'text', 'Units', 'points');
for s = {{''}, {'', ''}, {'', 'a'}, ' ', {}, ['  '; '  ']}
    t.String = s{1}; e = t.Extent; fprintf('Extent of %s: %s\n', v2s(s{1}), mat2str(e));
end
delete(f);
end

function s = two(text, cols)
[a, b] = textwrap(text, cols);
s = sprintf('%s | %s', v2s(a), v2s(b));
end

function s = three(c, text)
[a, b, d] = textwrap(c, text); %#ok<ASGLU>
s = 'ran';
end

function s = tw(c, text)
[out, pos] = textwrap(c, text);
s = sprintf('%s pos=%s', v2s(out), mat2str(pos, 8));
end

function s = tw3(c, text, cols)
[out, pos] = textwrap(c, text, cols);
s = sprintf('%s pos=%s', v2s(out), mat2str(pos, 8));
end

function s = deleted(f)
x = uicontrol(f); delete(x);
s = v2s(textwrap(x, {'a b'}));
end

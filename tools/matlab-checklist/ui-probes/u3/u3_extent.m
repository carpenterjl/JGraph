function u3_extent
% U3 probe: Extent of every uicontrol style for a grid of strings and fonts, textwrap and
% listfonts. Headless: run-probe.ps1 -Name u3_extent.
styles = {'pushbutton', 'togglebutton', 'radiobutton', 'checkbox', 'edit', 'text', 'slider', 'frame', 'listbox', 'popupmenu'};
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
strings = {'', 'a', 'W', 'abc', 'Hello World', 'iiii', 'MMMM', ' ', 'a b', {'a'; 'bb'}, ['ab'; 'cd'], {'one', 'two', 'three'}, sprintf('x\ny'), 'a|b|c'};

%% every style, default font
for s = 1:numel(styles)
    c = uicontrol(f, 'Style', styles{s});
    for k = 1:numel(strings)
        c.String = strings{k};
        fprintf('%s %s -> %s\n', styles{s}, v2s(strings{k}), mat2str(c.Extent, 8));
    end
    drawnow;
    fprintf('%s after drawnow -> %s\n', styles{s}, mat2str(c.Extent, 8));
    delete(c);
end

%% per character, text style, default font
c = uicontrol(f, 'Style', 'text');
chars = ['a':'z' 'A':'Z' '0':'9' ' .,;:!?-_()[]'];
w = zeros(1, numel(chars));
for k = 1:numel(chars)
    c.String = chars(k); e = c.Extent; w(k) = e(3);
end
fprintf('char widths (text, MS Sans Serif 8pt):\n');
for k = 1:numel(chars), fprintf(' [%s]=%.6g', chars(k), w(k)); end
fprintf('\n');
c.String = 'a'; e1 = c.Extent; c.String = 'aa'; e2 = c.Extent; c.String = 'aaa'; e3 = c.Extent; c.String = repmat('a', 1, 10); e10 = c.Extent;
fprintf('a=%s aa=%s aaa=%s a10=%s\n', mat2str(e1, 8), mat2str(e2, 8), mat2str(e3, 8), mat2str(e10, 8));
for n = 1:6
    c.String = repmat({'a'}, n, 1); e = c.Extent; fprintf('lines %d -> %s\n', n, mat2str(e, 8));
end
delete(c);

%% fonts
fonts = {
    {'FontSize', 10}
    {'FontSize', 12}
    {'FontSize', 20}
    {'FontWeight', 'bold'}
    {'FontAngle', 'italic'}
    {'FontName', 'Arial'}
    {'FontName', 'Courier New', 'FontSize', 10}
    {'FontName', 'FixedWidth'}
    {'FontName', 'Helvetica'}
    {'FontName', 'NoSuchFontAtAll'}
    {'FontUnits', 'pixels', 'FontSize', 12}
    {'FontUnits', 'normalized', 'FontSize', 0.5}
    {'FontUnits', 'inches', 'FontSize', 0.2}
    };
for s = {'text', 'pushbutton', 'edit', 'checkbox', 'popupmenu', 'listbox'}
    for k = 1:numel(fonts)
        c = uicontrol(f, 'Style', s{1}, fonts{k}{:});
        for str = {'', 'a', 'Hello World', {'a'; 'bb'}}
            c.String = str{1};
            fprintf('%s %s %s -> %s\n', s{1}, v2s(fonts{k}), v2s(str{1}), mat2str(c.Extent, 8));
        end
        delete(c);
    end
end

%% Extent in other units, and in a panel
c = uicontrol(f, 'Style', 'text', 'String', 'Hello World');
for u = {'pixels', 'points', 'characters', 'normalized', 'inches', 'centimeters'}
    c.Units = u{1}; fprintf('Extent in %s: %s\n', u{1}, mat2str(c.Extent, 8));
end
delete(c);
p = uipanel(f, 'Units', 'pixels', 'Position', [10 10 200 100]);
c = uicontrol(p, 'Style', 'text', 'String', 'Hello World', 'Units', 'normalized');
fprintf('Extent normalized in a 200x100 panel: %s\n', mat2str(c.Extent, 8));
delete(p);
uf = uifigure('Visible', 'off');
c = uicontrol(uf, 'Style', 'text', 'String', 'Hello World');
fprintf('uicontrol in uifigure: FontName=%s FontSize=%g FontUnits=%s Extent=%s Position=%s\n', c.FontName, c.FontSize, c.FontUnits, mat2str(c.Extent, 8), mat2str(c.Position));
delete(uf);

%% textwrap
c = uicontrol(f, 'Style', 'text', 'Position', [20 20 100 60]);
long = 'The quick brown fox jumps over the lazy dog and keeps on running far away';
tryp('textwrap(c,{long})', @() tw(c, {long}));
tryp('textwrap(c,{long,''second para''})', @() tw(c, {long, 'second para'}));
tryp('textwrap(c,long)', @() tw(c, long));
tryp('textwrap({long},20)', @() v2s(textwrap({long}, 20)));
tryp('textwrap({long},10)', @() v2s(textwrap({long}, 10)));
tryp('textwrap({''abcdefghijklmnopqrstuvwxyz''},5)', @() v2s(textwrap({'abcdefghijklmnopqrstuvwxyz'}, 5)));
tryp('textwrap({''a'',''b''},20)', @() v2s(textwrap({'a', 'b'}, 20)));
tryp('textwrap({''''},20)', @() v2s(textwrap({''}, 20)));
tryp('textwrap({},20)', @() v2s(textwrap({}, 20)));
tryp('textwrap(c,{long},15)', @() tw3(c, {long}, 15));
tryp('textwrap(["a b c d e f g h"],3)', @() v2s(textwrap("a b c d e f g h", 3)));
tryp('textwrap(c)', @() textwrap(c));
tryp('textwrap(5,{long})', @() textwrap(5, {long}));
tryp('textwrap(f,{long})', @() textwrap(f, {long}));
tryp('textwrap()', @() textwrap());
tryp('nargout textwrap', @() nargout('textwrap'));
c.Position = [20 20 300 60];
tryp('wide: textwrap(c,{long})', @() tw(c, {long}));
c.Units = 'characters';
tryp('characters: textwrap(c,{long})', @() tw(c, {long}));
c.Units = 'pixels'; c.Position = [20 20 100 60]; c.Style = 'edit';
tryp('edit: textwrap(c,{long})', @() tw(c, {long}));
c.Style = 'pushbutton';
tryp('pushbutton: textwrap(c,{long})', @() tw(c, {long}));
c.Style = 'text'; c.FontSize = 12;
tryp('12pt: textwrap(c,{long})', @() tw(c, {long}));
tryp('one long word', @() tw(c, {'Supercalifragilisticexpialidocious and more'}));
tryp('String unchanged', @() c.String);
delete(c);

%% listfonts
L = listfonts;
fprintf('listfonts: class=%s size=%s issorted=%d hasArial=%d hasCourierNew=%d hasMSSansSerif=%d unique=%d\n', class(L), mat2str(size(L) > [1 1]), issorted(L), any(strcmp(L, 'Arial')), any(strcmp(L, 'Courier New')), any(strcmp(L, 'MS Sans Serif')), numel(unique(L)) == numel(L));
fprintf('listfonts first five: %s\n', strjoin(L(1:5)', ' / '));
fprintf('listfonts sort check (lower): %d ; (plain sort): %d\n', isequal(L, sortrows(L)), isequal(L, sort(L)));
[~, order] = sort(lower(L)); fprintf('sorted case-insensitively: %d\n', isequal(order(:)', 1:numel(L)));
c = uicontrol(f, 'FontName', 'ZZNotAFont');
L2 = listfonts(c);
fprintf('listfonts(h): n-n0=%d has ZZNotAFont=%d\n', numel(L2) - numel(L), any(strcmp(L2, 'ZZNotAFont')));
tryp('listfonts(f)', @() numel(listfonts(f)) - numel(L));
tryp('listfonts(5)', @() listfonts(5));
tryp('listfonts(''a'')', @() listfonts('a'));
tryp('nargout listfonts', @() nargout('listfonts'));
tryp('nargin listfonts', @() nargin('listfonts'));
delete(f);
end

function s = tw(c, text)
[out, pos] = textwrap(c, text);
s = sprintf('%s pos=%s', v2s(out), mat2str(pos, 8));
end

function s = tw3(c, text, cols)
[out, pos] = textwrap(c, text, cols);
s = sprintf('%s pos=%s', v2s(out), mat2str(pos, 8));
end

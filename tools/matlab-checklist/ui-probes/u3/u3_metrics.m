function u3_metrics
% U3 probe: what Extent measures - per-character advances from long repeats, line heights, across
% font names and sizes. Extent is read in points, where R2025b's numbers are whole.
% Headless: run-probe.ps1 -Name u3_metrics.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
c = uicontrol(f, 'Style', 'text', 'Units', 'points');
chars = char(32:126);

%% which names are fonts of their own: 'Hello World' at 8 points
names = {'MS Sans Serif', 'Arial', 'Helvetica', 'Tahoma', 'Segoe UI', 'Verdana', 'Times New Roman', 'Courier New', 'Courier', 'Consolas', ...
    'Calibri', 'Microsoft Sans Serif', 'MS Shell Dlg 2', 'SansSerif', 'Serif', 'Monospaced', 'Dialog', 'FixedWidth', 'NoSuchFontAtAll', 'Symbol', 'Georgia', 'Times', 'Comic Sans MS', 'Impact'};
for k = 1:numel(names)
    c.FontName = names{k}; c.FontSize = 8;
    c.String = 'Hello World'; e1 = c.Extent;
    c.String = repmat('a', 1, 100); e2 = c.Extent;
    c.String = repmat('W', 1, 100); e3 = c.Extent;
    c.String = repmat({'a'}, 10, 1); e4 = c.Extent;
    fprintf('name %-22s HelloWorld=%s a100=%g W100=%g lines10=%g\n', names{k}, mat2str(e1(3:4)), e2(3), e3(3), e4(4));
end

%% per-character advances
fonts = {
    {'FontName', 'MS Sans Serif', 'FontSize', 8}
    {'FontName', 'MS Sans Serif', 'FontSize', 16}
    {'FontName', 'MS Sans Serif', 'FontSize', 8, 'FontWeight', 'bold'}
    {'FontName', 'Arial', 'FontSize', 8}
    {'FontName', 'Courier New', 'FontSize', 10}
    {'FontName', 'Times New Roman', 'FontSize', 12}
    };
for k = 1:numel(fonts)
    set(c, 'FontWeight', 'normal', fonts{k}{:});
    fprintf('font %s\n', v2s(fonts{k}));
    for n = [1 2 100]
        w = zeros(1, numel(chars));
        for j = 1:numel(chars)
            c.String = repmat(chars(j), 1, n); e = c.Extent; w(j) = e(3);
        end
        fprintf(' x%d: %s\n', n, mat2str(w));
    end
end

%% sizes: a x100 and line counts
set(c, 'FontName', 'MS Sans Serif', 'FontWeight', 'normal');
for sz = [4 6 7 8 9 10 11 12 14 16 18 20 24 36 8.5 10.5]
    c.FontSize = sz;
    c.String = repmat('a', 1, 100); e = c.Extent; a100 = e(3);
    c.String = 'a'; e = c.Extent; a1 = e(3);
    h = zeros(1, 6);
    for n = [1 2 3 4 10 20]
        c.String = repmat({'a'}, n, 1); e = c.Extent; h(n == [1 2 3 4 10 20]) = e(4);
    end
    fprintf('MS Sans Serif %g pt: a1=%g a100=%g heights(1 2 3 4 10 20)=%s\n', sz, a1, a100, mat2str(h));
end
set(c, 'FontName', 'Arial');
for sz = [8 10 12 20]
    c.FontSize = sz;
    c.String = repmat('a', 1, 100); e = c.Extent; a100 = e(3);
    h = zeros(1, 6);
    for n = [1 2 3 4 10 20]
        c.String = repmat({'a'}, n, 1); e = c.Extent; h(n == [1 2 3 4 10 20]) = e(4);
    end
    fprintf('Arial %g pt: a100=%g heights=%s\n', sz, a100, mat2str(h));
end

%% a few mixed strings, MS Sans Serif 8
set(c, 'FontName', 'MS Sans Serif', 'FontSize', 8);
for s = {'The quick brown fox', 'The quick brown fox ', 'jumps over the lazy', 'AV', 'To', 'fi', '  ', 'a  b', sprintf('a\tb'), 'Wide WWW', '0123456789', 'ÄÖÜ', '€', char(960)}
    c.String = s{1}; e = c.Extent;
    fprintf('[%s] -> %s\n', s{1}, mat2str(e(3:4)));
end

%% the other styles add nothing? width of the same string per style, 3 lines
c.Units = 'points';
for s = {'pushbutton', 'togglebutton', 'radiobutton', 'checkbox', 'edit', 'text', 'slider', 'frame', 'listbox', 'popupmenu'}
    c.Style = s{1};
    c.String = 'Hello World'; e1 = c.Extent;
    c.String = {'Hello', 'Wide World', 'x'}; e2 = c.Extent;
    c.Max = 3; e3 = c.Extent; c.Max = 1;
    fprintf('style %s: one=%s three=%s three(Max 3)=%s\n', s{1}, mat2str(e1(3:4)), mat2str(e2(3:4)), mat2str(e3(3:4)));
end
delete(f);
end

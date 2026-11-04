% w14_ties - how R2025b's Comparison Tool places an insertion or a deletion that could sit in several places
% (stage W14 of the App Designer window plan), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w14_ties
% Small pairs where two matchings are equally long - a line duplicated beside itself, a block repeated, a swap, a
% rotation - varied by indentation and blank lines, each read from visdiff's report as w14_compare reads them. The
% rows feed CompareOracle.json's second part (CompareTiesOracle.json) and settle the tie rule TextDiff follows.
here = fileparts(mfilename('fullpath'));
work = fullfile(here, 'rt', 'ties');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
beep off;
glyphs = {'+', char(8722), char(8800)};
kinds = {'inserted', 'deleted', 'changed'};
nl = newline;

cases = struct('name', {}, 'left', {}, 'right', {});
function cases = add(cases, name, left, right)
    cases(end+1) = struct('name', name, 'left', {left}, 'right', {right});
end
% Duplicates beside themselves.
cases = add(cases, 'dup insert flat', {'a', 'X', 'b'}, {'a', 'X', 'X', 'b'});
cases = add(cases, 'dup delete flat', {'a', 'X', 'X', 'b'}, {'a', 'X', 'b'});
cases = add(cases, 'dup insert indented', {'a', '    X', 'b'}, {'a', '    X', '    X', 'b'});
cases = add(cases, 'dup insert two of three', {'a', 'X', 'X', 'b'}, {'a', 'X', 'X', 'X', 'b'});
cases = add(cases, 'dup delete two of three', {'a', 'X', 'X', 'X', 'b'}, {'a', 'X', 'X', 'b'});
cases = add(cases, 'dup insert at start', {'X', 'b'}, {'X', 'X', 'b'});
cases = add(cases, 'dup insert at end', {'a', 'X'}, {'a', 'X', 'X'});
cases = add(cases, 'dup delete at start', {'X', 'X', 'b'}, {'X', 'b'});
cases = add(cases, 'dup delete at end', {'a', 'X', 'X'}, {'a', 'X'});
cases = add(cases, 'dup insert blank before', {'a', '', 'X', 'b'}, {'a', '', 'X', 'X', 'b'});
cases = add(cases, 'dup insert blank after', {'a', 'X', '', 'b'}, {'a', 'X', 'X', '', 'b'});
cases = add(cases, 'blank dup insert', {'a', '', 'b'}, {'a', '', '', 'b'});
cases = add(cases, 'blank dup delete', {'a', '', '', 'b'}, {'a', '', 'b'});
% A two-line block repeated.
cases = add(cases, 'block dup insert flat', {'a', 'X', 'Y', 'b'}, {'a', 'X', 'Y', 'X', 'Y', 'b'});
cases = add(cases, 'block dup delete flat', {'a', 'X', 'Y', 'X', 'Y', 'b'}, {'a', 'X', 'Y', 'b'});
cases = add(cases, 'block dup insert Y indented', {'a', 'X', '    Y', 'b'}, {'a', 'X', '    Y', 'X', '    Y', 'b'});
cases = add(cases, 'block dup insert X indented', {'a', '    X', 'Y', 'b'}, {'a', '    X', 'Y', '    X', 'Y', 'b'});
cases = add(cases, 'loop dup flat', {'a', 'for k', 'body', 'end', 'b'}, {'a', 'for k', 'body', 'end', 'for k', 'body', 'end', 'b'});
cases = add(cases, 'loop dup indented body', {'a', 'for k', '    body', 'end', 'b'}, {'a', 'for k', '    body', 'end', 'for k', '    body', 'end', 'b'});
cases = add(cases, 'loop dup other name', {'a', 'for j', '    body', 'end', 'b'}, {'a', 'for j', '    body', 'end', 'for k', '    body', 'end', 'b'});
cases = add(cases, 'loop dup other name flat', {'a', 'for j', 'body', 'end', 'b'}, {'a', 'for j', 'body', 'end', 'for k', 'body', 'end', 'b'});
% The w14_compare shapes made small.
cases = add(cases, 'shape A', {'w', 'h', 'm', 'r'}, {'w', 'h', 't', 'm', 'h', 'm', 'r'});
cases = add(cases, 'shape A indented t', {'w', 'h', 'm', 'r'}, {'w', 'h', '    t', 'm', 'h', 'm', 'r'});
cases = add(cases, 'shape A real', {'while y > 0', 'hold on', 'x = mod(x, 7);', '    z = x * y;'}, ...
    {'while y > 0', 'hold on', 'title(''t'');', 'x = mod(x, 7);', 'hold on', 'x = mod(x, 7);', '    z = x * y;'});
cases = add(cases, 'shape E', {'p', 'h', 'h', 'f', 'q'}, {'p', 'h', 'h', 'h', 'q'});
cases = add(cases, 'shape E no deletion', {'p', 'h', 'h', 'q'}, {'p', 'h', 'h', 'h', 'q'});
cases = add(cases, 'shape C4', {'f', 'z', 'y'}, {'f', 'd', 'p', 'z', 'z', 'y'});
cases = add(cases, 'shape C4 no lead', {'f', 'z', 'y'}, {'f', 'z', 'z', 'y'});
cases = add(cases, 'shape C7', {'r', 'e', 'e', 'y'}, {'r', 'e', 'y'});
cases = add(cases, 'shape C26', {'k', 'z', 'w', 'h'}, {'k', 'z', 'w', 'i', 'z', 'w', 'h'});
cases = add(cases, 'shape C26 real', {'y = y + k;', '    z = x * y;', 'while y > 0', 'hold on'}, ...
    {'y = y + k;', '    z = x * y;', 'while y > 0', 'if x > y', '    z = x * y;', 'while y > 0', 'hold on'});
cases = add(cases, 'ends only', {'end', 'end', 'end'}, {'end', 'end'});
cases = add(cases, 'ends only reversed', {'end', 'end'}, {'end', 'end', 'end'});
% Swaps and rotations.
cases = add(cases, 'swap', {'p', 'A', 'B', 'q'}, {'p', 'B', 'A', 'q'});
cases = add(cases, 'swap at start', {'A', 'B', 'q'}, {'B', 'A', 'q'});
cases = add(cases, 'swap at end', {'p', 'A', 'B'}, {'p', 'B', 'A'});
cases = add(cases, 'rotate', {'A', 'B', 'C'}, {'C', 'A', 'B'});
cases = add(cases, 'rotate back', {'C', 'A', 'B'}, {'A', 'B', 'C'});
cases = add(cases, 'move one down', {'p', 'A', 'B', 'C', 'q'}, {'p', 'B', 'C', 'A', 'q'});
cases = add(cases, 'move one up', {'p', 'B', 'C', 'A', 'q'}, {'p', 'A', 'B', 'C', 'q'});
cases = add(cases, 'block moved', {'1', '2', 'if', 'r10', 'end', 'for', 'rrk', 'end', 'd'}, {'1', '2', 'for', 'rrk', 'end', 'if', 'r10', 'end', 'd'});
cases = add(cases, 'block moved flat ends', {'1', 'if', 'x', 'E', 'for', 'y', 'E', 'd'}, {'1', 'for', 'y', 'E', 'if', 'x', 'E', 'd'});
% Longer stretches with a repeated line far apart.
cases = add(cases, 'far dup insert', {'a', 'X', 'b', 'c', 'd', 'e'}, {'a', 'X', 'b', 'c', 'd', 'X', 'e'});
cases = add(cases, 'far dup delete', {'a', 'X', 'b', 'c', 'd', 'X', 'e'}, {'a', 'X', 'b', 'c', 'd', 'e'});
cases = add(cases, 'ambiguous middle', {'a', 'X', 'b', 'X', 'c'}, {'a', 'X', 'c'});
cases = add(cases, 'ambiguous middle insert', {'a', 'X', 'c'}, {'a', 'X', 'b', 'X', 'c'});
cases = add(cases, 'X then Xb', {'a', 'X', 'b'}, {'a', 'X', 'q', 'X', 'b'});
cases = add(cases, 'two groups', {'a', 'X', 'b', 'Y', 'c'}, {'a', 'X', 'X', 'b', 'Y', 'Y', 'c'});

function rows = parseReport(h, glyphs, kinds)
    rows = struct('kind', {}, 'left', {}, 'right', {});
    i = strfind(h, 'class="TextDiffTable"');
    if isempty(i), return; end
    h = h(i:end);
    trs = regexp(h, '<tr>(.*?)</tr>', 'tokens');
    carry = {struct('kind', '', 'left', 0), struct('kind', '', 'left', 0)};
    for t = 1:numel(trs)
        tds = regexp(trs{t}{1}, '<td[^>]*>.*?</td>', 'match');
        side = 0;
        row = struct('kind', 'same', 'left', 0, 'right', 0);
        sideKind = {'', ''};
        k = 1;
        while k <= numel(tds) && side < 2
            td = tds{k};
            if contains(td, 'valign="middle"')
                g = regexp(td, 'vertical-align:middle;">([^<]*)<', 'tokens', 'once');
                span = regexp(td, 'rowspan="(\d+)"', 'tokens', 'once');
                n = 1; if ~isempty(span), n = str2double(span{1}); end
                kindName = '';
                if ~isempty(g) && any(strcmp(glyphs, strtrim(g{1}))), kindName = kinds{strcmp(glyphs, strtrim(g{1}))}; end
                sideKind{side + 1} = kindName;
                carry{side + 1} = struct('kind', kindName, 'left', n - 1);
                k = k + 1;
                continue
            end
            num = regexp(td, 'vertical-align:top;">(\d*)<', 'tokens', 'once');
            if ~isempty(num)
                value = 0; if ~isempty(num{1}), value = str2double(num{1}); end
                if side == 0, row.left = value; else, row.right = value; end
                if isempty(sideKind{side + 1}) && carry{side + 1}.left > 0
                    sideKind{side + 1} = carry{side + 1}.kind;
                    carry{side + 1}.left = carry{side + 1}.left - 1;
                end
                k = k + 1;
                if k <= numel(tds) && contains(tds{k}, '<p'), k = k + 1; end
                side = side + 1;
                continue
            end
            k = k + 1;
        end
        if any(strcmp(sideKind, 'changed')), row.kind = 'changed';
        elseif any(strcmp(sideKind, 'inserted')), row.kind = 'inserted';
        elseif any(strcmp(sideKind, 'deleted')), row.kind = 'deleted';
        end
        rows(end+1) = row; %#ok<AGROW>
    end
end

oracle = struct('name', {}, 'left', {}, 'right', {}, 'rows', {}, 'class', {}, 'identical', {});
for k = 1:numel(cases)
    c = cases(k);
    folder = fullfile(work, sprintf('t%02d', k));
    mkdir(fullfile(folder, 'l')); mkdir(fullfile(folder, 'r'));
    a = fullfile(folder, 'l', 'prog.m'); b = fullfile(folder, 'r', 'prog.m');
    fa = fopen(a, 'w', 'n', 'UTF-8'); fprintf(fa, '%s', strjoin(c.left, nl)); fclose(fa);
    fb = fopen(b, 'w', 'n', 'UTF-8'); fprintf(fb, '%s', strjoin(c.right, nl)); fclose(fb);
    entry = struct('name', c.name, 'left', {c.left}, 'right', {c.right}, 'rows', [], 'class', '', 'identical', false);
    cmp = visdiff(a, b);
    entry.class = class(cmp);
    r = publish(cmp, 'Format', 'html', 'OutputFolder', folder, 'Name', 'report');
    h = fileread(r);
    entry.rows = parseReport(h, glyphs, kinds);
    words = {};
    for i = 1:numel(entry.rows)
        row = entry.rows(i);
        switch row.kind
            case 'same', words{end+1} = sprintf('%d=%d', row.left, row.right); %#ok<AGROW>
            case 'inserted', words{end+1} = sprintf('+R%d', row.right); %#ok<AGROW>
            case 'deleted', words{end+1} = sprintf('-L%d', row.left); %#ok<AGROW>
            case 'changed', words{end+1} = sprintf('L%d~R%d', row.left, row.right); %#ok<AGROW>
        end
    end
    fprintf('%-28s L[%s]  R[%s]\n%-28s %s\n', c.name, strjoin(strtrim(c.left), '|'), strjoin(strtrim(c.right), '|'), '', strjoin(words, ' '));
    oracle(end+1) = entry; %#ok<AGROW>
end
fid = fopen(fullfile(here, 'CompareTiesOracle.json'), 'w', 'n', 'UTF-8');
fprintf(fid, '%s', jsonencode(oracle, 'PrettyPrint', true));
fclose(fid);
fprintf('oracle: %d cases\ndone\n', numel(oracle));

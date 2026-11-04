% w14_compare - how R2025b's Comparison Tool pairs the lines of two texts (stage W14 of the App Designer
% window plan), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w14_compare
% visdiff with an output takes the no-window road and answers a comparison whose publish writes the
% side-by-side report; the report's rows are the oracle: each row's glyph (+ inserted, U+2212 deleted,
% U+2260 changed, none unchanged) with the left and right line numbers. Pairs: hand-made texts that tell
% diff algorithms apart (one edit, blocks, moves, repeats, white space, blank lines, the ends), a seeded
% corpus of edited programs, and the W0 app pairs. CompareOracle.json holds them for CompareOracleTests.
here = fileparts(mfilename('fullpath'));
ds8 = fullfile(fileparts(here), 'ds8', 'rt', 'accept');
work = fullfile(here, 'rt', 'compare');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
short = @(s) strrep(strrep(char(s), work, '<compare>'), ds8, '<ds8>');
beep off;

pairs = struct('name', {}, 'left', {}, 'right', {});
function pairs = add(pairs, name, left, right)
    pairs(end+1) = struct('name', name, 'left', {left}, 'right', {right});
end
nl = newline;
prog = {'function r = compute(a, b)', '    % Adds two numbers.', '    r = a + b;', '    if r > 10', '        r = 10;', '    end', ...
    '    for k = 1:3', '        r = r + k;', '    end', '    disp(r);', 'end'};
pairs = add(pairs, 'identical', prog, prog);
edited = prog; edited{3} = '    r = a + b + 0;';
pairs = add(pairs, 'one line changed', prog, edited);
pairs = add(pairs, 'one line inserted', prog, [prog(1:3), {'    r = r * 2;'}, prog(4:end)]);
pairs = add(pairs, 'one line deleted', prog, [prog(1:2), prog(4:end)]);
pairs = add(pairs, 'two lines become three', prog, [prog(1:3), {'    if r > 20', '        r = 20;', '        disp(''capped'');'}, prog(6:end)]);
pairs = add(pairs, 'three lines become two', [prog(1:3), {'    if r > 20', '        r = 20;', '        disp(''capped'');'}, prog(6:end)], prog);
pairs = add(pairs, 'block moved', prog, [prog(1:3), prog(7:9), prog(4:6), prog(10:end)]);
pairs = add(pairs, 'block appended', prog(1:end-1), [prog(1:end-1), {'    r = r - 1;', '    r = r / 2;', 'end'}]);
pairs = add(pairs, 'block prepended', prog, [{'% A header comment', '% on two lines', ''}, prog]);
pairs = add(pairs, 'first line changed', prog, [{'function r = compute(a, b, c)'}, prog(2:end)]);
pairs = add(pairs, 'last line changed', prog, [prog(1:end-1), {'end % of compute'}]);
pairs = add(pairs, 'white space only', prog, strrep(prog, 'r = a + b;', 'r = a+b;'));
pairs = add(pairs, 'indent only', prog, [prog(1:2), {'  r = a + b;'}, prog(4:end)]);
pairs = add(pairs, 'blank line inserted', prog, [prog(1:3), {''}, prog(4:end)]);
pairs = add(pairs, 'blank line deleted', [prog(1:3), {''}, prog(4:end)], prog);
pairs = add(pairs, 'empty left', {}, prog);
pairs = add(pairs, 'empty right', prog, {});
pairs = add(pairs, 'all different', prog, {'x = 1;', 'y = 2;', 'z = x + y;'});
% Repeated lines: three ends and two loops, with one loop body changed and one end removed.
rep = {'a = 1;', 'for i = 1:2', '    a = a + 1;', 'end', 'for j = 1:2', '    a = a + 1;', 'end', 'if a > 2', '    a = 0;', 'end', 'disp(a);'};
pairs = add(pairs, 'repeated lines, one body changed', rep, [rep(1:5), {'    a = a + 2;'}, rep(7:end)]);
pairs = add(pairs, 'repeated lines, a loop removed', rep, [rep(1:4), rep(8:end)]);
pairs = add(pairs, 'repeated lines, a loop added', rep, [rep(1:7), {'for k = 1:2', '    a = a + 1;', 'end'}, rep(8:end)]);
pairs = add(pairs, 'ends only', {'end', 'end', 'end'}, {'end', 'end'});
pairs = add(pairs, 'swap two lines', prog, [prog(1:2), prog(4), prog(3), prog(5:end)]);
pairs = add(pairs, 'change then insert', prog, [prog(1:2), {'    r = a - b;', '    r = abs(r);'}, prog(4:end)]);
pairs = add(pairs, 'delete then change', prog, [prog(1), {'    r = a - b;'}, prog(4:end)]);
pairs = add(pairs, 'change far apart', prog, [prog(1:2), {'    r = a - b;'}, prog(4:9), {'    disp(r * 2);'}, prog(11)]);
pairs = add(pairs, 'tab and spaces', {'x = 1;', sprintf('\ty = 2;'), 'z = 3;'}, {'x = 1;', '    y = 2;', 'z = 3;'});
pairs = add(pairs, 'case only', prog, strrep(prog, 'disp(r);', 'DISP(r);'));
% A seeded corpus: a 40-line program edited by 1 to 6 random operations.
rng(14, 'twister');
vocab = {'x = x + 1;', 'y = y - 1;', 'end', 'if x > y', 'for k = 1:n', 'while y > 0', '    z = x * y;', '    disp(z);', ...
    '% comment', '', 'z = sqrt(x);', 'x = mod(x, 7);', 'y = y + k;', 'plot(x, y);', 'hold on', 'title(''t'');', 'r = [x y z];'};
for c = 1:30
    n = 40;
    base = vocab(randi(numel(vocab), 1, n));
    right = base;
    ops = randi(6);
    for o = 1:ops
        kind = randi(4);
        at = randi(max(1, numel(right)));
        switch kind
            case 1 % change a line
                right{min(at, numel(right))} = vocab{randi(numel(vocab))};
            case 2 % insert one to three lines
                m = randi(3);
                right = [right(1:at-1), vocab(randi(numel(vocab), 1, m)), right(at:end)];
            case 3 % delete one to three lines
                m = min(randi(3), numel(right) - at + 1);
                right(at:at+m-1) = [];
            case 4 % move a block
                m = min(randi(4), numel(right) - at + 1);
                block = right(at:at+m-1);
                right(at:at+m-1) = [];
                to = randi(max(1, numel(right)));
                right = [right(1:to-1), block, right(to:end)];
        end
    end
    pairs = add(pairs, sprintf('corpus %d', c), base, right);
end

glyphs = {'+', char(8722), char(8800)};
kinds = {'inserted', 'deleted', 'changed'};
function rows = parseReport(h, glyphs, kinds)
    % Each <tr> of the TextDiffTable: a glyph cell (rowspan n, white glyph, vertical-align:middle) per changed side, a
    % number cell and a text cell per side present. A glyph cell spanning n rows is carried to the next n-1 rows.
    rows = struct('kind', {}, 'left', {}, 'right', {});
    i = strfind(h, 'class="TextDiffTable"');
    if isempty(i), return; end
    h = h(i:end);
    trs = regexp(h, '<tr>(.*?)</tr>', 'tokens');
    carry = {struct('kind', '', 'left', 0), struct('kind', '', 'left', 0)};
    for t = 1:numel(trs)
        % A side is [glyph cell (valign middle; blank for a side with no line, spanning the block's rows)]
        % number cell (vertical-align:top; empty for no line) [text cell (<p>; the filler spans the block's rows)].
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
for k = 1:numel(pairs)
    p = pairs(k);
    folder = fullfile(work, sprintf('p%02d', k));
    mkdir(fullfile(folder, 'l')); mkdir(fullfile(folder, 'r'));
    a = fullfile(folder, 'l', 'prog.m'); b = fullfile(folder, 'r', 'prog.m');
    fa = fopen(a, 'w', 'n', 'UTF-8'); fprintf(fa, '%s', strjoin(p.left, nl)); fclose(fa);
    fb = fopen(b, 'w', 'n', 'UTF-8'); fprintf(fb, '%s', strjoin(p.right, nl)); fclose(fb);
    entry = struct('name', p.name, 'left', {p.left}, 'right', {p.right}, 'rows', [], 'class', '', 'identical', false);
    try
        c = visdiff(a, b);
        entry.class = class(c);
        r = publish(c, 'Format', 'html', 'OutputFolder', folder, 'Name', 'report');
        h = fileread(r);
        entry.identical = contains(h, 'identical') || contains(h, 'No changes detected');
        entry.rows = parseReport(h, glyphs, kinds);
        counts = [sum(strcmp({entry.rows.kind}, 'inserted')), sum(strcmp({entry.rows.kind}, 'deleted')), sum(strcmp({entry.rows.kind}, 'changed'))];
        fprintf('== %-36s %3d rows: inserted %d, deleted %d, changed %d%s\n', p.name, numel(entry.rows), counts, ...
            repmat(' (identical)', 1, entry.identical));
        if k <= 28
            for i = 1:numel(entry.rows)
                if ~strcmp(entry.rows(i).kind, 'same')
                    fprintf('     %-8s %3d %3d\n', entry.rows(i).kind, entry.rows(i).left, entry.rows(i).right);
                end
            end
        end
    catch e
        fprintf('== %s -> %s: %s\n', p.name, e.identifier, short(e.message));
        entry.class = ['ERR ' e.identifier];
    end
    oracle(end+1) = entry; %#ok<AGROW>
end

% The W0 app pairs again, for the rows: the acceptance app against another app, and against a copy with one line edited.
left = fullfile(work, 'AcceptForm.mlapp');
copyfile(fullfile(ds8, 'AcceptForm.mlapp'), left);
mkdir(fullfile(work, 'code'));
[codeData, ~] = appdesigner.internal.comparison.getAppData(left);
text = appdesigner.internal.codegeneration.getAppFileCode(left);
cbs = codeData.Callbacks;
for k = 1:numel(cbs), cbs(k).Code = strrep(cbs(k).Code, 'r = a + b;', 'r = a + b + 0;'); end
appdesigner.internal.comparison.saveAppCode(left, fullfile(work, 'code', 'AcceptForm.mlapp'), strrep(text, 'r = a + b;', 'r = a + b + 0;'), struct('Callbacks', cbs), true);
copyfile(fullfile(ds8, 'AcceptGrid.mlapp'), fullfile(work, 'AcceptGrid.mlapp'));
appPairs = {{'app: one callback line', fullfile(work, 'code', 'AcceptForm.mlapp')}, {'app: another app', fullfile(work, 'AcceptGrid.mlapp')}};
for k = 1:numel(appPairs)
    right = appPairs{k}{2};
    entry = struct('name', appPairs{k}{1}, 'left', {splitlines(appdesigner.internal.codegeneration.getAppFileCode(left))'}, ...
        'right', {splitlines(appdesigner.internal.codegeneration.getAppFileCode(right))'}, 'rows', [], 'class', '', 'identical', false);
    c = visdiff(left, right);
    entry.class = class(c);
    r = publish(c, 'Format', 'html', 'OutputFolder', work, 'Name', sprintf('app_report_%d', k));
    h = fileread(r);
    entry.identical = contains(h, 'No changes detected');
    entry.rows = parseReport(h, glyphs, kinds);
    counts = [sum(strcmp({entry.rows.kind}, 'inserted')), sum(strcmp({entry.rows.kind}, 'deleted')), sum(strcmp({entry.rows.kind}, 'changed'))];
    fprintf('== %-36s %3d rows: inserted %d, deleted %d, changed %d\n', entry.name, numel(entry.rows), counts);
    oracle(end+1) = entry; %#ok<AGROW>
end

% The comparison object for two text files, and the refusals.
c = visdiff(fullfile(work, 'p02', 'l', 'prog.m'), fullfile(work, 'p02', 'r', 'prog.m'));
fprintf('== text comparison: class %s; properties %s; methods %s\n', class(c), strjoin(properties(c)', ', '), strjoin(methods(c)', ', '));
fprintf('   Left %s; Right %s\n', short(c.Left), short(c.Right));
disp(c);
try, filter(c, 'unfiltered'); catch e, fprintf('   filter -> %s: %s\n', e.identifier, e.message); end
try, visdiff(fullfile(work, 'nowhere.m'), fullfile(work, 'p02', 'r', 'prog.m')); catch e, fprintf('== missing left -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(fullfile(work, 'nowhere.m'), fullfile(work, 'p02', 'r', 'prog.m')); catch e, fprintf('== missing left, output -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(fullfile(work, 'p02', 'l', 'prog.m')); catch e, fprintf('== one argument -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(fullfile(work, 'p02', 'l', 'prog.m'), fullfile(work, 'p02', 'r', 'prog.m'), 'nosuchtype'); catch e, fprintf('== unknown type -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(fullfile(work, 'p02', 'l', 'prog.m'), fullfile(work, 'p02', 'r', 'prog.m'), 'text'); fprintf('== type text -> %s\n', class(c)); catch e, fprintf('== type text -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(fullfile(work, 'p02', 'l', 'prog.m'), fullfile(work, 'p02', 'r', 'prog.m'), 'binary'); fprintf('== type binary -> %s\n', class(c)); catch e, fprintf('== type binary -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(fullfile(work, 'p02', 'l'), fullfile(work, 'p02', 'r')); fprintf('== folders, output -> %s\n', class(c)); catch e, fprintf('== folders, output -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(left, fullfile(work, 'p02', 'r', 'prog.m')); fprintf('== mlapp vs m -> %s\n', class(c)); catch e, fprintf('== mlapp vs m -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(left, fullfile(work, 'AcceptGrid.mlapp'), 'text'); fprintf('== mlapp vs mlapp as text -> %s\n', class(c)); catch e, fprintf('== mlapp vs mlapp as text -> %s: %s\n', e.identifier, short(e.message)); end
try, c = visdiff(1, 2); catch e, fprintf('== numbers -> %s: %s\n', e.identifier, short(e.message)); end
try, r = publish(c, 'Format', 'nosuch'); catch e, fprintf('== publish bad format -> %s: %s\n', e.identifier, short(e.message)); end
try, r = publish(c, 'OutputFolder', fullfile(work, 'nowhere')); catch e, fprintf('== publish bad folder -> %s: %s\n', e.identifier, short(e.message)); end
c = visdiff(fullfile(work, 'p02', 'l', 'prog.m'), fullfile(work, 'p02', 'r', 'prog.m'));
r = publish(c);
fprintf('== publish defaults -> %s\n', short(r));
r = publish(c, 'Format', 'docx', 'OutputFolder', work, 'Name', 'word');
fprintf('== publish docx -> %s\n', short(r));
% The lines a text file was read as: does a trailing newline make an empty last line?
f = fopen(fullfile(work, 'trail_l.m'), 'w'); fprintf(f, 'a = 1;\nb = 2;\n'); fclose(f);
f = fopen(fullfile(work, 'trail_r.m'), 'w'); fprintf(f, 'a = 1;\nb = 2;'); fclose(f);
c = visdiff(fullfile(work, 'trail_l.m'), fullfile(work, 'trail_r.m'));
h = fileread(publish(c, 'Format', 'html', 'OutputFolder', work, 'Name', 'trail'));
rows = parseReport(h, glyphs, kinds);
fprintf('== trailing newline vs none: %d rows, kinds %s; identical words %d\n', numel(rows), strjoin({rows.kind}, ' '), contains(h, 'identical'));
f = fopen(fullfile(work, 'crlf_l.m'), 'w'); fprintf(f, 'a = 1;\r\nb = 2;\r\n'); fclose(f);
c = visdiff(fullfile(work, 'crlf_l.m'), fullfile(work, 'trail_l.m'));
h = fileread(publish(c, 'Format', 'html', 'OutputFolder', work, 'Name', 'crlf'));
rows = parseReport(h, glyphs, kinds);
fprintf('== CRLF vs LF: %d rows, kinds %s\n', numel(rows), strjoin({rows.kind}, ' '));

fid = fopen(fullfile(here, 'CompareOracle.json'), 'w', 'n', 'UTF-8');
fprintf(fid, '%s', jsonencode(oracle, 'PrettyPrint', true));
fclose(fid);
fprintf('oracle: %d pairs\n', numel(oracle));
fprintf('done\n');

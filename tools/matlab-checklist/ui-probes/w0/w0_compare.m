% w0_compare - what R2025b's app comparison holds (stage W0 of the App Designer window plan), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w0_compare
% visdiff with an output takes the no-window road (MlappDiffNoGUIProvider) and answers a
% comparisons.MlappComparison; publish writes its report. Pairs: an app and its copy, its copy with only
% App Details changed, with one callback line edited (through App Designer's own saveAppCode), and two
% different apps. The merge refusals are read from the code (MlappDiffGUIProvider, comparison.xml).
here = fileparts(mfilename('fullpath'));
src = fullfile(fileparts(here), 'ds8', 'rt', 'accept');
work = fullfile(here, 'rt', 'compare');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
short = @(s) strrep(strrep(char(s), work, '<compare>'), fullfile(fileparts(here), 'ds8'), '<ds8>');
left = fullfile(work, 'AcceptForm.mlapp');
copyfile(fullfile(src, 'AcceptForm.mlapp'), left);
mk = @(name) fullfile(work, name, 'AcceptForm.mlapp');
mkdir(fullfile(work, 'same')); copyfile(left, mk('same'));
mkdir(fullfile(work, 'details')); copyfile(left, mk('details'));
appdesigner.internal.serialization.FileWriter(mk('details')).writeAppMetadata(struct('Name', 'Changed', 'Version', '3.0'));
mkdir(fullfile(work, 'code'));
[codeData, compat] = appdesigner.internal.comparison.getAppData(left);
fprintf('== getAppData: code data fields %s\n', strjoin(fieldnames(codeData)', ', '));
fprintf('   compatibility fields %s\n', strjoin(fieldnames(compat)', ', '));
text = appdesigner.internal.codegeneration.getAppFileCode(left);
edited = strrep(text, 'r = a + b;', 'r = a + b + 0;');
cbs = codeData.Callbacks;
fprintf('   Callbacks: %s %s, fields %s\n', class(cbs), mat2str(size(cbs)), strjoin(fieldnames(cbs)', ', '));
for k = 1:numel(cbs)
    if isfield(cbs(k), 'Code')
        cbs(k).Code = strrep(cbs(k).Code, 'r = a + b;', 'r = a + b + 0;');
    end
end
o = appdesigner.internal.comparison.saveAppCode(left, mk('code'), edited, struct('Callbacks', cbs), true);
if isfield(o, 'Message'), msg = o.Message; else, msg = ''; end
fprintf('saveAppCode -> %s %s\n', o.Status, short(msg));
copyfile(fullfile(src, 'AcceptGrid.mlapp'), fullfile(work, 'AcceptGrid.mlapp'));

pairs = {{'same', mk('same')}, {'details', mk('details')}, {'code', mk('code')}, {'other app', fullfile(work, 'AcceptGrid.mlapp')}};
for k = 1:numel(pairs)
    name = pairs{k}{1}; right = pairs{k}{2};
    fprintf('== %s: %s vs %s\n', name, short(left), short(right));
    try
        c = visdiff(left, right);
        fprintf('class %s; Left %s; Right %s\n', class(c), short(c.Left), short(c.Right));
        fprintf('methods: %s\n', strjoin(methods(c)', ', '));
        r = publish(c, 'Format', 'html', 'OutputFolder', work, 'Name', ['report_' strrep(name, ' ', '_')]);
        fprintf('report %s\n', short(r));
        h = fileread(r);
        fprintf('report: %d bytes; "No changes detected" %d; section headings: %s\n', numel(h), ...
            contains(h, 'No changes detected'), strjoin(strip(strip(regexprep( ...
            regexp(h, 'class="ReportSectionHeading"[^>]*>.*?<', 'match'), '.*>|<', '')), 'both', char(160))', ' | '));
        % Each changed row carries a white glyph in its middle cell: + inserted, U+2212 deleted, U+2260 changed.
        rows = regexp(h, '<tr.*?</tr>', 'match');
        glyphs = {'+', char(8722), char(8800)};
        counts = zeros(1, 3);
        shown = 0;
        for i = 1:numel(rows)
            g = regexp(rows{i}, 'vertical-align:middle;">([^<]*)<', 'tokens', 'once');
            if isempty(g) || contains(rows{i}, 'Insertion'), continue, end % the legend row
            j = find(strcmp(glyphs, strtrim(g{1})));
            if isempty(j), continue, end
            counts(j) = counts(j) + 1;
            if shown < 6
                shown = shown + 1;
                t = regexprep(regexprep(rows{i}, '<[^>]*>', ' '), '&lt;', '<');
                fprintf('    %s\n', regexprep(strtrim(t), '\s+', ' '));
            end
        end
        fprintf('  rows %d: inserted %d, deleted %d, changed %d\n', numel(rows), counts);
        try
            filter(c, 'unfiltered');
        catch e
            fprintf('filter -> %s: %s\n', e.identifier, e.message);
        end
    catch e
        fprintf('-> %s: %s\n', e.identifier, short(e.message));
    end
end
try
    c = visdiff(left, fullfile(src, 'AcceptFormPlain.m'));
    fprintf('== mlapp vs plain-text .m: %s\n', class(c));
catch e
    fprintf('== mlapp vs plain-text .m -> %s: %s\n', e.identifier, short(e.message));
end
fprintf('done\n');

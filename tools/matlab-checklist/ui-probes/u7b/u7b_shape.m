% U7b: the exact shape of an .mlapp's design-time code copy, and where its pieces sit in the text.
% MathWorks' files are read in place and nothing of theirs is written here but counts and shapes.
files = dir(fullfile(matlabroot,'toolbox','**','*.mlapp'));
fprintf('%d shipped .mlapp files\n', numel(files));
tally = containers.Map();
for k = 1:numel(files)
    fn = fullfile(files(k).folder, files(k).name);
    try
        raw = char(matlab.internal.getCode(fn));
        r = appdesigner.internal.serialization.FileReader(fn);
        c = r.readAppCodeData();
    catch e
        fprintf('%s: read error %s\n', files(k).name, e.identifier); continue
    end
    bump(tally, sprintf('text has CRLF=%d', contains(raw, sprintf('\r\n'))));
    bump(tally, sprintf('text ends with newline=%d', ~isempty(raw) && raw(end) == newline));
    lines = splitlines(strrep(raw, sprintf('\r\n'), newline));
    % --- anchors
    iProps = find(strcmp(strtrim(lines), '% Properties that correspond to app components'), 1);
    iCb = find(strcmp(strtrim(lines), '% Callbacks that handle component events'), 1);
    iInit = find(strcmp(strtrim(lines), '% Component initialization'), 1);
    iCtor = find(strcmp(strtrim(lines), '% App creation and deletion'), 1);
    bump(tally, sprintf('anchors props=%d cb=%d init=%d ctor=%d', ~isempty(iProps), ~isempty(iCb), ~isempty(iInit), ~isempty(iCtor)));
    if isempty(iProps), fprintf('  %s: no component properties comment; line 3 is "%s"\n', files(k).name, lines{min(3,end)}); end
    % end of the component properties block: first line that is exactly an `end` after it
    iPropsEnd = [];
    if ~isempty(iProps)
        iPropsEnd = iProps + find(strcmp(strtrim(lines(iProps+1:end)), 'end'), 1);
    end
    nextAnchor = min([iCb, iInit]);
    % --- editable section
    if isfield(c, 'EditableSectionCode')
        esc = c.EditableSectionCode;
        bump(tally, sprintf('ES class=%s rows=%d empty=%d', class(esc), size(esc,1), isempty(esc)));
        esc = esc(:)';
        hit = 0;
        for s = 1:numel(lines)-numel(esc)+1
            if isequal(lines(s:s+numel(esc)-1)', esc), hit = s; break; end
        end
        if hit && ~isempty(esc) && ~isempty(iPropsEnd)
            before = lines(iPropsEnd+1 : hit-1); after = lines(hit+numel(esc) : nextAnchor-1);
            bump(tally, sprintf('ES gap before=%s after=%s', vis(before), vis(after)));
            bump(tally, sprintf('ES first line blank=%d last line blank=%d', isempty(strtrim(esc{1})), isempty(strtrim(esc{end}))));
        elseif isempty(esc) && ~isempty(iPropsEnd)
            bump(tally, sprintf('ES empty: lines between props end and anchor=%s', vis(lines(iPropsEnd+1:nextAnchor-1))));
        else
            bump(tally, 'ES not located');
        end
        zc = cellfun(@(x) sprintf('%s%dx%d', class(x), size(x,1), size(x,2)), esc(cellfun(@isempty, esc)), 'UniformOutput', false);
        for z = unique(zc), bump(tally, ['ES empty line stored as ' z{1}]); end
    else
        bump(tally, sprintf('no ES field: between props end and anchor=%s', vis(lines(iPropsEnd+1:nextAnchor-1))));
    end
    % --- callbacks
    if isfield(c, 'Callbacks')
        bump(tally, sprintf('Callbacks class=%s size=%dx%d fields=%s', class(c.Callbacks), size(c.Callbacks,1), size(c.Callbacks,2), strjoin(fieldnames(c.Callbacks)', ',')));
        for j = 1:numel(c.Callbacks)
            cb = c.Callbacks(j);
            bump(tally, sprintf('cb Code class=%s rows=%d', class(cb.Code), size(cb.Code,1)));
            if isempty(cb.Code), bump(tally, sprintf('cb empty Code size=%dx%d', size(cb.Code,1), size(cb.Code,2))); end
            at = find(~cellfun(@isempty, regexp(lines, ['^\s*function\s+' cb.Name '\s*\('], 'once')));
            if numel(at) == 1
                sig = strtrim(lines{at});
                bump(tally, ['cb signature form: ' regexprep(sig, cb.Name, 'NAME')]);
                bump(tally, sprintf('cb comment line above=%d', startsWith(strtrim(lines{at-1}), '%')));
                bump(tally, sprintf('cb within callbacks block=%d', ~isempty(iCb) && at > iCb && at < iInit));
            else
                bump(tally, sprintf('cb signature found %d times', numel(at)));
                fprintf('  %s: callback %s found %d times\n', files(k).name, cb.Name, numel(at));
            end
        end
        % order: do the callbacks appear in the text in the order of the struct array?
        pos = zeros(1, numel(c.Callbacks));
        for j = 1:numel(c.Callbacks)
            a = find(~cellfun(@isempty, regexp(lines, ['^\s*function\s+' c.Callbacks(j).Name '\s*\('], 'once')), 1);
            if ~isempty(a), pos(j) = a; end
        end
        bump(tally, sprintf('callbacks in text order=%d', issorted(pos)));
        % are there functions in the callbacks block that are not in Callbacks and not the startup?
        if ~isempty(iCb)
            blk = lines(iCb:iInit-1);
            tok = regexp(blk, '^\s*function\s+(?:\S+\s*=\s*)?(\w+)\s*\(', 'tokens', 'once');
            names = cellfun(@(t) t{1}, tok(~cellfun(@isempty, tok)), 'UniformOutput', false);
            known = {c.Callbacks.Name};
            if isfield(c,'StartupCallback') && isfield(c.StartupCallback,'Name'), known{end+1} = c.StartupCallback.Name; end
            extra = setdiff(names, known);
            bump(tally, sprintf('callbacks block has %d extra functions', numel(extra)));
            if ~isempty(extra), fprintf('  %s: extra in callbacks block: %s\n', files(k).name, strjoin(extra, ',')); end
        end
    else
        bump(tally, 'no Callbacks field');
    end
    % --- startup
    if isfield(c, 'StartupCallback')
        sc = c.StartupCallback;
        bump(tally, sprintf('Startup class=%s size=%dx%d fields=%s', class(sc), size(sc,1), size(sc,2), strjoin(fieldnames(sc)', ',')));
        if isfield(sc, 'Name')
            bump(tally, sprintf('Startup name class=%s empty=%d; Code class=%s rows=%d', class(sc.Name), isempty(sc.Name), class(sc.Code), size(sc.Code,1)));
            if ~isempty(sc.Name)
                at = find(~cellfun(@isempty, regexp(lines, ['^\s*function\s+' sc.Name '\s*\('], 'once')));
                bump(tally, sprintf('startup comment above="%s"', strtrim(lines{at(1)-1})));
                bump(tally, sprintf('startup is first in callbacks block=%d', at(1) == iCb + 4));
            end
        end
    else
        bump(tally, 'no StartupCallback field');
    end
    if isfield(c, 'InputParameters')
        bump(tally, sprintf('InputParameters class=%s size=%dx%d', class(c.InputParameters), size(c.InputParameters,1), size(c.InputParameters,2)));
    end
    if isfield(c, 'SingletonMode')
        bump(tally, sprintf('SingletonMode class=%s value=%s', class(c.SingletonMode), mat2str(c.SingletonMode)));
    end
    if isfield(c, 'AppTypeData')
        a = c.AppTypeData;
        if isstruct(a), bump(tally, sprintf('AppTypeData struct %dx%d fields=%s', size(a,1), size(a,2), strjoin(fieldnames(a)', ',')));
        else, bump(tally, sprintf('AppTypeData class=%s size=%dx%d', class(a), size(a,1), size(a,2))); end
    end
    bump(tally, sprintf('ClassName class=%s matches file=%d', class(c.ClassName), strcmp(c.ClassName, erase(files(k).name, '.mlapp'))));
    % callbacks block lines that precede each function: blank, comment, function
    if strcmp(files(k).name, 'SystemLogViewer.mlapp') || strcmp(files(k).name, 'app1.mlapp') || strcmp(files(k).name, 'InstrumentationVisualizer.mlapp')
        fprintf('--- %s skeleton ---\n', files(k).name);
        for i = 1:numel(lines)
            L = lines{i};
            if ~isempty(regexp(L, '^\s*(classdef|properties|methods|function|events|enumeration)', 'once')) || (startsWith(strtrim(L), '%') && numel(L) - numel(strtrim(L)) <= 8 && i < 400 && ~contains(L, 'Create '))
                fprintf('%4d: %s\n', i, regexprep(L, '(?<=function\s.{0,60}\().*', '...'));
            end
        end
    end
end
ks = sort(keys(tally));
fprintf('=== tally ===\n');
for i = 1:numel(ks), fprintf('%4d  %s\n', tally(ks{i}), ks{i}); end

% --- the package: parts, their order, compression and what the MAT-file holds
fn = fullfile(matlabroot,'toolbox','comm','comm','+comm','+internal','+bertool','DataExport.mlapp');
r = appdesigner.internal.serialization.FileReader(fn);
[d, ver] = r.readAppDesignerData();
fprintf('=== DataExport package: version %s ===\n', mat2str(ver));
fns = fieldnames(d);
for i = 1:numel(fns)
    v = d.(fns{i});
    fprintf('appData.%s : %s %s\n', fns{i}, class(v), mat2str(size(v)));
    if isstruct(v)
        g = fieldnames(v);
        for j = 1:numel(g), w = v.(g{j}); fprintf('    .%s : %s %s\n', g{j}, class(w), mat2str(size(w))); end
    elseif isobject(v)
        p = properties(v);
        for j = 1:numel(p)
            try, w = v.(p{j}); fprintf('    .%s : %s %s\n', p{j}, class(w), mat2str(size(w))); catch, end
        end
    end
end
m = r.readAppMetadata();
g = fieldnames(m);
for j = 1:numel(g)
    w = m.(g{j});
    if ischar(w) || isstring(w), s = char(w); else, s = mat2str(size(w)); end
    if strcmp(g{j}, 'Name') || contains(lower(g{j}), 'version') || contains(lower(g{j}), 'release'), fprintf('metadata.%s : %s = %s\n', g{j}, class(w), s); else, fprintf('metadata.%s : %s\n', g{j}, class(w)); end
end

function bump(t, key)
    if isKey(t, key), t(key) = t(key) + 1; else, t(key) = 1; end
end

function s = vis(c)
    % How many lines, and each one's length when it is all white space ("b<n>") or "TEXT".
    parts = cell(1, numel(c));
    for i = 1:numel(c)
        if isempty(strtrim(c{i})), parts{i} = sprintf('b%d', numel(c{i})); else, parts{i} = 'TEXT'; end
    end
    s = ['[' strjoin(parts, ' ') ']'];
end

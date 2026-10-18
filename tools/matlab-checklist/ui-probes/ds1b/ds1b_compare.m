function ds1b_compare(subfolder)
% DS1b (designer plan): loads each rt/<subfolder>/<source>__<variant>.mat and compares every object
% of its component tree with the same object loaded from <source>__id.mat (or, when there is none,
% <source>__ref.mat), by position in findall's order, through ds1b_objdiff. Then it sets a property
% on each loaded component, to see that the object works. One line per file, with any warning load
% gave.
folder = fullfile(pwd, 'rt', subfolder);
files = dir(fullfile(folder, '*.mat'));
refs = struct();
for k = 1:numel(files)
    name = files(k).name;
    parts = split(extractBefore(name, '.mat'), '__');
    if numel(parts) ~= 2, continue; end % not a <source>__<variant> file
    source = parts{1}; variant = parts{2};
    if any(strcmp(variant, {'id', 'ref'})), continue; end
    if ~isfield(refs, source)
        refFile = fullfile(folder, [source '__id.mat']);
        if ~isfile(refFile), refFile = fullfile(folder, [source '__ref.mat']); end
        R = load(refFile);
        refs.(source) = findall(R.components.UIFigure);
    end
    ref = refs.(source);
    lastwarn('');
    try
        S = load(fullfile(folder, name));
    catch e
        fprintf('%s: load error %s: %s\n', name, e.identifier, firstLine(e.message));
        continue
    end
    [w, wid] = lastwarn;
    if ~isfield(S, 'components') || ~isfield(S.components, 'UIFigure') || ~isa(S.components.UIFigure, 'matlab.ui.Figure') || ~isvalid(S.components.UIFigure)
        fprintf('%s: loads, but no valid figure%s\n', name, warnText(w, wid));
        continue
    end
    got = findall(S.components.UIFigure);
    head = '';
    if numel(got) ~= numel(ref)
        head = sprintf(' (%d objects, reference %d)', numel(got), numel(ref));
    end
    diffs = {};
    for i = 1:min(numel(got), numel(ref))
        diffs = [diffs, ds1b_objdiff(got(i), ref(i))]; %#ok<AGROW>
    end
    worked = tryUse(got);
    if isempty(diffs)
        fprintf('%s: same%s%s%s\n', name, head, warnText(w, wid), worked);
    else
        fprintf('%s: differs%s in %s%s%s\n', name, head, strjoin(diffs, ', '), warnText(w, wid), worked);
    end
    delete(S.components.UIFigure);
end
end

function s = warnText(w, wid)
if isempty(w), s = ''; else, s = sprintf(' [warning %s: %s]', wid, firstLine(w)); end
end

function s = firstLine(t)
t = char(t); k = find(t == newline, 1);
if isempty(k), s = t; else, s = t(1:k - 1); end
end

function s = tryUse(objs)
s = '';
for i = 1:numel(objs)
    o = objs(i);
    try
        if isprop(o, 'Text'), o.Text = 'Changed'; end
        if isprop(o, 'Position') && ~(isprop(o, 'Layout') && isa(o.Layout, 'matlab.ui.layout.GridLayoutOptions')) ...
                && ~isa(o, 'matlab.ui.container.Tab') && ~isa(o, 'matlab.ui.container.Menu') && ~isa(o, 'matlab.ui.container.ContextMenu')
            o.Position = o.Position + [1 1 0 0];
        end
        drawnow
    catch e
        s = sprintf('%s; using %s fails: %s', s, class(o), firstLine(e.message));
    end
end
end

% DS1b (designer plan): loads each tree JGraph's DesignTreeWriter wrote (rt/jgraph/<App>__x.mat, left
% by the local test AppDesignerTreeWriterTests with the createComponents it stands for) and compares
% it, component by component (by code name, through ds1b_objdiff), with the tree that
% createComponents makes in R2025b as App Designer would hold it (ds1b_reference), and each
% container's children in order. The fixture app is this project's; the others are the apps R2025b
% ships (ds1b_appsource). One line per app: same, or how it differs, with any warning load gave.
% Headless. Nothing of MathWorks' is printed but where a value differs.
here = fileparts(mfilename('fullpath'));
addpath(here);
folder = fullfile(here, 'rt', 'jgraph');
files = dir(fullfile(folder, '*__x.mat'));
only = getenv('DS1B_ONLY');
tally = struct('same', 0, 'differs', 0, 'skipped', 0);
for k = 1:numel(files)
    app = extractBefore(files(k).name, '__x.mat');
    if ~isempty(only) && ~strcmp(only, app), continue; end
    src = ds1b_appsource(app);
    try
        [ref, why] = ds1b_reference(src.method, src.folder, src.appType);
    catch e
        ref = []; why = strtok(e.message, newline);
    end
    if isempty(ref)
        fprintf('%s: skipped (%s)\n', app, why);
        tally.skipped = tally.skipped + 1;
        continue
    end
    lastwarn('');
    previous = cd(src.folder); % where a file the app names without a path is found
    S = load(fullfile(folder, files(k).name));
    cd(previous);
    [w, wid] = lastwarn;
    got = byCodeName(S.components.UIFigure);
    want = byCodeName(ref);
    diffs = {};
    missing = setdiff(keys(want), keys(got));
    extra = setdiff(keys(got), keys(want));
    if ~isempty(missing), diffs{end + 1} = ['missing ' strjoin(missing, ' ')]; end %#ok<SAGROW>
    if ~isempty(extra), diffs{end + 1} = ['extra ' strjoin(extra, ' ')]; end %#ok<SAGROW>
    for name = intersect(keys(got), keys(want))
        a = got(name{1}); b = want(name{1});
        d = strcat(name{1}, ':', ds1b_objdiff(a, b));
        if ~isequal(childNames(a), childNames(b))
            d{end + 1} = sprintf('%s: children %s (ref %s)', name{1}, strjoin(childNames(a), ' '), strjoin(childNames(b), ' ')); %#ok<AGROW>
        end
        diffs = [diffs, d]; %#ok<AGROW>
    end
    if ~isempty(w), diffs{end + 1} = sprintf('[warning %s: %s]', wid, strtok(w, newline)); end %#ok<SAGROW>
    if isempty(diffs)
        fprintf('%s: same (%d components)\n', app, numel(keys(got)));
        tally.same = tally.same + 1;
    else
        fprintf('%s: differs in %s\n', app, strjoin(diffs, ', '));
        tally.differs = tally.differs + 1;
    end
    delete(S.components.UIFigure);
    delete(ref);
end
disp(tally)

function m = byCodeName(fig)
    % The tree's components by their code names.
    m = containers.Map();
    for h = findall(fig, '-property', 'DesignTimeProperties')'
        m(h.DesignTimeProperties.CodeName) = h;
    end
end

function names = childNames(h)
    % The code names of a component's children, in MATLAB's Children order.
    names = {};
    if ~isprop(h, 'Children'), return; end
    for c = h.Children'
        if isprop(c, 'DesignTimeProperties'), names{end + 1} = c.DesignTimeProperties.CodeName; end %#ok<AGROW>
    end
end

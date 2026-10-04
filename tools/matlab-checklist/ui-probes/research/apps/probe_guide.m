% probe_guide.m - can a GUIDE app run in R2025b without GUIDE; what is in a .fig. All invisible.
here = pwd;
diary(fullfile(here, 'probe_guide.out')); diary on
cleanupFigs = onCleanup(@() delete(findall(groot, 'Type', 'figure')));
set(groot, 'DefaultFigureVisible', 'off');

fprintf('--- GUIDE runtime presence\n');
fprintf('gui_mainfcn: %s\n', which('gui_mainfcn'));
fprintf('guide: %s\n', which('guide'));
fprintf('openfigLegacy exists: %d\n', exist('matlab.hg.internal.openfigLegacy') > 0);
try, guide; catch e, fprintf('guide() -> %s: %s\n', e.identifier, e.message); end

fprintf('--- build myguide.fig (figure + 3 uicontrols)\n');
gp = fullfile(here, 'guide_pair');
f = figure('Visible', 'off', 'Tag', 'figure1', 'Name', 'myguide', 'NumberTitle', 'off', ...
    'MenuBar', 'none', 'IntegerHandle', 'off', 'HandleVisibility', 'callback', 'Units', 'characters', ...
    'Position', [100 30 60 12]);
uicontrol(f, 'Style', 'pushbutton', 'Tag', 'pushbutton1', 'String', 'Add', 'Units', 'characters', ...
    'Position', [40 2 15 2], ...
    'Callback', @(hObject,eventdata)myguide('pushbutton1_Callback',hObject,eventdata,guidata(hObject)));
uicontrol(f, 'Style', 'edit', 'Tag', 'edit1', 'String', '2', 'Units', 'characters', ...
    'Position', [5 2 30 2], ...
    'Callback', @(hObject,eventdata)myguide('edit1_Callback',hObject,eventdata,guidata(hObject)), ...
    'CreateFcn', @(hObject,eventdata)myguide('edit1_CreateFcn',hObject,eventdata,guidata(hObject)));
uicontrol(f, 'Style', 'text', 'Tag', 'text1', 'String', 'Count: 0', 'Units', 'characters', ...
    'Position', [5 7 50 2]);
setappdata(f, 'GUIDEOptions', struct('active_h', [], 'taginfo', [], 'override', 0, 'release', 13, ...
    'resize', 'none', 'accessibility', 'callback', 'mfile', 1, 'callbacks', 1, 'singleton', 1, ...
    'syscolorfig', 1, 'blocking', 0, 'lastSavedFile', ''));
hgsave(f, fullfile(gp, 'myguide.fig'));
delete(f);

fprintf('--- .fig as a MAT-file\n');
inspectFig(fullfile(gp, 'myguide.fig'));
lte = fullfile(matlabroot, 'toolbox', 'lte', 'lte', 'saLteDLConformanceTestBenchGUI.fig');
if isfile(lte)
    fprintf('shipped GUIDE fig: %s\n', lte);
    inspectFig(lte);
end

fprintf('--- run the GUIDE pair invisibly\n');
addpath(gp);
try
    h = myguide('Visible', 'off');
    fprintf('returned %s, Tag=%s, Visible=%s\n', class(h), h.Tag, h.Visible);
    hs = guidata(h);
    fprintf('handles fields: %s\n', strjoin(fieldnames(hs)', ', '));
    fprintf('edit1 BackgroundColor after CreateFcn: %s\n', mat2str(hs.edit1.BackgroundColor));
    % dispatch a callback exactly as the stored Callback would
    cb = get(hs.pushbutton1, 'Callback');
    fprintf('stored Callback: %s\n', func2str(cb));
    cb(hs.pushbutton1, []);
    hs = guidata(h);
    fprintf('after callback count=%d text=%s\n', hs.count, get(hs.text1, 'String'));
    % singleton: second call reuses the figure
    h2 = myguide('Visible', 'off');
    fprintf('singleton reuse same figure = %d\n', h2 == h);
    delete(h);
catch e
    fprintf('GUIDE run err: %s | %s\n', e.identifier, e.message);
    for k = 1:numel(e.stack), fprintf('   at %s:%d\n', e.stack(k).name, e.stack(k).line); end
end

fprintf('--- openfig / hgload of the shipped fig without its code\n');
if isfile(lte)
    try
        f2 = openfig(lte, 'new', 'invisible');
        fprintf('openfig ok: %s, %d objects, types: %s\n', class(f2), numel(findall(f2)), ...
            strjoin(unique(get(findall(f2), 'Type'))', ','));
        delete(f2);
    catch e, fprintf('openfig err: %s | %s\n', e.identifier, e.message); end
    try
        [f3, old] = hgload(lte, struct('Visible', 'off'));
        fprintf('hgload ok: %s\n', class(f3));
        delete(f3);
    catch e, fprintf('hgload err: %s | %s\n', e.identifier, e.message); end
end
diary off

function inspectFig(p)
    w = whos('-file', p);
    for k = 1:numel(w), fprintf('  var %s class %s size %s\n', w(k).name, w(k).class, mat2str(w(k).size)); end
    s = load(p, '-mat');
    fn = fieldnames(s);
    for k = 1:numel(fn)
        v = s.(fn{k});
        if isstruct(v)
            fprintf('  %s fields: %s\n', fn{k}, strjoin(fieldnames(v)', ', '));
            if isfield(v, 'type')
                fprintf('    type=%s, #children=%d\n', v.type, numel(v.children));
                if ~isempty(v.children)
                    c = v.children(1);
                    fprintf('    child(1) type=%s fields=%s\n', c.type, strjoin(fieldnames(c)', ', '));
                    pf = fieldnames(c.properties);
                    fprintf('    child(1) properties: %s\n', strjoin(pf(1:min(25, end))', ', '));
                    types = arrayfun(@(x) x.type, v.children, 'UniformOutput', false);
                    fprintf('    children types: %s\n', strjoin(unique(types)', ','));
                end
                pf = fieldnames(v.properties);
                fprintf('    figure properties (%d): %s\n', numel(pf), strjoin(pf(1:min(30, end))', ', '));
                if isfield(v.properties, 'ApplicationData')
                    fprintf('    ApplicationData: %s\n', strjoin(fieldnames(v.properties.ApplicationData)', ', '));
                end
            end
        else
            fprintf('  %s: %s %s\n', fn{k}, class(v), mat2str(size(v)));
        end
    end
end

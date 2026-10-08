% u11_guide - GUIDE pairs under R2025b's gui_mainfcn, and the CreateFcn order of a load. Headless.
here = fileparts(mfilename('fullpath'));
gp = fullfile(here, 'guide');
addpath(gp);
global U11LOG %#ok<GVMIS>
cleanupFigs = onCleanup(@() delete(findall(groot, 'Type', 'figure')));

% the pair's figure, made as research C made it, once by hgsave (hgS_070000) and once by savefig
% (hgM only); myguide0 is the same with gui_Singleton = 0.
makeFig('myguide', fullfile(gp, 'myguide.fig'), true);
makeFig('myguides', fullfile(gp, 'myguides.fig'), false);
makeFig('myguide0', fullfile(gp, 'myguide0.fig'), false);
makeFig('myguidev', fullfile(gp, 'myguidev.fig'), false, 'on');
src = fileread(fullfile(gp, 'myguide.m'));
writeText(fullfile(gp, 'myguides.m'), strrep(src, 'myguide', 'myguides'));
writeText(fullfile(gp, 'myguidev.m'), strrep(src, 'myguide', 'myguidev'));
writeText(fullfile(gp, 'myguide0.m'), strrep(strrep(src, 'myguide', 'myguide0'), 'gui_Singleton = 1;', 'gui_Singleton = 0;'));
rehash;

for name = {'myguide', 'myguides'}
    fn = str2func(name{1});
    fprintf('=== %s\n', name{1});
    U11LOG = {};
    h = fn('Visible', 'off');
    fprintf('log: %s\n', strjoin(U11LOG, ' || '));
    fprintf('class=%s Visible=%s HV=%s Tag=%s Name=%s Number=%s IntegerHandle=%s\n', class(h), h.Visible, ...
        h.HandleVisibility, h.Tag, h.Name, mat2str(h.Number), h.IntegerHandle);
    fprintf('init appdata after=%d Color=%s FileName=%s\n', isappdata(h, 'InGUIInitialization'), mat2str(h.Color, 4), ...
        strrep(h.FileName, here, '<here>'));
    hs = guidata(h);
    fprintf('handles: %s; openingArgs=%d\n', strjoin(fieldnames(hs)', ','), hs.openingArgs);
    fprintf('edit1 bg=%s\n', mat2str(hs.edit1.BackgroundColor));
    cb = get(hs.pushbutton1, 'Callback');
    fprintf('stored Callback: %s\n', func2str(cb));
    cb(hs.pushbutton1, []);
    hs = guidata(h);
    fprintf('after callback count=%d text=%s\n', hs.count, get(hs.text1, 'String'));
    fn('pushbutton1_Callback', hs.pushbutton1, [], guidata(h));
    fprintf('dispatched by name: text=%s\n', get(hs.text1, 'String'));
    try
        fn('nosuch_Callback', hs.pushbutton1, [], guidata(h));
    catch e, fprintf('no such callback: %s | %s\n', e.identifier, e.message); end
    try
        fn('pushbutton1_Nothing', hs.pushbutton1, [], guidata(h));
    catch e, fprintf('tag matches, no function: %s | %s\n', e.identifier, e.message); end
    try
        fn('edit1_CreateFcn', hs.edit1, [], guidata(h)); fprintf('createfcn by name ok\n');
    catch e, fprintf('createfcn by name: %s | %s\n', e.identifier, e.message); end
    U11LOG = {};
    h2 = fn('Visible', 'off', 'Name', 'Again');
    fprintf('singleton same=%d Name=%s log=%s\n', isequal(h2, h), h2.Name, strjoin(U11LOG, ' || '));
    U11LOG = {};
    fn('Visible', 'off');
    fprintf('nargout 0 log=%s ans exists=%d\n', strjoin(U11LOG, ' || '), exist('ans', 'var'));
    U11LOG = {};
    try
        h3 = fn(7, 'x');
        fprintf('numeric first: same=%d log=%s\n', isequal(h3, h), strjoin(U11LOG, ' || '));
    catch e, fprintf('numeric first: %s | %s\n', e.identifier, e.message); end
    U11LOG = {};
    try
        h3 = fn('extra');
        fprintf('lone word: same=%d Visible=%s log=%s\n', isequal(h3, h), h3.Visible, strjoin(U11LOG, ' || '));
    catch e, fprintf('lone word: %s | %s\n', e.identifier, e.message); end
    U11LOG = {};
    try
        h3 = fn('Visible', 'off', 'NoSuchProp', 1);
        fprintf('bad pair: ok %d log=%s\n', isgraphics(h3), strjoin(U11LOG, ' || '));
    catch e, fprintf('bad pair: %s | %s\n', e.identifier, e.message); end
    delete(h);
    U11LOG = {};
    h = fn('Visible', 'off');
    fprintf('after delete: new log=%s\n', strjoin(U11LOG, ' || '));
    delete(h);
end

fprintf('=== visible default\n');
U11LOG = {};
h = myguide;
fprintf('Visible=%s log=%s\n', h.Visible, strjoin(U11LOG, ' || '));
fprintf('gcf hidden: CurrentFigure is it=%d\n', isequal(get(groot, 'CurrentFigure'), h));
delete(h);

fprintf('=== myguidev (saved visible)\n');
U11LOG = {};
h = myguidev;
fprintf('Visible=%s log=%s\n', h.Visible, strjoin(U11LOG, ' || '));
delete(h);
h = myguidev('Visible', 'off');
fprintf('with Visible off: %s\n', h.Visible);
delete(h);
h = openfig(fullfile(gp, 'myguidev.fig'));
fprintf('openfig of it: Visible=%s HV=%s\n', h.Visible, h.HandleVisibility);
delete(h);
fprintf('=== myguide0\n');
a = myguide0('Visible', 'off'); b = myguide0('Visible', 'off');
fprintf('two figures=%d\n', ~isequal(a, b));
delete([a b]);

fprintf('=== myguidex\n');
U11LOG = {};
h = myguidex('Visible', 'off');
fprintf('log=%s Tag=%s Visible=%s\n', strjoin(U11LOG, ' || '), h.Tag, h.Visible);
U11LOG = {};
h2 = myguidex('Visible', 'off');
fprintf('reuse same=%d log=%s\n', isequal(h, h2), strjoin(U11LOG, ' || '));
delete(h);

fprintf('=== gui_mainfcn direct\n');
try, gui_mainfcn(); catch e, fprintf('no args: %s | %s\n', e.identifier, e.message); end
try, gui_mainfcn(struct('gui_Name', 'nofig_here', 'gui_Singleton', 1, 'gui_OpeningFcn', [], 'gui_OutputFcn', [], 'gui_LayoutFcn', [], 'gui_Callback', [])); catch e, fprintf('no fig: %s | %s\n', e.identifier, strrep(e.message, here, '<here>')); end

fprintf('=== create order\n');
f = figure('Visible', 'off', 'CreateFcn', @(s, e) u11log(['F ' get(s, 'Tag')]), 'Tag', 'F');
p1 = uipanel(f, 'Tag', 'P1', 'CreateFcn', @(s, e) u11log(['P ' get(s, 'Tag')]));
uicontrol(p1, 'Tag', 'C1', 'CreateFcn', @(s, e) u11log(['C ' get(s, 'Tag')]));
uicontrol(f, 'Tag', 'C2', 'CreateFcn', @(s, e) u11log(['C ' get(s, 'Tag')]));
a1 = axes(f, 'Tag', 'A1', 'NextPlot', 'add', 'CreateFcn', @(s, e) u11log(['A ' get(s, 'Tag')]));
line(a1, 1:2, 1:2, 'Tag', 'L1', 'CreateFcn', @(s, e) u11log(['L ' get(s, 'Tag')]));
line(a1, 1:2, 2:3, 'Tag', 'L2', 'CreateFcn', @(s, e) u11log(['L ' get(s, 'Tag')]));
a2 = axes(f, 'Tag', 'A2', 'NextPlot', 'add', 'Position', [0.6 0.6 0.3 0.3], 'CreateFcn', @(s, e) u11log(['A ' get(s, 'Tag')]));
text(a2, 0.5, 0.5, 'hi', 'Tag', 'T3', 'CreateFcn', @(s, e) u11log(['T ' get(s, 'Tag')]));
uimenu(f, 'Tag', 'M1', 'CreateFcn', @(s, e) u11log(['M ' get(s, 'Tag')]));
savefig(f, fullfile(gp, 'order.fig'));
warning('off', 'MATLAB:hgsave:HgsaveToBeRemoved');
hgsave(f, fullfile(gp, 'order7.fig'));
delete(f);
U11LOG = {};
h = openfig(fullfile(gp, 'order.fig'), 'invisible');
fprintf('savefig file: %s\n', strjoin(U11LOG, ', '));
fprintf('Children: %s\n', strjoin(get(h.Children, 'Tag')', ','));
delete(h);
U11LOG = {};
h = openfig(fullfile(gp, 'order7.fig'), 'invisible');
fprintf('hgsave file: %s\n', strjoin(U11LOG, ', '));
delete(h);
% only the hgS tree of the hgsave file
s = load(fullfile(gp, 'order7.fig'), '-mat', 'hgS_070000');
hgS_070000 = s.hgS_070000; %#ok<NASGU>
save(fullfile(gp, 'order7s.fig'), 'hgS_070000', '-mat');
U11LOG = {};
h = openfig(fullfile(gp, 'order7s.fig'), 'invisible');
fprintf('hgS only: %s\n', strjoin(U11LOG, ', '));
fprintf('hgS only Children: %s\n', strjoin(get(h.Children, 'Tag')', ','));
a = findobj(h, 'Tag', 'A1');
fprintf('hgS only A1 children: %s\n', strjoin(get(a.Children, 'Tag')', ','));
delete(h);

function makeFig(name, file, legacy, vis)
if nargin < 4, vis = 'off'; end
f = figure('Visible', vis, 'Tag', 'figure1', 'Name', name, 'NumberTitle', 'off', ...
    'MenuBar', 'none', 'IntegerHandle', 'off', 'HandleVisibility', 'callback', 'Units', 'characters', ...
    'Position', [100 30 60 12]);
uicontrol(f, 'Style', 'pushbutton', 'Tag', 'pushbutton1', 'String', 'Add', 'Units', 'characters', ...
    'Position', [40 2 15 2], ...
    'Callback', str2func(sprintf('@(hObject,eventdata)%s(''pushbutton1_Callback'',hObject,eventdata,guidata(hObject))', name)));
uicontrol(f, 'Style', 'edit', 'Tag', 'edit1', 'String', '2', 'Units', 'characters', ...
    'Position', [5 2 30 2], ...
    'Callback', str2func(sprintf('@(hObject,eventdata)%s(''edit1_Callback'',hObject,eventdata,guidata(hObject))', name)));
e = findobj(f, 'Tag', 'edit1');
e.CreateFcn = str2func(sprintf('@(hObject,eventdata)%s(''edit1_CreateFcn'',hObject,eventdata,guidata(hObject))', name));
uicontrol(f, 'Style', 'text', 'Tag', 'text1', 'String', 'Count: 0', 'Units', 'characters', ...
    'Position', [5 7 50 2]);
setappdata(f, 'GUIDEOptions', struct('active_h', [], 'taginfo', [], 'override', 0, 'release', 13, ...
    'resize', 'none', 'accessibility', 'callback', 'mfile', 1, 'callbacks', 1, 'singleton', 1, ...
    'syscolorfig', 1, 'blocking', 0, 'lastSavedFile', ''));
if legacy
    warning('off', 'MATLAB:hgsave:HgsaveToBeRemoved');
    hgsave(f, file);
else
    savefig(f, file);
end
delete(f);
end

function writeText(file, text)
fid = fopen(file, 'w');
fwrite(fid, text);
fclose(fid);
end

% u11_guiderun - runs the GUIDE pairs u11_guide wrote, in R2025b and in JGraph; the outputs are diffed.
here = fileparts(mfilename('fullpath'));
gp = fullfile(here, 'guide');
addpath(gp);
global U11LOG %#ok<GVMIS>
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
try, gui_mainfcn(struct('gui_Name', 'nofig_here', 'gui_Singleton', 1, 'gui_OpeningFcn', [], 'gui_OutputFcn', [], 'gui_LayoutFcn', [], 'gui_Callback', [])); catch e, fprintf('no fig: %s | %s\n', e.identifier, e.message); end

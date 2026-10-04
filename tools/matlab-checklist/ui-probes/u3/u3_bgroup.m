function u3_bgroup
% U3 probe: uibuttongroup - defaults in both figure kinds, which buttons it manages, SelectedObject,
% the callbacks and their older names. Headless: run-probe.ps1 -Name u3_bgroup.
global LOG
LOG = {};
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);

%% defaults and lists
bg = uibuttongroup(f);
ubg = uibuttongroup(uf);
p = uipanel(f);
s = get(bg); names = fieldnames(s);
us = get(ubg);
fprintf('class=%s Type=%s\n', class(bg), bg.Type);
for k = 1:numel(names)
    fprintf('  %s = %s || %s\n', names{k}, v2s(s.(names{k})), v2s(us.(names{k})));
end
pn = fieldnames(get(p));
fprintf('names only in buttongroup: %s\n', strjoin(setdiff(names, pn)', ','));
fprintf('names only in panel: %s\n', strjoin(setdiff(pn, names)', ','));
fprintf('set names only in buttongroup: %s\n', strjoin(setdiff(fieldnames(set(bg)), fieldnames(set(p)))', ','));
for h = {'SelectionChangeFcn', 'Buttons', 'ResizeFcn', 'ShadowColor', 'TooltipString', 'SelectedObject', 'UIContextMenu'}
    tryp(['get ' h{1}], @() get(bg, h{1}));
end
delete(p); delete(ubg);

%% the forms of the call
c0 = uicontrol(f);
tryp('uibuttongroup(f) Parent', @() get(uibuttongroup(f), 'Parent') == f);
tryp('uibuttongroup(''Parent'',f,''Title'',''T'')', @() get(uibuttongroup('Parent', f, 'Title', 'T'), 'Title'));
tryp('uibuttongroup(f,struct)', @() get(uibuttongroup(f, struct('Title', 'S')), 'Title'));
tryp('uibuttongroup(''Title'')', @() uibuttongroup('Title'));
tryp('uibuttongroup(f,''Title'')', @() uibuttongroup(f, 'Title'));
tryp('uibuttongroup(5.5)', @() uibuttongroup(5.5));
tryp('uibuttongroup(c)', @() uibuttongroup(c0));
tryp('uibuttongroup(f,''Bogus'',1)', @() uibuttongroup(f, 'Bogus', 1));
tryp('uibuttongroup in panel', @() class(get(uibuttongroup(uipanel(f)), 'Parent')));
tryp('uipanel in buttongroup', @() class(get(uipanel(uibuttongroup(f)), 'Parent')));
tryp('axes in buttongroup', @() class(get(axes(uibuttongroup(f)), 'Parent')));
tryp('uibuttongroup in buttongroup', @() class(get(uibuttongroup(uibuttongroup(f)), 'Parent')));
tryp('nargout uibuttongroup', @() nargout('uibuttongroup'));
clf(f);

%% adding buttons
bg = uibuttongroup(f, 'Units', 'pixels', 'Position', [20 20 300 300], 'Tag', 'bg');
show = @(label, hs) fprintf('%s: values=[%s] selected=%s\n', label, strjoin(arrayfun(@(h) sprintf('%s:%s', h.Tag, mat2str(h.Value)), hs, 'UniformOutput', false), ' '), tagOf(bg.SelectedObject));
r1 = uicontrol(bg, 'Style', 'radiobutton', 'String', 'A', 'Tag', 'r1', 'Position', [10 250 100 20]);
show('first radio', r1);
r2 = uicontrol(bg, 'Style', 'radiobutton', 'String', 'B', 'Tag', 'r2', 'Position', [10 220 100 20]);
show('second radio', [r1 r2]);
t1 = uicontrol(bg, 'Style', 'togglebutton', 'String', 'T', 'Tag', 't1', 'Position', [10 190 100 20]);
cb = uicontrol(bg, 'Style', 'checkbox', 'String', 'C', 'Tag', 'cb', 'Position', [10 160 100 20]);
pb = uicontrol(bg, 'Style', 'pushbutton', 'String', 'P', 'Tag', 'pb', 'Position', [10 130 100 20]);
all5 = [r1 r2 t1 cb pb];
show('five', all5);
fprintf('Children tags: %s\n', strjoin(arrayfun(@(h) h.Tag, bg.Children, 'UniformOutput', false)', ' '));
tryp('Buttons', @() tagsOf(get(bg, 'Buttons')));

%% selecting through Value
r2.Value = 1; show('r2.Value=1', all5);
r2.Value = 0; show('r2.Value=0 (selected one)', all5);
r1.Value = 0; show('r1.Value=0 (unselected)', all5);
t1.Value = 1; show('t1.Value=1', all5);
cb.Value = 1; show('cb.Value=1', all5);
pb.Value = 1; show('pb.Value=1', all5);
r1.Value = 0.5; show('r1.Value=0.5', all5);
r1.Value = 5; show('r1.Value=5', all5);
r1.Value = true; show('r1.Value=true', all5);
r2.Value = r2.Max; show('r2.Value=Max', all5);
cb.Value = 0; pb.Value = 0;

%% selecting through SelectedObject
tryp('SelectedObject<-r1', @() tagOf(setget(bg, 'SelectedObject', r1))); show('  ', all5);
tryp('SelectedObject<-t1', @() tagOf(setget(bg, 'SelectedObject', t1))); show('  ', all5);
tryp('SelectedObject<-[]', @() tagOf(setget(bg, 'SelectedObject', []))); show('  ', all5);
tryp('SelectedObject<-cb', @() tagOf(setget(bg, 'SelectedObject', cb))); show('  ', all5);
tryp('SelectedObject<-pb', @() tagOf(setget(bg, 'SelectedObject', pb))); show('  ', all5);
tryp('SelectedObject<-f', @() tagOf(setget(bg, 'SelectedObject', f))); show('  ', all5);
out = uicontrol(f, 'Style', 'radiobutton', 'Tag', 'out', 'Position', [400 20 100 20]);
tryp('SelectedObject<-outside radio', @() tagOf(setget(bg, 'SelectedObject', out))); show('  ', all5);
tryp('SelectedObject<-5', @() tagOf(setget(bg, 'SelectedObject', 5))); show('  ', all5);
tryp('SelectedObject<-''r1''', @() tagOf(setget(bg, 'SelectedObject', 'r1')));
tryp('SelectedObject<-[r1 r2]', @() tagOf(setget(bg, 'SelectedObject', [r1 r2]))); show('  ', all5);
tryp('SelectedObject<-double(r2)', @() tagOf(setget(bg, 'SelectedObject', double(r2)))); show('  ', all5);
tryp('class of empty SelectedObject', @() classAfterClear(bg));

%% Min and Max
bg.SelectedObject = r1;
r2.Min = 2; r2.Max = 7; show('r2 Min 2 Max 7 (unselected)', all5);
r2.Value = 7; show('r2.Value=7', all5);
r1.Value = 1; show('r1.Value=1', all5);
r2.Value = 1; show('r2.Value=1 (neither Min nor Max)', all5);
r2.Min = 0; r2.Max = 1;

%% moving in and out, deleting, changing style
bg.SelectedObject = r1;
out.Value = 1; out.Parent = bg; show('outside radio with Value 1 moved in', [all5 out]);
out.Parent = f; show('moved out again', [all5 out]); fprintf('  out.Value=%s\n', mat2str(out.Value));
bg.SelectedObject = r2; r2.Parent = f; show('selected r2 moved out', [r1 t1 cb pb]); fprintf('  r2.Value=%s\n', mat2str(r2.Value));
r2.Parent = bg; show('r2 moved back', all5);
bg.SelectedObject = t1; delete(t1); show('selected t1 deleted', [r1 r2 cb pb]);
pb.Style = 'radiobutton'; show('pb becomes a radiobutton', [r1 r2 cb pb]);
pb.Value = 1; show('  and is selected', [r1 r2 cb pb]);
pb.Style = 'pushbutton'; show('  and becomes a pushbutton again', [r1 r2 cb pb]);
r1.Visible = 'off'; r1.Value = 1; show('invisible r1 selected', [r1 r2 cb pb]); r1.Visible = 'on';
r2.Enable = 'off'; r2.Value = 1; show('disabled r2 selected', [r1 r2 cb pb]); r2.Enable = 'on';

%% created with a Value
bg2 = uibuttongroup(f, 'Units', 'pixels', 'Position', [330 100 200 200]);
a = uicontrol(bg2, 'Style', 'radiobutton', 'Tag', 'a', 'Value', 0);
fprintf('first radio made with Value 0: a=%s selected=%s\n', mat2str(a.Value), tagOf(bg2.SelectedObject));
b = uicontrol(bg2, 'Style', 'radiobutton', 'Tag', 'b', 'Value', 1);
fprintf('second made with Value 1: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg2.SelectedObject));
c = uicontrol('Parent', bg2, 'Style', 'radiobutton', 'Tag', 'c');
fprintf('third via Parent pair: c=%s selected=%s\n', mat2str(c.Value), tagOf(bg2.SelectedObject));
d = uicontrol('Style', 'radiobutton', 'Tag', 'd', 'Parent', bg2);
fprintf('fourth, Style before Parent: d=%s selected=%s\n', mat2str(d.Value), tagOf(bg2.SelectedObject));
bg3 = uibuttongroup(f, 'Units', 'pixels', 'Position', [330 10 200 80]);
t = uicontrol(bg3, 'Style', 'togglebutton', 'Tag', 't');
fprintf('first toggle: t=%s selected=%s\n', mat2str(t.Value), tagOf(bg3.SelectedObject));
e = uicontrol(bg3, 'Tag', 'e'); e.Style = 'radiobutton';
fprintf('pushbutton turned radio in a group with a selection: e=%s selected=%s\n', mat2str(e.Value), tagOf(bg3.SelectedObject));
bg4 = uibuttongroup(f, 'Units', 'pixels', 'Position', [330 310 200 80]);
g = uicontrol(bg4, 'Tag', 'g'); g.Style = 'radiobutton';
fprintf('pushbutton turned radio in an empty group: g=%s selected=%s\n', mat2str(g.Value), tagOf(bg4.SelectedObject));
bg5 = uibuttongroup(f, 'Units', 'pixels', 'Position', [10 330 200 80], 'SelectedObject', []);
h1 = uicontrol(bg5, 'Style', 'radiobutton', 'Tag', 'h1');
fprintf('group made with SelectedObject []: h1=%s selected=%s\n', mat2str(h1.Value), tagOf(bg5.SelectedObject));
inner = uipanel(bg5); h2 = uicontrol(inner, 'Style', 'radiobutton', 'Tag', 'h2', 'Value', 1);
fprintf('radio in a panel in the group: h1=%s h2=%s selected=%s\n', mat2str(h1.Value), mat2str(h2.Value), tagOf(bg5.SelectedObject));

%% callbacks
tryp('SelectionChangedFcn<-handle', @() setget(bg, 'SelectionChangedFcn', @(s, e) note('changed', e)));
tryp('SelectionChangeFcn reads', @() get(bg, 'SelectionChangeFcn'));
tryp('SelectionChangeFcn<-char', @() setget(bg, 'SelectionChangeFcn', 'disp(1)'));
tryp('SelectionChangedFcn reads', @() get(bg, 'SelectionChangedFcn'));
tryp('SelectionChangedFcn<-cell', @() setget(bg, 'SelectionChangedFcn', {@note, 1}));
tryp('SelectionChangedFcn<-5', @() setget(bg, 'SelectionChangedFcn', 5));
tryp('SelectionChangedFcn<-''''', @() setget(bg, 'SelectionChangedFcn', ''));
bg.SelectionChangedFcn = @(s, e) note('SelectionChangedFcn', e);
r1.Callback = @(s, e) note('r1.Callback', e);
r2.Callback = @(s, e) note('r2.Callback', e);
r1.Value = 1; drawnow; r2.Value = 1; drawnow; bg.SelectedObject = r1; drawnow; bg.SelectedObject = []; drawnow;
fprintf('callbacks after programmatic changes: %d\n', numel(LOG));
cellfun(@(l) fprintf('  %s\n', l), LOG);

%% the panel half
tryp('Title', @() setget(bg, 'Title', 'Group'));
tryp('InnerPosition titled', @() bg.InnerPosition);
tryp('Enable<-inactive', @() setget(bg, 'Enable', 'inactive'));
tryp('Enable<-off', @() setget(bg, 'Enable', 'off'));
fprintf('  r1.Enable=%s\n', r1.Enable);
tryp('BorderType<-etchedin', @() setget(bg, 'BorderType', 'etchedin'));
fprintf('  lastwarn=%s\n', lastwarn);
tryp('BackgroundColor<-none', @() setget(bg, 'BackgroundColor', 'none'));
tryp('ShadowColor<-r', @() setget(bg, 'ShadowColor', 'r'));
tryp('Clipping<-off', @() setget(bg, 'Clipping', 'off'));
tryp('Scrollable<-on', @() setget(bg, 'Scrollable', 'on'));
tryp('AutoResizeChildren<-on', @() setget(bg, 'AutoResizeChildren', 'on'));
tryp('getpixelposition', @() getpixelposition(bg));
tryp('findobj uibuttongroup', @() numel(findobj(f, 'Type', 'uibuttongroup')));
tryp('findobj radiobutton', @() numel(findobj(f, 'Style', 'radiobutton')));
tryp('ancestor(r1,''uibuttongroup'')', @() tagOf(ancestor(r1, 'uibuttongroup')));
tryp('isa panel', @() isa(bg, 'matlab.ui.container.Panel'));

%% delete order
set(bg, 'DeleteFcn', @(s, e) fprintf('  deleted bg\n'));
set(r1, 'DeleteFcn', @(s, e) fprintf('  deleted r1 (selected=%s)\n', tagOf(bg.SelectedObject)));
set(r2, 'DeleteFcn', @(s, e) fprintf('  deleted r2\n'));
bg.SelectedObject = r1;
delete(bg);

%% in a uifigure
ubg = uibuttongroup(uf, 'Tag', 'ubg');
tryp('uicontrol radio in a uifigure group', @() inUi(ubg));
tryp('uiradiobutton exists', @() exist('uiradiobutton')); %#ok<EXIST>
delete(f); delete(uf);
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function s = inUi(ubg)
a = uicontrol(ubg, 'Style', 'radiobutton', 'Tag', 'ua');
b = uicontrol(ubg, 'Style', 'radiobutton', 'Tag', 'ub');
s = sprintf('a=%s b=%s selected=%s', mat2str(a.Value), mat2str(b.Value), tagOf(ubg.SelectedObject));
b.Value = 1;
s = sprintf('%s ; after b.Value=1: a=%s b=%s selected=%s', s, mat2str(a.Value), mat2str(b.Value), tagOf(ubg.SelectedObject));
end

function s = classAfterClear(bg)
bg.SelectedObject = [];
v = bg.SelectedObject;
s = sprintf('%s %s', class(v), mat2str(size(v)));
end

function s = tagOf(h)
if isempty(h), s = '(none)'; elseif ~isscalar(h), s = sprintf('(%d handles)', numel(h)); else, s = h.Tag; end
end

function s = tagsOf(h)
if isempty(h), s = sprintf('(empty %s %s)', class(h), mat2str(size(h))); return; end
s = strjoin(arrayfun(@(x) x.Tag, h, 'UniformOutput', false), ' ');
end

function note(what, e)
global LOG
LOG{end + 1} = sprintf('%s %s', what, class(e));
end

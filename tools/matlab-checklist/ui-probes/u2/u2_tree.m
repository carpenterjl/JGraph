function u2_tree
% U2 probe: the tree - Children order across kinds, uistack, findobj/findall/allchild over
% containers, HandleVisibility's three states, reparenting and delete order.
% Headless: run-probe.ps1 -Name u2_tree.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);

%% Children order across kinds
a1 = axes(f, 'Tag', 'a1');
c1 = uicontrol(f, 'Tag', 'c1');
p1 = uipanel(f, 'Tag', 'p1');
a2 = axes(f, 'Tag', 'a2');
c2 = uicontrol(f, 'Tag', 'c2');
fprintf('created a1 c1 p1 a2 c2 -> Children: %s\n', tags(f.Children));
fprintf('allchild: %s ; findobj(f): %s\n', tags(allchild(f)), tags(findobj(f)));
c3 = uicontrol(p1, 'Tag', 'c3'); a3 = axes(p1, 'Tag', 'a3'); p2 = uipanel(p1, 'Tag', 'p2'); c4 = uicontrol(p2, 'Tag', 'c4');
fprintf('p1.Children: %s\n', tags(p1.Children));
fprintf('findobj(f): %s\n', tags(findobj(f)));
fprintf('findobj(f,''-depth'',1): %s\n', tags(findobj(f, '-depth', 1)));
fprintf('findobj(p1): %s\n', tags(findobj(p1)));
fprintf('findobj(f,''Type'',''uicontrol''): %s\n', tags(findobj(f, 'Type', 'uicontrol')));
fprintf('findobj(f,''Type'',''uipanel''): %s\n', tags(findobj(f, 'Type', 'uipanel')));
fprintf('findall(f,''Type'',''axes''): %s\n', tags(findall(f, 'Type', 'axes')));
fprintf('findobj(''Tag'',''c4'') found %d\n', numel(findobj('Tag', 'c4')));
fprintf('findobj(f,''-property'',''Title'') : %s\n', tags(findobj(f, '-property', 'BorderType')));

%% uistack
tryp('uistack(c1,''top'')', @() st(f, c1, 'top'));
tryp('uistack(c1,''bottom'')', @() st(f, c1, 'bottom'));
tryp('uistack(p1,''up'')', @() st(f, p1, 'up'));
tryp('uistack(p1,''up'',2)', @() st(f, p1, 'up', 2));
tryp('uistack(p1,''down'',1)', @() st(f, p1, 'down', 1));
tryp('uistack(p1,''down'',99)', @() st(f, p1, 'down', 99));
tryp('uistack(a1,''top'')', @() st(f, a1, 'top'));
tryp('uistack([c1 c2],''top'')', @() st(f, [c1 c2], 'top'));
tryp('uistack(c1)', @() st(f, c1));
tryp('uistack(c1,''bogus'')', @() st(f, c1, 'bogus'));
tryp('uistack(c1,''up'',-1)', @() st(f, c1, 'up', -1));
tryp('uistack(c1,''up'',1.5)', @() st(f, c1, 'up', 1.5));
tryp('uistack(5.5,''top'')', @() st(f, 5.5, 'top'));
tryp('uistack(f,''top'')', @() st(f, f, 'top'));
tryp('uistack([c1 c3],''top'') different parents', @() st(f, [c1 c3], 'top'));
tryp('uistack(c3,''top'') in panel', @() st(p1, c3, 'top'));
tryp('nargout uistack', @() nargout('uistack'));
tryp('set Children reorder', @() setkids(f));
tryp('set Children missing one', @() set(f, 'Children', f.Children(1:2)));

%% HandleVisibility
c2.HandleVisibility = 'off';
p1.HandleVisibility = 'callback';
fprintf('c2 off, p1 callback: Children=%s\n', tags(f.Children));
fprintf('  allchild=%s\n', tags(allchild(f)));
fprintf('  findobj(f)=%s\n', tags(findobj(f)));
fprintf('  findall(f)=%s\n', tags(findall(f)));
fprintf('  findobj(p1)=%s (searching from a hidden one)\n', tags(findobj(p1)));
fprintf('  findobj(c2)=%s\n', tags(findobj(c2)));
fprintf('  ishandle(c2)=%d isvalid=%d ; get(c2,Tag)=%s\n', ishandle(c2), isvalid(c2), c2.Tag);
set(groot, 'ShowHiddenHandles', 'on');
fprintf('  ShowHiddenHandles on: Children=%s findobj(f)=%s\n', tags(f.Children), tags(findobj(f)));
set(groot, 'ShowHiddenHandles', 'off');
u = uicontrol(f, 'Tag', 'cb', 'Callback', @(s, e) fprintf('  in callback: Children=%s gcbo=%s gcbf==f %d\n', tags(get(ancestor(s, 'figure'), 'Children')), get(gcbo, 'Tag'), gcbf == ancestor(s, 'figure')));
cb = u.Callback; cb(u, []);
fprintf('  (called directly, not through a user action: gcbo empty? %d)\n', isempty(gcbo));
a2.HandleVisibility = 'off';
fprintf('a2 off: gca Tag=%s (a2 was current) ; f.CurrentAxes Tag=%s\n', get(gca, 'Tag'), get(f.CurrentAxes, 'Tag'));
f.HandleVisibility = 'off';
fprintf('f off: get(0,Children) has f? %d ; findobj(0,''Type'',''figure'') %d ; findall %d ; isempty(get(0,''CurrentFigure''))=%d\n', any(get(0, 'Children') == f), numel(findobj(0, 'Type', 'figure')), numel(findall(0, 'Type', 'figure')), isempty(get(0, 'CurrentFigure')));
g = gcf; fprintf('  gcf made a new figure? %d (Number %d)\n', g ~= f, g.Number); delete(g);
f.HandleVisibility = 'callback';
fprintf('f callback: get(0,Children) has f? %d\n', any(get(0, 'Children') == f));
close all; fprintf('  after close all: isvalid(f)=%d\n', isvalid(f));
f.HandleVisibility = 'on';
tryp('HandleVisibility bogus', @() set(f, 'HandleVisibility', 'bogus'));
tryp('HandleVisibility CALL', @() setget(c1, 'HandleVisibility', 'CALL'));
c1.HandleVisibility = 'on'; c2.HandleVisibility = 'on'; p1.HandleVisibility = 'on'; a2.HandleVisibility = 'on';

%% reparenting
c1.Units = 'pixels'; c1.Position = [20 30 60 20];
p1.Units = 'pixels'; p1.Position = [100 100 200 150];
c1.Parent = p1;
fprintf('c1 into p1: Position=%s gpp(true)=%s p1.Children=%s\n', mat2str(c1.Position), mat2str(getpixelposition(c1, true)), tags(p1.Children));
a1.Parent = p1;
fprintf('a1 into p1: Position=%s p1.Children=%s f.Children=%s\n', mat2str(a1.Position, 5), tags(p1.Children), tags(f.Children));
tryp('panel into its own child', @() set(p1, 'Parent', p2));
tryp('panel into itself', @() set(p1, 'Parent', p1));
tryp('uicontrol Parent = axes', @() set(c1, 'Parent', a2));
tryp('uicontrol Parent = uicontrol', @() set(c1, 'Parent', c2));
tryp('uicontrol Parent = []', @() setget(c2, 'Parent', []));
fprintf('  c2 with no parent: isvalid=%d f.Children=%s\n', isvalid(c2), tags(f.Children));
c2.Parent = f;
tryp('axes Parent = uicontrol', @() set(a2, 'Parent', c2));
f2 = figure('Visible', 'off');
p1.Parent = f2;
fprintf('p1 moved to f2: f.Children=%s f2.Children=%s ancestor(c4)==f2 %d\n', tags(f.Children), tags(f2.Children), ancestor(c4, 'figure') == f2);
p1.Parent = f;

%% delete order
objs = [f, p1, p2, c1, c3, c4, a1, a3, c2, a2];
for o = objs
    set(o, 'DeleteFcn', @(s, e) fprintf('  DeleteFcn %s (%s) BeingDeleted=%s parentBeingDeleted=%s kids=%d\n', get(s, 'Tag'), get(s, 'Type'), char(get(s, 'BeingDeleted')), pbd(s), numel(allchild(s))));
end
f.Tag = 'f';
fprintf('p1.Children before delete: %s\n', tags(p1.Children));
fprintf('delete(p2):\n'); delete(p2);
fprintf('  isvalid(c4)=%d ishandle(c4)=%d\n', isvalid(c4), ishandle(c4));
fprintf('delete(p1):\n'); delete(p1);
fprintf('f.Children before delete: %s\n', tags(f.Children));
fprintf('delete(f):\n'); delete(f);
tryp('get on deleted panel', @() get(p1, 'Title'));
tryp('set on deleted panel', @() set(p1, 'Title', 'x'));
tryp('uicontrol(deleted panel)', @() uicontrol(p1));
tryp('uipanel(deleted figure)', @() uipanel(f));
delete(f2);

%% clf and close with panels
f = figure('Visible', 'off');
p = uipanel(f, 'Tag', 'p', 'DeleteFcn', @(s, e) fprintf('  panel DeleteFcn\n'));
c = uicontrol(p, 'Tag', 'c', 'DeleteFcn', @(s, e) fprintf('  control DeleteFcn\n'));
hv = uicontrol(f, 'Tag', 'hidden', 'HandleVisibility', 'off');
fprintf('clf:\n'); clf(f);
fprintf('  after clf: allchild=%s\n', tags(allchild(f)));
clf(f, 'reset'); fprintf('  after clf reset: allchild=%s\n', tags(allchild(f)));
p = uipanel(f, 'Tag', 'p', 'DeleteFcn', @(s, e) fprintf('  panel DeleteFcn\n'));
c = uicontrol(p, 'Tag', 'c', 'DeleteFcn', @(s, e) fprintf('  control DeleteFcn\n'));
f.CloseRequestFcn = @(s, e) fprintf('  CloseRequestFcn then delete\n');
f.DeleteFcn = @(s, e) fprintf('  figure DeleteFcn\n');
fprintf('close(f) with a CloseRequestFcn that does not delete:\n'); close(f);
fprintf('  isvalid=%d\n', isvalid(f));
fprintf('delete(f):\n'); delete(f);
end

function s = pbd(h)
p = get(h, 'Parent');
if isempty(p) || ~isprop(p, 'BeingDeleted'), s = '-'; else, s = char(get(p, 'BeingDeleted')); end
end

function s = st(parent, varargin)
uistack(varargin{:});
s = tags(allchild(parent));
end

function s = setkids(f)
k = allchild(f);
set(f, 'Children', flipud(k));
s = tags(allchild(f));
set(f, 'Children', k);
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function s = tags(k)
if isempty(k), s = '(none)'; return; end
parts = arrayfun(@(x) get(x, 'Tag'), k, 'UniformOutput', false);
s = strjoin(parts(:)', ' ');
end

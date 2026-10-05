function u8_forms
% U8 probe: the forms of each maker and what each accepts as a parent. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
fns = {'uitable', 'uitabgroup', 'uitab', 'uimenu', 'uicontextmenu', 'uitoolbar', 'uipushtool', 'uitoggletool'};
ax = axes(f);
for k = 1:numel(fns)
    name = fns{k}; fn = str2func(name);
    before = findall(groot, 'Type', 'figure');
    tryp([name ' ()'], @() noarg(fn));
    delete(setdiff(findall(groot, 'Type', 'figure'), before));
    figure(f); set(f, 'Visible', 'off');
    tryp([name ' () with a current figure'], @() chain(fn()));
    tryp([name ' (uifigure)'], @() chain(fn(uf)));
    tryp([name ' (figure)'], @() chain(fn(f)));
    tryp([name ' (uipanel)'], @() chain(fn(uipanel(uf))));
    tryp([name ' (classic uipanel)'], @() chain(fn(uipanel(f))));
    tryp([name ' (grid)'], @() chain(fn(uigridlayout(uf))));
    tryp([name ' (uibuttongroup)'], @() chain(fn(uibuttongroup(uf))));
    tryp([name ' (tabgroup)'], @() chain(fn(uitabgroup(uf))));
    tryp([name ' (classic tabgroup)'], @() chain(fn(uitabgroup(f))));
    tryp([name ' (tab)'], @() chain(fn(uitab(uitabgroup(uf)))));
    tryp([name ' (classic tab)'], @() chain(fn(uitab(uitabgroup(f)))));
    tryp([name ' (menu)'], @() chain(fn(uimenu(uf))));
    tryp([name ' (classic menu)'], @() chain(fn(uimenu(f))));
    tryp([name ' (contextmenu)'], @() chain(fn(uicontextmenu(uf))));
    tryp([name ' (classic contextmenu)'], @() chain(fn(uicontextmenu(f))));
    tryp([name ' (toolbar)'], @() chain(fn(uitoolbar(uf))));
    tryp([name ' (classic toolbar)'], @() chain(fn(uitoolbar(f))));
    tryp([name ' (pushtool)'], @() chain(fn(uipushtool(uitoolbar(f)))));
    tryp([name ' (axes)'], @() chain(fn(ax)));
    tryp([name ' (uicontrol)'], @() chain(fn(uicontrol(f))));
    tryp([name ' (label)'], @() chain(fn(uilabel(uf))));
    tryp([name ' (5.5)'], @() chain(fn(5.5)));
    tryp([name ' ([])'], @() chain(fn([])));
    tryp([name ' (groot)'], @() chain(fn(groot)));
    tryp([name ' (Parent, uf)'], @() chain(fn('Parent', uf)));
    tryp([name ' (Parent, f)'], @() chain(fn('Parent', f)));
    tryp([name ' (Parent, [])'], @() chain(fn('Parent', [])));
    tryp([name ' (f, Bogus, 1)'], @() chain(fn(f, 'Bogus', 1)));
    tryp([name ' (uf, Bogus, 1)'], @() chain(fn(uf, 'Bogus', 1)));
    tryp([name ' (f, Tag)'], @() chain(fn(f, 'Tag')));
    tryp([name ' (Tag, t) pairs only'], @() get(fn('Tag', 't'), 'Tag'));
    tryp([name ' (f, struct)'], @() get(fn(f, struct('Tag', 's')), 'Tag'));
    tryp([name ' (f, tag, t) lower case'], @() get(fn(f, 'tag', 't'), 'Tag'));
    tryp([name ' (f, Ta, t) prefix'], @() get(fn(f, 'Ta', 't'), 'Tag'));
    tryp([name ' (uf, Ta, t) prefix'], @() get(fn(uf, 'Ta', 't'), 'Tag'));
    tryp([name ' (f, "Tag", "t") strings'], @() get(fn(f, "Tag", "t"), 'Tag'));
    tryp([name ' bogus get'], @() get(fn(f), 'Bogus'));
    tryp([name ' bogus set'], @() set(fn(f), 'Bogus', 1));
    tryp([name ' set Type'], @() set(fn(f), 'Type', 'x'));
    tryp([name ' isprop Units Position Visible Enable, isgraphics, ishghandle'], @() flags(fn(f)));
    tryp([name ' u isprop Units Position Visible Enable'], @() flags(fn(uf)));
    tryp([name ' Parent <- other figure'], @() reparent(fn(uf), f));
    tryp([name ' Parent <- []'], @() reparent(fn(uf), []));
    tryp([name ' Parent <- axes'], @() reparent(fn(f), ax));
    tryp([name ' Parent <- panel'], @() reparent(fn(uf), uipanel(uf)));
    tryp([name ' findobj by Type f'], @() numel(findobj(f, 'Type', get(fn(f), 'Type'))));
    tryp([name ' findobj by Type uf'], @() numel(findobj(uf, 'Type', get(fn(uf), 'Type'))));
    tryp([name ' findall by Type uf'], @() numel(findall(uf, 'Type', get(fn(uf), 'Type'))));
    tryp([name ' nargout 0'], @() nout(fn, f));
    delete(allchild(uf)); delete(allchild(f)); ax = axes(f);
end
delete(findall(groot, 'Type', 'figure'));
end

function s = noarg(fn)
h = fn();
s = chain(h);
end

function s = chain(h)
% The class of h and of each ancestor up to the root, with a figure's kind.
s = '';
while ~isempty(h) && ~isa(h, 'matlab.ui.Root')
    c = class(h);
    if isa(h, 'matlab.ui.Figure')
        if matlab.ui.internal.isUIFigure(h), c = 'uifigure'; else, c = 'figure'; end
    end
    if isempty(s), s = c; else, s = [s ' < ' c]; end %#ok<AGROW>
    h = h.Parent;
end
end

function s = flags(h)
s = sprintf('%d%d%d%d %d %d', isprop(h, 'Units'), isprop(h, 'Position'), isprop(h, 'Visible'), isprop(h, 'Enable'), isgraphics(h), ishghandle(h));
end

function s = reparent(h, p)
h.Parent = p;
s = chain(h);
end

function s = nout(fn, f)
clear ans
fn(f);
s = exist('ans', 'var');
end

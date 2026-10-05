function u8_parents
% U8 probe: what the older makers say when given one of the U8 objects as a parent. Headless.
uf = uifigure('Visible', 'off');
f = figure('Visible', 'off');
fns = {'uicontrol', 'uipanel', 'uibuttongroup', 'uibutton', 'uilabel', 'uigridlayout', 'uiaxes', 'axes', 'uitable'};
for k = 1:numel(fns)
    name = fns{k}; fn = str2func(name);
    if any(strcmp(name, {'uicontrol', 'axes'})), p = f; else, p = uf; end
    tryp([name ' (tabgroup)'], @() cls(fn(uitabgroup(p))));
    tryp([name ' (tab)'], @() cls(fn(uitab(uitabgroup(p)))));
    tryp([name ' (menu)'], @() cls(fn(uimenu(p))));
    tryp([name ' (contextmenu)'], @() cls(fn(uicontextmenu(p))));
    tryp([name ' (toolbar)'], @() cls(fn(uitoolbar(p))));
    tryp([name ' (pushtool)'], @() cls(fn(uipushtool(uitoolbar(p)))));
    tryp([name ' (table)'], @() cls(fn(uitable(p))));
    tryp([name ' Parent <- tabgroup'], @() rep(fn(p), uitabgroup(p)));
    tryp([name ' Parent <- tab'], @() rep(fn(p), uitab(uitabgroup(p))));
    tryp([name ' Parent <- menu'], @() rep(fn(p), uimenu(p)));
    tryp([name ' Parent <- table'], @() rep(fn(p), uitable(p)));
    tryp([name ' Parent <- toolbar'], @() rep(fn(p), uitoolbar(p)));
    delete(allchild(uf)); delete(allchild(f));
end
% the U8 objects moved about
tryp('tab Parent <- figure', @() rep(uitab(uitabgroup(uf)), uf));
tryp('tab Parent <- panel', @() rep(uitab(uitabgroup(uf)), uipanel(uf)));
tryp('tab Parent <- tab', @() rep(uitab(uitabgroup(uf)), uitab(uitabgroup(uf))));
tryp('tabgroup Parent <- its own tab', @() own(uf));
tryp('tabgroup Parent <- grid', @() rep(uitabgroup(uf), uigridlayout(uf)));
tryp('tabgroup Parent <- tabgroup', @() rep(uitabgroup(uf), uitabgroup(uf)));
tryp('table Parent <- tab', @() rep(uitable(uf), uitab(uitabgroup(uf))));
tryp('table Parent <- grid', @() rep(uitable(uf), uigridlayout(uf)));
tryp('menu Parent <- panel', @() rep(uimenu(uf), uipanel(uf)));
tryp('menu Parent <- toolbar', @() rep(uimenu(uf), uitoolbar(uf)));
tryp('menu Parent <- its own child', @() ownmenu(uf));
tryp('contextmenu Parent <- menu', @() rep(uicontextmenu(uf), uimenu(uf)));
tryp('toolbar Parent <- panel', @() rep(uitoolbar(uf), uipanel(uf)));
tryp('pushtool Parent <- figure', @() rep(uipushtool(uitoolbar(uf)), uf));
tryp('pushtool Parent <- menu', @() rep(uipushtool(uitoolbar(uf)), uimenu(uf)));
tryp('toggletool Parent <- figure', @() rep(uitoggletool(uitoolbar(uf)), uf));
% what delete and a few verbs answer
tb = uitoolbar(uf); pt = uipushtool(tb);
tryp('isgraphics tool', @() isgraphics(pt));
tryp('ancestor tool figure', @() class(ancestor(pt, 'figure')));
tryp('findobj uipushtool', @() numel(findobj(uf, 'Type', 'uipushtool')));
tryp('get(tool) fields n', @() numel(fieldnames(get(pt))));
tryp('tool Position', @() get(pt, 'Position'));
tryp('toolbar Position', @() get(tb, 'Position'));
m = uimenu(uf, 'Text', 'x');
tryp('menu Units', @() get(m, 'Units'));
tryp('menu Label listed', @() any(strcmp(fieldnames(get(m)), 'Label')));
tryp('menu isprop Label Callback', @() [isprop(m, 'Label') isprop(m, 'Callback')]);
tryp('copyobj menu', @() class(copyobj(m, uf)));
tg = uitabgroup(uf); t = uitab(tg, 'Title', 'A');
tryp('copyobj tab', @() get(copyobj(t, tg), 'Title'));
tryp('copyobj tabgroup children', @() numel(get(copyobj(tg, uf), 'Children')));
tbl = uitable(uf, 'Data', magic(3), 'ColumnName', {'a', 'b', 'c'});
tryp('copyobj table Data', @() mat2str(get(copyobj(tbl, uf), 'Data')));
tryp('uistack tabgroup', @() uistack(tg, 'top'));
tryp('reset(tbl) Data', @() rst(tbl));
tryp('set(tbl) names n', @() numel(fieldnames(set(tbl))));
tryp('get(tbl,{Data,Tag})', @() v2s(get(tbl, {'Data', 'Tag'})));
tryp('findobj uitable -property Data', @() numel(findobj(uf, '-property', 'Data')));
tryp('findobj Title A', @() numel(findobj(uf, 'Title', 'A')));
tryp('delete(tg) then isvalid(t)', @() del(tg, t));
delete(findall(groot, 'Type', 'figure'));
end

function s = cls(h)
s = class(h);
end

function s = rep(h, p)
h.Parent = p;
s = class(h.Parent);
end

function s = own(uf)
tg = uitabgroup(uf); t = uitab(tg);
tg.Parent = t;
s = class(tg.Parent);
end

function s = ownmenu(uf)
m = uimenu(uf); c = uimenu(m);
m.Parent = c;
s = class(m.Parent);
end

function s = rst(tbl)
reset(tbl);
s = mat2str(size(tbl.Data));
end

function s = del(tg, t)
delete(tg);
s = isvalid(t);
end

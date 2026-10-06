function u9b_forms
% U9b probe: uihtml's maker forms and parents, and the HTMLSource forms it takes or refuses.
% Headless.
here = fileparts(mfilename('fullpath'));
addpath(here);
site = fullfile(here, 'site');
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
name = 'uihtml'; fn = @uihtml;
before = findall(groot, 'Type', 'figure');
tryp([name ' ()'], @() chain(fn()));
delete(setdiff(findall(groot, 'Type', 'figure'), before));
figure(f); set(f, 'Visible', 'off');
tryp([name ' () with a current figure'], @() chain(fn()));
delete(setdiff(findall(groot, 'Type', 'figure'), before));
tryp([name ' (uifigure)'], @() chain(fn(uf)));
tryp([name ' (figure)'], @() chain(fn(f)));
tryp([name ' (uipanel)'], @() chain(fn(uipanel(uf))));
tryp([name ' (classic uipanel)'], @() chain(fn(uipanel(f))));
tryp([name ' (grid)'], @() chain(fn(uigridlayout(uf))));
tryp([name ' (uibuttongroup)'], @() chain(fn(uibuttongroup(uf))));
tryp([name ' (tabgroup)'], @() chain(fn(uitabgroup(uf))));
tryp([name ' (tab)'], @() chain(fn(uitab(uitabgroup(uf)))));
tryp([name ' (menu)'], @() chain(fn(uimenu(uf))));
tryp([name ' (axes)'], @() chain(fn(ax)));
tryp([name ' (uiaxes)'], @() chain(fn(uiaxes(uf))));
tryp([name ' (uicontrol)'], @() chain(fn(uicontrol(f))));
tryp([name ' (label)'], @() chain(fn(uilabel(uf))));
tryp([name ' (tree)'], @() chain(fn(uitree(uf))));
tryp([name ' (5.5)'], @() chain(fn(5.5)));
tryp([name ' ([])'], @() chain(fn([])));
tryp([name ' (groot)'], @() chain(fn(groot)));
tryp([name ' (Parent, uf)'], @() chain(fn('Parent', uf)));
tryp([name ' (Parent, [])'], @() chain(fn('Parent', [])));
tryp([name ' (uf, Bogus, 1)'], @() chain(fn(uf, 'Bogus', 1)));
tryp([name ' (uf, Tag)'], @() chain(fn(uf, 'Tag')));
tryp([name ' (uf, 5, 6)'], @() chain(fn(uf, 5, 6)));
tryp([name ' (Tag, t) pairs only'], @() get(fn('Tag', 't'), 'Tag'));
delete(setdiff(findall(groot, 'Type', 'figure'), before));
tryp([name ' (uf, struct)'], @() get(fn(uf, struct('Tag', 's')), 'Tag'));
tryp([name ' (uf, tag, t) lower case'], @() get(fn(uf, 'tag', 't'), 'Tag'));
tryp([name ' (uf, Ta, t) prefix'], @() get(fn(uf, 'Ta', 't'), 'Tag'));
tryp([name ' (uf, htmls, x) prefix'], @() get(fn(uf, 'htmls', '<b>x</b>'), 'HTMLSource'));
tryp([name ' (uf, "Tag", "t") strings'], @() get(fn(uf, "Tag", "t"), 'Tag'));
tryp([name ' (uf, HTMLSource, missing.html)'], @() get(fn(uf, 'HTMLSource', 'missing.html'), 'HTMLSource'));
tryp([name ' (uf, Data, 5, DataChangedFcn, @disp)'], @() get(fn(uf, 'Data', 5, 'DataChangedFcn', @disp), 'Data'));
tryp([name ' Units'], @() get(fn(uf), 'Units'));
tryp([name ' bogus get'], @() get(fn(uf), 'Bogus'));
tryp([name ' bogus set'], @() set(fn(uf), 'Bogus', 1));
tryp([name ' dot bogus get'], @() dotget(fn(uf)));
tryp([name ' dot bogus set'], @() dotset(fn(uf)));
tryp([name ' set Type'], @() set(fn(uf), 'Type', 'x'));
tryp([name ' isprop Units, isgraphics, ishghandle, isvalid, ishandle, isa Component'], @() flags(fn(uf)));
tryp([name ' Parent <- figure'], @() reparent(fn(uf), f));
tryp([name ' Parent <- []'], @() reparent(fn(uf), []));
tryp([name ' Parent <- panel'], @() reparent(fn(uf), uipanel(uf)));
tryp([name ' Parent <- grid'], @() reparent(fn(uf), uigridlayout(uf)));
tryp([name ' in panel default Position'], @() get(fn(uipanel(uf)), 'Position'));
tryp([name ' in grid Position'], @() gridpos(uf));
tryp([name ' findobj by Type'], @() numel(findobj(uf, 'Type', 'uihtml')));
tryp([name ' focus'], @() dofocus(fn(uf)));
tryp([name ' copyobj'], @() class(copyobj(fn(uf, 'Data', 3, 'HTMLSource', '<b>c</b>'), uf)));
tryp([name ' copyobj keeps'], @() copied(fn(uf, 'Data', 3, 'HTMLSource', '<b>c</b>'), uf));
delete(allchild(uf));

%% HTMLSource forms, from the site folder and from elsewhere
cd(site);
forms = {'page.html', fullfile(site, 'page.html'), './page.html', 'types/sub/index.html', 'types\sub\index.html', ...
    'PAGE.HTML', 'page', 'page.htm', 'types/a.htm', 'types/a.txt', 'types/a.js', 'types/a.xhtml', 'types/noext', 'types', site, ...
    '<p>hi</p>', 'hello world', 'missing.html', 'missing.htm', 'missing.txt', 'missing', 'sub/missing.html', ...
    'https://www.mathworks.com', 'http://localhost/x.html', 'file:///C:/x.html', 'www.mathworks.com', 'ftp://x', ...
    'mailto:a@b.c', 'data:text/html,<b>x</b>', 'javascript:alert(1)', ['file:///' strrep(fullfile(site, 'page.html'), '\', '/')], ...
    '  page.html', 'page.html  ', "<b>string</b>", "page.html", ['ab'; 'cd'], {'page.html'}, 5, [], '', "", string(missing), ...
    ["a" "b"], '<', 'a.html b', 'x.html<', '..\u9b_forms.m', 'echo.html', 'C:\nowhere\x.html', '\\server\share\x.html'};
for k = 1:numel(forms)
    v = forms{k};
    h = uihtml(uf);
    lastwarn('');
    try
        h.HTMLSource = v;
        r = v2s(h.HTMLSource);
    catch e
        r = sprintf('ERR %s | %s', e.identifier, oneline(e.message));
    end
    [wm, wid] = lastwarn;
    if ~isempty(wm), r = sprintf('%s  WARN %s | %s', r, wid, oneline(wm)); end
    label = v2s(v);
    label = strrep(label, here, '<u9b>');
    r = strrep(r, here, '<u9b>');
    fprintf('HTMLSource <- %s : %s\n', label, r);
    delete(h);
end
% a file name found on the path, not in the current folder
cd(here);
addpath(fullfile(site, 'types', 'sub'));
tryp('HTMLSource <- index.html on the path', @() strrep(get(uihtml(uf, 'HTMLSource', 'index.html'), 'HTMLSource'), here, '<u9b>'));
tryp('HTMLSource <- deep.js on the path', @() strrep(get(uihtml(uf, 'HTMLSource', 'deep.js'), 'HTMLSource'), here, '<u9b>'));
rmpath(fullfile(site, 'types', 'sub'));
delete(uf); delete(f);
end

function r = chain(h)
r = class(h);
p = h;
while isprop(p, 'Parent') && ~isempty(p.Parent)
    p = p.Parent;
    r = [r ' < ' class(p)];
end
end
function r = dotget(h)
r = h.Bogus;
end
function r = dotset(h)
h.Bogus = 1;
r = 'set';
end
function r = flags(h)
r = [isprop(h, 'Units') isgraphics(h) ishghandle(h) isvalid(h) ishandle(h) isa(h, 'matlab.ui.control.Component') isa(h, 'matlab.graphics.Graphics') isa(h, 'matlab.ui.control.HTML')];
end
function r = reparent(h, p)
set(h, 'Parent', p);
r = class(get(h, 'Parent'));
end
function r = gridpos(uf)
g = uigridlayout(uf, [2 2]);
h = uihtml(g);
drawnow; pause(1);
r = h.Position;
end
function r = dofocus(h)
focus(h);
r = 'focused';
end
function r = copied(h, p)
c = copyobj(h, p);
r = sprintf('Data=%s HTMLSource=%s', v2s(c.Data), v2s(c.HTMLSource));
end

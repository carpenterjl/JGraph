% record: -noFigureWindows
% U9b of the app-building plan (ADR 0208): the forms uihtml takes - no parent, a parent of each
% kind, name-value pairs, a struct - and its refusals; the HTMLSource forms it takes as a file, as
% markup or refuses; sendEventToHTMLSource's argument forms and refusals; copyobj and a new parent.
% Probes u9b_forms and u9b_bridge.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
u9b_chk('in_uifigure', @() u8_chain(uihtml(uf)));
u9b_chk('in_figure', @() u8_chain(uihtml(f)));
u9b_chk('in_uipanel', @() u8_chain(uihtml(uipanel(uf))));
u9b_chk('in_classic_uipanel', @() u8_chain(uihtml(uipanel(f))));
u9b_chk('in_grid', @() u8_chain(uihtml(uigridlayout(uf))));
u9b_chk('in_group', @() u8_chain(uihtml(uibuttongroup(uf))));
u9b_chk('in_tabgroup', @() u8_chain(uihtml(uitabgroup(uf))));
u9b_chk('in_tab', @() u8_chain(uihtml(uitab(uitabgroup(uf)))));
u9b_chk('in_menu', @() u8_chain(uihtml(uimenu(uf))));
u9b_chk('in_axes', @() u8_chain(uihtml(ax)));
u9b_chk('in_uiaxes', @() u8_chain(uihtml(uiaxes(uf))));
u9b_chk('in_uicontrol', @() u8_chain(uihtml(uicontrol(f))));
u9b_chk('in_label', @() u8_chain(uihtml(uilabel(uf))));
u9b_chk('in_tree', @() u8_chain(uihtml(uitree(uf))));
u9b_chk('in_number', @() u8_chain(uihtml(5.5)));
u9b_chk('in_empty', @() u8_chain(uihtml([])));
u9b_chk('in_root', @() u8_chain(uihtml(groot)));
u9b_chk('named_parent', @() u8_chain(uihtml('Parent', uf)));
u9b_chk('unknown', @() u8_chain(uihtml(uf, 'Bogus', 1)));
u9b_chk('odd', @() u8_chain(uihtml(uf, 'Tag')));
u9b_chk('numbers', @() u8_chain(uihtml(uf, 5, 6)));
u9b_chk('struct', @() get(uihtml(uf, struct('Tag', 's')), 'Tag'));
u9b_chk('lower_name', @() get(uihtml(uf, 'tag', 't'), 'Tag'));
u9b_chk('prefix_name', @() get(uihtml(uf, 'Ta', 't'), 'Tag'));
u9b_chk('prefix_source', @() get(uihtml(uf, 'htmls', '<b>x</b>'), 'HTMLSource'));
u9b_chk('string_names', @() get(uihtml(uf, "Tag", "t"), 'Tag'));
u9b_chk('missing_source', @() get(uihtml(uf, 'HTMLSource', 'missing.html'), 'HTMLSource'));
u9b_chk('url_source', @() get(uihtml(uf, 'HTMLSource', 'https://www.mathworks.com'), 'HTMLSource'));
u9b_chk('data_pair', @() get(uihtml(uf, 'Data', 5, 'DataChangedFcn', @disp), 'Data'));
u9b_chk('flags', @() double([isprop(uihtml(uf), 'Units') isgraphics(uihtml(uf)) isa(uihtml(uf), 'matlab.ui.control.Component') ...
    isa(uihtml(uf), 'matlab.graphics.Graphics') isa(uihtml(uf), 'matlab.ui.control.HTML') isa(uihtml(uf), 'handle')]));
u9b_chk('parent_figure', @() reparent(uihtml(uf), f));
% A component with no parent cannot be kept here (ADR 0202, open item 48).
u9b_chkdiv('parent_none', @() unparent(uihtml(uf)), 'ADR0202');
u9b_chk('parent_panel', @() reparent(uihtml(uf), uipanel(uf)));
u9b_chk('parent_axes', @() reparent(uihtml(uf), ax));
u9b_chk('in_panel_position', @() get(uihtml(uipanel(uf)), 'Position'));
u9b_chk('findobj', @() numel(findobj(uf, 'Type', 'uihtml')) > 0);
u9b_chk('copyobj', @() copied(uihtml(uf, 'Data', struct('a', 3), 'HTMLSource', '<b>c</b>', 'Tag', 'c'), uf));
u9b_chk('copyobj_data_is_a_copy', @() copiedcopy(uf));
delete(allchild(uf));
delete(allchild(f));

% HTMLSource forms, from the fixtures folder (the current one, helpers on the path)
here = pwd;
forms = {'u9b_json.m', fullfile(here, 'u9b_json.m'), './u9b_json.m', 'helpers/u9b_text.m', 'helpers\u9b_text.m', 'U9B_JSON.M', ...
    'u9b_echo.html', 'helpers/u9b_echo.html', 'u9b_text.m', 'helpers', 'u9b_json', 'missing.html', 'missing.htm', 'MISSING.HTML', ...
    'helpers/missing.html', 'missing.txt', 'missing', '<p>hi</p>', 'hello world', 'https://x.org/a.html', 'http://localhost/x.html', ...
    'HTTPS://X.ORG', 'www.x.org', 'ftp://x', 'mailto:a@b.c', 'data:text/html,<b>x</b>', 'javascript:alert(1)', 'file:///C:/nowhere/x.html', ...
    ['file:///' strrep(fullfile(here, 'u9b_json.m'), '\', '/')], '  missing.html', 'missing.html  ', '<', 'a.html b', 'x.html<', ...
    'C:\nowhere\x.html', '\\server\share\x.html', '..\fixtures\u9b_json.m'};
for k = 1:numel(forms)
    h = uihtml(uf);
    lastwarn('');
    try
        h.HTMLSource = forms{k};
        r = u9b_text(strrep(strrep(h.HTMLSource, here, '<here>'), strrep(here, '\', '/'), '<here>'));
    catch err
        r = [err.identifier ' :: ' strrep(regexprep(strtrim(err.message), '\s+', ' '), '|', '/')];
    end
    fprintf('CHK|source_%02d|%s|exact\n', k, r);
    delete(h);
end

% sendEventToHTMLSource: what it takes and refuses (nothing here waits for a page)
h = uihtml(uf);
h2 = uihtml(uf);
sends = {@() sendEventToHTMLSource(h), @() sendEventToHTMLSource(h, 'n'), @() sendEventToHTMLSource(h, 'n', 1, 2), ...
    @() sendEventToHTMLSource(5, 'n'), @() sendEventToHTMLSource('n'), @() sendEventToHTMLSource(h, 5), @() sendEventToHTMLSource(h, ''), ...
    @() sendEventToHTMLSource(h, "n"), @() sendEventToHTMLSource(h, {'n'}), @() sendEventToHTMLSource(h, 'a b'), ...
    @() sendEventToHTMLSource(h, 'DataChanged'), @() sendEventToHTMLSource(h, ['ab'; 'cd']), @() sendEventToHTMLSource(h, ["a" "b"]), ...
    @() sendEventToHTMLSource(h, string(missing)), @() sendEventToHTMLSource(h, 'n', @sin), @() sendEventToHTMLSource([h h2], 'n'), ...
    @() sendEventToHTMLSource(uf, 'n'), @() sendEventToHTMLSource(uilabel(uf), 'n'), @() sendEventToHTMLSource(h, 'n', 'x', 'y'), ...
    @() sendEventToHTMLSource(), @() sendEventToHTMLSource(h, 1:3), @() sendEventToHTMLSource(h, true), @() sendEventToHTMLSource(h, {1}), ...
    @() sendEventToHTMLSource(h, 'HTMLEventReceived'), @() sendEventToHTMLSource(struct(), 'n'), @() sendEventToHTMLSource({h}, 'n')};
for k = 1:numel(sends)
    u9b_chk(sprintf('send_%02d', k), @() sent(sends{k}));
end
u9b_chk('send_output', @() sendout(h));
hd = uihtml(uf);
delete(hd);
% A deleted handle is a number here (ADR 0051), so it is refused as a number is.
u9b_chkdiv('send_deleted', @() sent(@() sendEventToHTMLSource(hd, 'n')), 'ADR0208');
delete(uf);
delete(f);

function r = sent(fn)
fn();
r = 'ok';
end

function r = sendout(h)
x = sendEventToHTMLSource(h, 'n');
r = x;
end

function r = reparent(h, p)
set(h, 'Parent', p);
q = get(h, 'Parent');
if isempty(q)
    r = 'none';
else
    r = get(q, 'Type');
end
end

function r = copied(h, p)
c = copyobj(h, p);
r = {c.Data, c.HTMLSource, c.Tag, c.Type, isequal(c, h)};
end

function r = copiedcopy(p)
h = uihtml(p, 'Data', 1);
c = copyobj(h, p);
c.Data = 2;
r = [h.Data c.Data];
end

function r = unparent(h)
try
    r = reparent(h, []);
catch err
    r = ['refused: ' err.message];
end
end

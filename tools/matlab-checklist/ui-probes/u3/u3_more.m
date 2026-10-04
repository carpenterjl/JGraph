function u3_more
% U3 probe: follow-ups - which buttons a group manages and what it writes into them, the hidden
% uicontrol names, a frame's and a slider's reading of Value. Headless: run-probe.ps1 -Name u3_more.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);

%% creation into a group whose selection was cleared
bg = uibuttongroup(f, 'Tag', 'bg');
r1 = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'r1');
r2 = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'r2');
bg.SelectedObject = [];
r3 = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'r3');
fprintf('made in a group with buttons and no selection: r3=%s selected=%s\n', mat2str(r3.Value), tagOf(bg.SelectedObject));
bg.SelectedObject = [];
r4 = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'r4', 'Value', 5);
fprintf('made with Value 5, no selection: r4=%s selected=%s\n', mat2str(r4.Value), tagOf(bg.SelectedObject));
r5 = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'r5', 'Value', 5);
fprintf('made with Value 5, with a selection: r5=%s selected=%s r4=%s\n', mat2str(r5.Value), tagOf(bg.SelectedObject), mat2str(r4.Value));
delete(bg);

%% Min and Max in what the group writes
bg = uibuttongroup(f, 'Tag', 'bg');
a = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'a', 'Min', 2, 'Max', 7);
fprintf('first, Min 2 Max 7: a=%s selected=%s\n', mat2str(a.Value), tagOf(bg.SelectedObject));
b = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'b', 'Min', 3, 'Max', 9);
fprintf('second, Min 3 Max 9: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
bg.SelectedObject = b; fprintf('SelectedObject=b: a=%s b=%s\n', mat2str(a.Value), mat2str(b.Value));
bg.SelectedObject = a; fprintf('SelectedObject=a: a=%s b=%s\n', mat2str(a.Value), mat2str(b.Value));
bg.SelectedObject = []; fprintf('SelectedObject=[]: a=%s b=%s\n', mat2str(a.Value), mat2str(b.Value));
b.Value = 9; fprintf('b.Value=9 (its Max): a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
b.Value = 1; fprintf('b.Value=1: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
a.Value = 1; fprintf('a.Value=1: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
a.Value = 0.5; fprintf('selected a.Value=0.5: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
a.Value = 1; a.Value = 7; fprintf('selected a.Value=7: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
a.Value = 1; a.Value = [1 1]; fprintf('selected a.Value=[1 1]: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
a.Value = 1; a.Value = 2; fprintf('selected a.Value=2 (its Min): a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
a.Value = 1; a.Value = 1; fprintf('selected a.Value=1 again: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
b.Value = [1 2]; fprintf('b.Value=[1 2]: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
b.Value = true; fprintf('b.Value=true: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
delete(bg);

%% a managed button that changes style, and an unmanaged one named as SelectedObject
bg = uibuttongroup(f, 'Tag', 'bg');
a = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'a');
b = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'b');
a.Style = 'checkbox'; fprintf('selected a becomes a checkbox: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
b.Value = 1; fprintf('b.Value=1: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
tryp('checkbox a.Value=1', @() setget(a, 'Value', 1)); fprintf('  a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
a.Style = 'radiobutton'; a.Value = 0; fprintf('a a radio again, Value=0: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject)); a.Value = 1; fprintf('a a radio again, Value=1: a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
p = uicontrol(bg, 'Tag', 'p'); p.Style = 'radiobutton';
tryp('SelectedObject<-late radio', @() tagOf(setget(bg, 'SelectedObject', p)));
fprintf('  a=%s b=%s p=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), mat2str(p.Value), tagOf(bg.SelectedObject));
b.Value = 1; fprintf('b.Value=1 after: a=%s b=%s p=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), mat2str(p.Value), tagOf(bg.SelectedObject));
t = uicontrol(bg, 'Style', 'togglebutton', 'Tag', 't');
t.Value = 1; fprintf('toggle t.Value=1: a=%s b=%s t=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), mat2str(t.Value), tagOf(bg.SelectedObject));
g2 = uibuttongroup(f, 'Tag', 'g2');
q1 = uicontrol(f, 'Style', 'radiobutton', 'Tag', 'q1'); q1.Parent = g2;
fprintf('first radio moved into an empty group: q1=%s selected=%s\n', mat2str(q1.Value), tagOf(g2.SelectedObject));
q2 = uicontrol(g2, 'Style', 'radiobutton', 'Tag', 'q2');
fprintf('then one made in it: q1=%s q2=%s selected=%s\n', mat2str(q1.Value), mat2str(q2.Value), tagOf(g2.SelectedObject));
q2.Value = 1; fprintf('q2.Value=1: q1=%s q2=%s selected=%s\n', mat2str(q1.Value), mat2str(q2.Value), tagOf(g2.SelectedObject));
q1.Value = 1; fprintf('q1.Value=1: q1=%s q2=%s selected=%s\n', mat2str(q1.Value), mat2str(q2.Value), tagOf(g2.SelectedObject));
q1.Value = 0.5; fprintf('selected q1.Value=0.5: q1=%s q2=%s selected=%s\n', mat2str(q1.Value), mat2str(q2.Value), tagOf(g2.SelectedObject));
q1.Value = 1; q1.Value = [1 1]; fprintf('selected q1.Value=[1 1]: q1=%s q2=%s selected=%s\n', mat2str(q1.Value), mat2str(q2.Value), tagOf(g2.SelectedObject));
q2.Value = [1 1]; fprintf('q2.Value=[1 1]: q1=%s q2=%s selected=%s\n', mat2str(q1.Value), mat2str(q2.Value), tagOf(g2.SelectedObject));
q1.Value = 1; q2.Value = 3; q3 = uicontrol(g2, 'Style', 'radiobutton', 'Tag', 'q3', 'Value', 1);
fprintf('q2 at 3, q3 made with Value 1: q1=%s q2=%s q3=%s selected=%s\n', mat2str(q1.Value), mat2str(q2.Value), mat2str(q3.Value), tagOf(g2.SelectedObject));
q1.Max = 5; q1.Value = 5; fprintf('q1 Max 5 Value 5: q1=%s q3=%s selected=%s\n', mat2str(q1.Value), mat2str(q3.Value), tagOf(g2.SelectedObject));
q1.Value = 1; fprintf('q1 Max 5 Value 1: q1=%s q3=%s selected=%s\n', mat2str(q1.Value), mat2str(q3.Value), tagOf(g2.SelectedObject));
g3 = uibuttongroup(f, 'Tag', 'g3');
m1 = uicontrol(g3, 'Style', 'radiobutton', 'Tag', 'm1', 'Max', 7);
fprintf('first made with Max 7: m1=%s selected=%s\n', mat2str(m1.Value), tagOf(g3.SelectedObject));
m2 = uicontrol(g3, 'Style', 'radiobutton', 'Tag', 'm2');
fprintf('second, plain: m1=%s m2=%s selected=%s\n', mat2str(m1.Value), mat2str(m2.Value), tagOf(g3.SelectedObject));
m2.Value = 1; fprintf('m2.Value=1: m1=%s m2=%s selected=%s\n', mat2str(m1.Value), mat2str(m2.Value), tagOf(g3.SelectedObject));
g4 = uibuttongroup(f, 'Tag', 'g4');
n1 = uicontrol(g4, 'Style', 'radiobutton', 'Tag', 'n1', 'Min', -1);
fprintf('first made with Min -1: n1=%s selected=%s\n', mat2str(n1.Value), tagOf(g4.SelectedObject));
n2 = uicontrol(g4, 'Style', 'radiobutton', 'Tag', 'n2'); n2.Value = 1;
fprintf('second plain, Value=1: n1=%s n2=%s selected=%s\n', mat2str(n1.Value), mat2str(n2.Value), tagOf(g4.SelectedObject));
delete(g2); delete(g3); delete(g4);
c2 = copyobj(b, bg); c2.Tag = 'c2';
fprintf('copyobj(b,bg): b=%s c2=%s selected=%s\n', mat2str(b.Value), mat2str(c2.Value), tagOf(bg.SelectedObject));
bgc = copyobj(bg, f);
fprintf('copyobj(bg,f): selected=%s children=%d\n', tagOf(bgc.SelectedObject), numel(bgc.Children));
delete(bg); delete(bgc);

%% set through a struct and through several pairs
bg = uibuttongroup(f, 'Tag', 'bg');
a = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'a');
b = uicontrol(bg, 'Style', 'radiobutton', 'Tag', 'b');
set([a b], 'Value', 1); fprintf('set([a b],Value,1): a=%s b=%s selected=%s\n', mat2str(a.Value), mat2str(b.Value), tagOf(bg.SelectedObject));
set(bg, 'SelectedObject', a, 'Title', 'x'); fprintf('pairs: selected=%s\n', tagOf(bg.SelectedObject));
tryp('get(bg,''SelectedObject'')==a', @() get(bg, 'SelectedObject') == a);
tryp('findobj(bg,''Value'',1)', @() tagOf(findobj(bg, 'Value', 1)));
tryp('SelectedObject<-deleted', @() deletedSel(bg, f));
delete(bg);

%% hidden names on uicontrol
c = uicontrol(f);
for h = {'Selected', 'SelectionHighlight', 'HitTest'}
    for v = {'on', 'off', true, 0, 'bogus', 5}
        tryp(sprintf('%s<-%s', h{1}, v2s(v{1})), @() setget(c, h{1}, v{1}));
    end
end
tryp('TooltipStr', @() setget(c, 'TooltipStr', 'x'));
tryp('Tooltip after TooltipStr', @() c.Tooltip);
tryp('get(c) has Selected', @() isfield(get(c), 'Selected'));
tryp('findobj Selected off', @() numel(findobj(f, 'Selected', 'off')));
tryp('set(c,''Enable'')', @() set(c, 'Enable'));
tryp('set(c,''Style'')', @() set(c, 'Style'));
tryp('Enable<-INACTIVE', @() setget(c, 'Enable', 'INACTIVE'));
tryp('Enable<-ina', @() setget(c, 'Enable', 'ina'));
tryp('Enable<-true', @() setget(c, 'Enable', true));
tryp('Enable<-0', @() setget(c, 'Enable', 0));
tryp('Enable<-o', @() setget(c, 'Enable', 'o'));
tryp('ButtonDownFcn<-char', @() setget(c, 'ButtonDownFcn', 'disp(1)'));
tryp('ButtonDownFcn<-5', @() setget(c, 'ButtonDownFcn', 5));
tryp('Callback<-cell', @() setget(c, 'Callback', {@disp, 1}));
tryp('HorizontalAlignment<-r', @() setget(c, 'HorizontalAlignment', 'r'));
tryp('reset(c)', @() resetIt(c));
delete(c);

%% frame
fr = uicontrol(f, 'Style', 'frame', 'Position', [10 10 100 100]);
inside = uicontrol(f, 'Style', 'text', 'String', 'in', 'Position', [20 20 50 20]);
fprintf('frame: Children=%d ; text Parent is figure %d ; frame String=%s BackgroundColor=%s ForegroundColor=%s Enable=%s\n', numel(fr.Children), inside.Parent == f, v2s(fr.String), mat2str(fr.BackgroundColor, 4), mat2str(fr.ForegroundColor, 4), fr.Enable);
tryp('uicontrol(frame)', @() uicontrol(fr));
tryp('uicontrol(''Parent'',frame)', @() uicontrol('Parent', fr));
delete(fr); delete(inside);

%% focus form, with every style
for s = {'pushbutton', 'edit', 'text', 'frame', 'slider', 'listbox'}
    c = uicontrol(f, 'Style', s{1});
    lastwarn('');
    tryp(sprintf('uicontrol(h) focus on %s', s{1}), @() focusIt(c));
    if ~isempty(lastwarn), fprintf('   warn %s\n', lastwarn); end
    delete(c);
end

%% nargout / nargin
for n = {'uicontrol', 'uibuttongroup', 'uipanel', 'textwrap', 'listfonts'}
    tryp(['nargin ' n{1}], @() nargin(n{1}));
    tryp(['nargout ' n{1}], @() nargout(n{1}));
end
delete(f);
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function s = resetIt(c)
c.String = 'x'; c.Value = 3; c.Style = 'checkbox'; c.Position = [1 2 3 4];
reset(c);
s = sprintf('Style=%s String=%s Value=%s Position=%s', c.Style, v2s(c.String), mat2str(c.Value), mat2str(c.Position));
end

function s = focusIt(c)
uicontrol(c);
s = 'ran';
end

function s = deletedSel(bg, f)
x = uicontrol(f, 'Style', 'radiobutton'); delete(x);
bg.SelectedObject = x;
s = tagOf(bg.SelectedObject);
end

function s = tagOf(h)
if isempty(h), s = '(none)'; elseif ~isscalar(h), s = sprintf('(%d handles)', numel(h)); else, s = h.Tag; end
end

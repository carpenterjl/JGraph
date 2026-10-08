function u10_matrix
% U10 probe: matlab.ui.componentcontainer.ComponentContainer, measured headless in R2025b.
here = fileparts(mfilename('fullpath'));
addpath(here);
addpath(fullfile(here, '..', 'research', 'apps', 'examples'));
f = uifigure('Visible', 'off');
u10log();

% --- construction order
c = u10Probe(f, 'Value', 5);
say('ctor log', u10log());
say('counts after ctor', sprintf('%d %d', c.SetupCalls, c.UpdateCalls));
drawnow;
say('log after drawnow', u10log());
drawnow;
say('log after 2nd drawnow', u10log());
c.Value = 6; c.Value = 7;
say('log after two sets', u10log());
drawnow;
say('log after drawnow', u10log());
c.Tag = 'tg'; drawnow;
say('log after Tag+drawnow', u10log());
c.Position = [20 20 150 40]; drawnow;
say('log after Position+drawnow', u10log());
c.Visible = 'off'; drawnow;
say('log after Visible+drawnow', u10log());
c.Visible = 'on'; drawnow; u10log();
c.poke(); drawnow;
say('log after private write+drawnow', u10log());
c.Value = 7; drawnow;
say('log after same-value write+drawnow', u10log());
c.Label = 'x'; pause(0.05);
say('log after write+pause', u10log());
c.Label = 'y'; figure(f);
say('log after write+figure(f)', u10log());
drawnow;
u10log();
c.ValueChangedFcn = @(s, e) u10log('cb');
drawnow;
say('log after setting the callback property + drawnow', u10log());

% --- the object
say('class', class(c));
say('Type', c.Type);
say('isa CC', isa(c, 'matlab.ui.componentcontainer.ComponentContainer'));
say('isa handle', isa(c, 'handle'));
say('isa Graphics', isa(c, 'matlab.graphics.Graphics'));
say('isa ui.control.Component', isa(c, 'matlab.ui.control.Component'));
say('isobject', isobject(c));
say('ishghandle', ishghandle(c));
say('isgraphics', isgraphics(c));
say('ishandle', ishandle(c));
say('isvalid', isvalid(c));
say('isstruct', isstruct(c));
say('Position', mat2str(c.Position));
say('Units', c.Units);
say('BackgroundColor', mat2str(c.BackgroundColor, 4));
say('Visible', class(c.Visible), char(c.Visible));
say('HandleVisibility', c.HandleVisibility);
say('Parent', class(c.Parent), num2str(c.Parent == f));
say('Children', class(c.Children), mat2str(size(c.Children)));
say('allchild', class(allchild(c)), mat2str(size(allchild(c))));
say('grid HandleVisibility', c.grid().HandleVisibility);
say('findall grid', mat2str(size(findall(c, 'Type', 'uigridlayout'))));
say('grid parent is c', num2str(c.grid().Parent == c));
say('grid parent class', class(c.grid().Parent));
say('f.Children', class(f.Children), mat2str(size(f.Children)));
say('f.Children(1)==c', num2str(f.Children(1) == c));
say('Layout', class(c.Layout));
say('ValueChangedFcn class', class(c.ValueChangedFcn));
c2 = u10Probe(f);
say('fresh ValueChangedFcn', class(c2.ValueChangedFcn), mat2str(size(c2.ValueChangedFcn)));
say('fresh ClickedFcn', class(c2.ClickedFcn));
say('isprop ValueChangedFcn', isprop(c2, 'ValueChangedFcn'));
say('isprop PlainFcn', isprop(c2, 'PlainFcn'));
say('isprop Value', isprop(c2, 'Value'));
say('isprop Position', isprop(c2, 'Position'));
say('properties', strjoin(properties(c2)', ','));
mc = metaclass(c2);
say('meta props', strjoin(sort({mc.PropertyList(~[mc.PropertyList.Hidden]).Name}), ','));
p = findobj(mc.PropertyList, 'Name', 'ValueChangedFcn');
say('meta ValueChangedFcn', sprintf('Set=%s Get=%s Dep=%d Def=%s', p.SetAccess, p.GetAccess, p.Dependent, p.DefiningClass.Name));
ev = findobj(mc.EventList, 'Name', 'ValueChanged');
say('meta event', ev.DefiningClass.Name);
say('get Value', get(c2, 'Value'));
set(c2, 'Value', 9);
say('set Value', c2.Value);
try, set(c2, 'Bogus', 1); say('set Bogus', 'ok'); catch e, say('set Bogus', e.identifier, e.message); end
try, x = c2.Bogus; say('read Bogus', 'ok'); catch e, say('read Bogus', e.identifier, e.message); end
try, c2.Bogus = 1; say('write Bogus', 'ok'); catch e, say('write Bogus', e.identifier, e.message); end
try, c2.Type = 'x'; say('write Type', 'ok'); catch e, say('write Type', e.identifier, e.message); end
try, c2.Children = []; say('write Children', 'ok'); catch e, say('write Children', e.identifier, e.message); end
try, c2.Value = 'abc'; say('write Value char', 'ok'); catch e, say('write Value char', e.identifier, e.message); end
try, c2.ValueChangedFcn = 5; say('cb = 5', 'ok'); catch e, say('cb = 5', e.identifier, e.message); end
try, c2.ValueChangedFcn = 'disp(''chr cb'')'; say('cb = char', class(c2.ValueChangedFcn)); catch e, say('cb = char', e.identifier, e.message); end
try, c2.ValueChangedFcn = {@(s, e, a) u10log(['cell cb ' a]), 'A'}; say('cb = cell', class(c2.ValueChangedFcn)); catch e, say('cb = cell', e.identifier, e.message); end
u10log();
c2.fire('ValueChanged');
say('cell cb fired', u10log());
c2.ValueChangedFcn = '';
c2.fire('ValueChanged');
say('empty cb fired', u10log());
try, c2.ValueChangedFcn = []; say('cb = []', class(c2.ValueChangedFcn), mat2str(size(c2.ValueChangedFcn))); catch e, say('cb = []', e.identifier, e.message); end
try, s = get(c2); say('get(c) fields', strjoin(fieldnames(s)', ',')); catch e, say('get(c)', e.identifier, e.message); end

% --- events
u10log();
addlistener(c, 'ValueChanged', @(s, e) u10log(sprintf('listener %s %s %d', class(e), e.EventName, e.Source == c)));
c.ValueChangedFcn = @(s, e) u10log(sprintf('callback %s %s %d %d', class(e), e.EventName, s == c, e.Source == c));
c.fire('ValueChanged');
say('fire order', u10log());
c.ValueChangedFcn = @(s, e) error('u10:cb', 'callback failed');
try
    c.fire('ValueChanged');
    say('cb error', 'no error', u10log());
catch e
    say('cb error', e.identifier, e.message);
end
c.ValueChangedFcn = @(s, e) u10log('one-arg?');
c.ValueChangedFcn = @() u10log('no args');
try, c.fire('ValueChanged'); say('no-arg cb', u10log()); catch e, say('no-arg cb', e.identifier, e.message); end
c3 = u10Probe(f, 'ClickedFcn', @(s, e) u10log('clicked from ctor'));
u10log();
c3.fire('Clicked');
say('ctor callback', u10log());

% --- forms
fn = @() u10Probe('Parent', f, 'Value', 3);
try, d = fn(); say('Parent NV', class(d.Parent), u10log()); catch e, say('Parent NV', e.identifier, e.message); end
try, d = u10Probe(f, 3); say('positional value', 'ok'); catch e, say('positional value', e.identifier, e.message); end
try, d = u10Probe(f, 'Bogus', 1); say('bogus NV', 'ok'); catch e, say('bogus NV', e.identifier, e.message); end
try, d = u10Probe(f, 'Position', [1 2 30 40]); say('Position NV', mat2str(d.Position), u10log()); catch e, say('Position NV', e.identifier, e.message); end
try, d = u10Probe(f, 'Units', 'normalized', 'Position', [0 0 .5 .5]); say('normalized', mat2str(d.Position), mat2str(getpixelposition(d))); catch e, say('normalized', e.identifier, e.message); end
try, d = u10Probe(f, 'SetupCalls', 4); say('private NV', 'ok'); catch e, say('private NV', e.identifier, e.message); end
try, d = u10Probe(f, 'Grid', 4); say('private-access NV', 'ok'); catch e, say('private-access NV', e.identifier, e.message); end
try, d = u10Probe(f, 'value', 4); say('lowercase NV', d.Value); catch e, say('lowercase NV', e.identifier, e.message); end
try, d = u10Probe(f, 'Val', 4); say('partial NV', d.Value); catch e, say('partial NV', e.identifier, e.message); end
try, d = u10Probe(f, "Value", 4); say('string NV', d.Value); catch e, say('string NV', e.identifier, e.message); end
try, d = u10Probe(f, struct('Value', 8)); say('struct NV', d.Value); catch e, say('struct NV', e.identifier, e.message); end
try, d = u10Probe(f, 'Value'); say('odd NV', 'ok'); catch e, say('odd NV', e.identifier, e.message); end
u10log();
try
    d = u10Probe();
    say('no parent', class(d.Parent), d.Parent.Type, char(d.Parent.Visible), u10log());
    delete(d.Parent);
catch e
    say('no parent', e.identifier, e.message);
end
try, cf = figure('Visible', 'off'); d = u10Probe(cf); say('classic figure', class(d.Parent), d.Parent.Type); catch e, say('classic figure', e.identifier, e.message); end
try, delete(cf); catch, end
try, d = u10Probe(uipanel(f)); say('in panel', d.Parent.Type); catch e, say('in panel', e.identifier, e.message); end
g = uigridlayout(f, [2 2]);
try
    d = u10Probe(g);
    d.Layout.Row = 2; d.Layout.Column = [1 2];
    say('in grid', class(d.Layout), mat2str(d.Layout.Row), mat2str(d.Layout.Column));
catch e
    say('in grid', e.identifier, e.message);
end
delete(g);
try, d = u10Probe(c); say('in another', d.Parent.Type); catch e, say('in another', e.identifier, e.message); end
try, d = u10Half(f); say('half', 'ok'); catch e, say('half', e.identifier, e.message); end
try, d = matlab.ui.componentcontainer.ComponentContainer(f); say('base', 'ok'); catch e, say('base', e.identifier, e.message); end

u10log();
try
    d = u10Ctor('ex', f);
    say('own ctor', u10log());
    drawnow; say('own ctor drawnow', u10log());
    drawnow; say('own ctor drawnow 2', u10log());
    drawnow; say('own ctor drawnow 3', u10log());
    say('own ctor Count', d.Count);
catch e
    say('own ctor', e.identifier, e.message, u10log());
end

% --- failures
setappdata(groot, 'u10bad', 'setup');
u10log();
n0 = numel(f.Children);
try, d = u10Bad(f); say('setup error', 'no error'); catch e, say('setup error', e.identifier, e.message, u10log()); end
say('children after setup error', numel(f.Children) - n0);
setappdata(groot, 'u10bad', 'update');
d = u10Bad(f);
u10log();
try, drawnow; say('update error', 'no error', u10log()); catch e, say('update error', e.identifier, e.message, u10log()); end
try, drawnow; say('update error 2', u10log()); catch e, say('update error 2', e.identifier, e.message); end
d.Mode = 'x';
try, drawnow; say('update error 3', u10log()); catch e, say('update error 3', e.identifier, e.message); end
setappdata(groot, 'u10bad', 'none');
d.Mode = 'y';
drawnow; say('update after recovery', u10log());
delete(d);

% --- deletion
d = u10Probe(f);
gg = d.grid();
u10log();
delete(d);
say('delete', u10log(), num2str(isvalid(d)), num2str(isvalid(gg)));
d = u10Probe(f);
gg = d.grid();
delete(gg);
say('delete grid', num2str(isvalid(d)), mat2str(size(d.Children)));
u10log();
delete(f);
say('fig delete', num2str(isvalid(c)), u10log());

% --- SpinnerGauge
f = uifigure('Visible', 'off');
s = SpinnerGauge(f, 'Value', 40);
say('SG ctor', s.SetupCalls, s.UpdateCalls);
drawnow;
say('SG drawnow', s.SetupCalls, s.UpdateCalls);
s.Value = 50; s.Value = 60;
drawnow;
say('SG two sets', s.UpdateCalls);
say('SG Position', mat2str(s.Position));
say('SG Type', s.Type);
delete(f);
end

function say(name, varargin)
parts = cellfun(@txt, varargin, 'UniformOutput', false);
fprintf('%s | %s\n', name, strjoin(parts, ' | '));
end

function s = txt(v)
if ischar(v), s = v;
elseif isstring(v), s = char(join(v, ','));
elseif islogical(v) || isnumeric(v), s = mat2str(v);
else, s = ['<' class(v) '>'];
end
end

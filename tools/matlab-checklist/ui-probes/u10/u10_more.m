function u10_more
% U10 probe, part two: children, gcbo, ancestor, reparenting, display, refusals. Headless.
here = fileparts(mfilename('fullpath'));
addpath(here);
f = uifigure('Visible', 'off');
u10log();
c = u10Probe(f);
try, b = uibutton(c); say('child after ctor', 'ok'); catch e, say('child after ctor', e.identifier, e.message); end
try, b = uibutton(f, 'Parent', c); say('child via Parent NV', 'ok'); catch e, say('child via Parent NV', e.identifier, e.message); end
b = uibutton(f);
try, b.Parent = c; say('reparent into c', 'ok'); catch e, say('reparent into c', e.identifier, e.message); end
try, g = c.grid(); uilabel(g); say('child of the grid after ctor', mat2str(size(g.Children))); catch e, say('child of the grid after ctor', e.identifier, e.message); end
late = u10Late(f);
drawnow;
say('made in update', u10log());
say('findall f', strjoin(sort(get(findall(f), 'Type'))', ','));
say('findobj f', strjoin(sort(get(findobj(f), 'Type'))', ','));
say('findall c', mat2str(size(findall(c))));
say('allchild f', class(allchild(f)), mat2str(size(allchild(f))));
say('ancestor grid', class(ancestor(c.grid(), 'figure')));
say('ancestor c', class(ancestor(c, 'figure')));
say('ancestor grid cc', class(ancestor(c.grid(), 'u10probe')));
say('double', class(double(c)));
say('isequal', isequal(c, f.Children(1)));
try, s = c.Children(1); say('Children(1)', class(s)); catch e, say('Children(1)', e.identifier); end

% gcbo and the callback source
c.ValueChangedFcn = @(s, e) u10log(sprintf('gcbo %s %d', class(gcbo), isequal(gcbo, c)));
c.fire('ValueChanged');
say('gcbo in callback', u10log());
c.ValueChangedFcn = @(s, e) u10log(sprintf('gcbf %s', class(gcbf)));
c.fire('ValueChanged');
say('gcbf in callback', u10log());

% NV validation failure
try, d = u10Probe(f, 'Value', 'abc'); say('bad Value NV', 'ok'); catch e, say('bad Value NV', e.identifier, e.message, u10log()); end
try, d = u10Probe(f, 'Position', 'abc'); say('bad Position NV', 'ok'); catch e, say('bad Position NV', e.identifier, e.message, u10log()); end
try, d = u10Probe(f, 'ValueChangedFcn', 5); say('bad cb NV', 'ok'); catch e, say('bad cb NV', e.identifier, e.message, u10log()); end
try, d = u10Probe(f, 'Type', 'x'); say('Type NV', 'ok'); catch e, say('Type NV', e.identifier, e.message); end
try, d = u10Probe(5); say('number parent', 'ok'); catch e, say('number parent', e.identifier, e.message); end
try, d = u10Probe('Value', 2); say('NV only', class(d.Parent), d.Value); delete(d.Parent); catch e, say('NV only', e.identifier, e.message); end
try, ax = uiaxes(f); d = u10Probe(ax); say('axes parent', 'ok'); catch e, say('axes parent', e.identifier, e.message); end

% reparenting
f2 = uifigure('Visible', 'off');
drawnow; u10log();
c.Parent = f2;
say('reparent', num2str(c.Parent == f2), mat2str(size(f.Children)), u10log());
drawnow;
say('reparent drawnow', u10log());

% plain class with HasCallbackProperty
try, p = u10Plain(); say('plain', num2str(isprop(p, 'HappenedFcn'))); catch e, say('plain', e.identifier, e.message); end

% display
txt = evalc('c');
say('display', strrep(txt, newline, '\n'));
txt = evalc('disp(c)');
say('disp', strrep(txt, newline, '\n'));

% writes during setup and dirty marks from a listener
u10log();
d = u10Probe(f);
drawnow;
say('first drawnow', u10log());
addlistener(d, 'Value', 'PostSet', @(s, e) u10log('postset'));
say('postset on non-observable', 'no error');
delete(f); delete(f2);
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

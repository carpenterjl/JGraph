function u7_small
% Small questions U7 needed answered: the saveobj warning's text, where ObjectBeingDestroyed falls
% relative to a class's own delete, and what a listener on a graphics object sees.
global vlog_text
vlog_text = '';
addpath(fullfile(fileparts(fileparts(fileparts(fileparts(fileparts(mfilename('fullpath')))))), ...
    'tests', 'JGraph.Tests', 'MatlabParity', 'fixtures', 'helpers'));
app = U7Plain;
lastwarn('');
s = saveobj(app); %#ok<NASGU>
[msg, id] = lastwarn;
fprintf('saveobj: %s | %s\n', id, msg);
addlistener(app, 'ObjectBeingDestroyed', @(s, e) u7_note(sprintf('listener valid=%d', isvalid(s))));
vlog_text = '';
delete(app);
fprintf('order on delete(app): %s\n', vlog_text);

h = DeleteLogger2();
addlistener(h, 'ObjectBeingDestroyed', @(s, e) u7_note(sprintf('listener valid=%d', isvalid(s))));
vlog_text = '';
delete(h);
fprintf('plain class order: %s\n', vlog_text);

uf = uifigure('Visible', 'off');
b = uibutton(uf);
addlistener(b, 'ObjectBeingDestroyed', @(s, e) u7_note(sprintf('listener valid=%d same=%d', isvalid(s), s == b)));
b.DeleteFcn = @(s, e) u7_note(sprintf('deletefcn valid=%d', isvalid(s)));
vlog_text = '';
delete(b);
fprintf('direct delete of a button: %s\n', vlog_text);
try
    addlistener(uf, 'Name', 'PostSet', @(s, e) u7_note('name'));
    uf.Name = 'x';
    fprintf('property listener on a figure: %s\n', vlog_text);
catch e
    fprintf('property listener: %s | %s\n', e.identifier, e.message);
end
ev = events(b);
fprintf('events of a deleted button: %d\n', numel(ev));
b = uibutton(uf);
fprintf('events of a button: %s\n', strjoin(events(b)', ','));
fprintf('events of a figure: %s\n', strjoin(events(uf)', ','));
try
    U7App(1, 'a', 3);
catch e
    fprintf('too many: %s | %s | %s\n', e.identifier, e.message, e.stack(1).name);
end
try
    u7_two(1, 2, 3);
catch e
    fprintf('too many, function: %s | %s\n', e.identifier, e.message);
end
f = @(a) a;
try
    f(1, 2);
catch e
    fprintf('too many, anonymous: %s | %s\n', e.identifier, e.message);
end
delete(findall(groot, 'Type', 'figure'));
end

function u7_two(a, b) %#ok<INUSD>
end

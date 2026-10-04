function u1w_uitest
% U1 WINDOW probe (needs a display; opens a uifigure — run only with the user's leave). The App
% Testing Framework refuses invisible figures (U0), so its gestures are recorded here as the oracle
% for what a user's action does to a uifigure component: which callbacks run, in which order, with
% which event data, and what Value reads inside each.
global LOG
LOG = {};
tc = matlab.uitest.TestCase.forInteractiveUse;
fig = uifigure('Name', 'u1w_uitest', 'Position', [300 300 420 320]);
btn = uibutton(fig, 'Text', 'Push', 'Position', [20 270 100 22], ...
    'ButtonPushedFcn', @(s, e) note('button.ButtonPushedFcn', s, e));
ef = uieditfield(fig, 'Position', [20 230 160 22], ...
    'ValueChangedFcn', @(s, e) note('edit.ValueChangedFcn', s, e), ...
    'ValueChangingFcn', @(s, e) note('edit.ValueChangingFcn', s, e));
nf = uieditfield(fig, 'numeric', 'Position', [200 230 100 22], ...
    'ValueChangedFcn', @(s, e) note('numeric.ValueChangedFcn', s, e));
sl = uislider(fig, 'Position', [20 190 200 3], ...
    'ValueChangedFcn', @(s, e) note('slider.ValueChangedFcn', s, e), ...
    'ValueChangingFcn', @(s, e) note('slider.ValueChangingFcn', s, e));
dd = uidropdown(fig, 'Items', {'A', 'B', 'C'}, 'Position', [20 120 100 22], ...
    'ValueChangedFcn', @(s, e) note('dropdown.ValueChangedFcn', s, e));
cb = uicheckbox(fig, 'Text', 'Check', 'Position', [20 90 100 22], ...
    'ValueChangedFcn', @(s, e) note('checkbox.ValueChangedFcn', s, e));
set(fig, 'WindowButtonDownFcn', @(s, e) note('fig.WindowButtonDownFcn', s, e), ...
    'WindowKeyPressFcn', @(s, e) note('fig.WindowKeyPressFcn', s, e), ...
    'KeyPressFcn', @(s, e) note('fig.KeyPressFcn', s, e));
drawnow; pause(1);

gesture('press button', @() tc.press(btn));
gesture('type hello into the edit field', @() tc.type(ef, 'hello'));
gesture('type 42 into the numeric field', @() tc.type(nf, 42));
gesture('drag the slider 0 to 60', @() tc.drag(sl, 0, 60));
gesture('choose B', @() tc.choose(dd, 'B'));
gesture('press the checkbox', @() tc.press(cb));
gesture('press the figure', @() tc.press(fig, [380 20]));
gesture('type x into the figure', @() tc.type(fig, 'x'));

fprintf('%s\n', LOG{:});
delete(fig);
end

function gesture(name, run)
global LOG
LOG{end + 1} = ['== ' name];
try
    run();
    drawnow;
    pause(0.5);
catch e
    LOG{end + 1} = sprintf('ERR %s %s', e.identifier, e.message);
end
end

function note(who, s, e)
global LOG
line = sprintf('%s: class=%s', who, class(e));
if isprop(s, 'Value')
    line = sprintf('%s s.Value=%s', line, v2s(s.Value));
end
if isobject(e)
    for p = properties(e)'
        if any(strcmp(p{1}, {'Source'})), continue; end
        line = sprintf('%s %s=%s', line, p{1}, v2s(e.(p{1})));
    end
end
LOG{end + 1} = line;
end

function s = v2s(v)
if ischar(v), s = ['''' v ''''];
elseif isstring(v), s = ['"' char(strjoin(v, '|')) '"'];
elseif isnumeric(v) || islogical(v), s = mat2str(v, 6);
elseif iscell(v), s = ['{' strjoin(cellfun(@v2s, v, 'UniformOutput', false), ',') '}'];
elseif isa(v, 'matlab.graphics.Graphics'), s = ['<' class(v) '>'];
else, s = ['<' class(v) '>'];
end
end

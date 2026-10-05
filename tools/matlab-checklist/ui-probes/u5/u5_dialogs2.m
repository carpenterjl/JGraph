function u5_dialogs2
% U5 probe: uialert and focus, which answer nothing, without a display. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off');
b = uibutton(uf);
tryn('uialert invisible', @() uialert(uf, 'm', 't'));
tryn('uialert no args', @() uialert());
tryn('uialert one arg', @() uialert(uf));
tryn('uialert two args', @() uialert(uf, 'm'));
tryn('uialert number', @() uialert(5.5, 'm', 't'));
tryn('uialert classic figure', @() uialert(f, 'm', 't'));
tryn('uialert component', @() uialert(b, 'm', 't'));
tryn('uialert bad option on invisible', @() uialert(uf, 'm', 't', 'Bogus', 1));
tryn('uialert bad icon on invisible', @() uialert(uf, 'm', 't', 'Icon', 'bogus'));
tryn('uialert numeric message on invisible', @() uialert(uf, 5, 't'));
tryn('focus invisible figure', @() focus(uf));
tryn('focus component of invisible figure', @() focus(b));
tryn('focus classic figure', @() focus(f));
tryn('focus label', @() focus(uilabel(uf)));
tryn('focus axes', @() focus(uiaxes(uf)));
tryn('focus panel', @() focus(uipanel(uf)));
tryn('focus deleted', @() focus(deleted(uf)));
tryn('focus two', @() focus([b uibutton(uf)]));
tryn('focus uicontrol', @() focus(uicontrol(f)));

vf = uifigure('Position', [100 100 400 300]);
tryn('uialert visible', @() uialert(vf, 'm', 't'));
tryn('uialert message cell', @() uialert(vf, {'a', 'b'}, 't'));
tryn('uialert message string array', @() uialert(vf, ["a" "b"], "t"));
tryn('uialert message number', @() uialert(vf, 5, 't'));
tryn('uialert message empty', @() uialert(vf, '', 't'));
tryn('uialert title number', @() uialert(vf, 'm', 5));
tryn('uialert title cell', @() uialert(vf, 'm', {'t'}));
tryn('uialert bad option', @() uialert(vf, 'm', 't', 'Bogus', 1));
tryn('uialert odd options', @() uialert(vf, 'm', 't', 'Icon'));
icons = {'error', 'warning', 'info', 'success', 'question', 'none', '', 'bogus', 5, 'ERROR', 'err'};
for k = 1:numel(icons), tryn(sprintf('uialert Icon %s', v2s(icons{k})), @() uialert(vf, 'm', 't', 'Icon', icons{k})); end
tryn('uialert Modal false', @() uialert(vf, 'm', 't', 'Modal', false));
tryn('uialert Modal bogus', @() uialert(vf, 'm', 't', 'Modal', 'bogus'));
tryn('uialert Interpreter html', @() uialert(vf, 'm', 't', 'Interpreter', 'html'));
tryn('uialert Interpreter bogus', @() uialert(vf, 'm', 't', 'Interpreter', 'bogus'));
tryn('uialert CloseFcn handle', @() uialert(vf, 'm', 't', 'CloseFcn', @(s, e) disp(1)));
tryn('uialert CloseFcn text', @() uialert(vf, 'm', 't', 'CloseFcn', 'disp(1)'));
tryn('uialert CloseFcn cell', @() uialert(vf, 'm', 't', 'CloseFcn', {@disp, 1}));
tryn('uialert CloseFcn number', @() uialert(vf, 'm', 't', 'CloseFcn', 5));
tryn('uialert lower-case option', @() uialert(vf, 'm', 't', 'icon', 'info'));
tryn('uialert prefix option', @() uialert(vf, 'm', 't', 'Ic', 'info'));
tryn('uialert on a component of a visible figure', @() uialert(uibutton(vf), 'm', 't'));
tryn('focus visible figure', @() focus(vf));
tryn('focus component of visible figure', @() focus(uibutton(vf)));
tryn('close all force then count', @() closeall());
delete(findall(groot, 'Type', 'figure'));
end

function tryn(label, fn)
lastwarn('');
try
    fn();
    r = 'ok';
catch e
    r = sprintf('ERR %s | %s', e.identifier, oneline(e.message));
end
[m, id] = lastwarn;
if ~isempty(m), r = sprintf('%s  WARN %s | %s', r, id, oneline(m)); end
fprintf('%s : %s\n', label, r);
end

function h = deleted(uf)
h = uibutton(uf); delete(h);
end

function closeall()
close all force
fprintf('   figures left: %d\n', numel(findall(groot, 'Type', 'figure')));
end

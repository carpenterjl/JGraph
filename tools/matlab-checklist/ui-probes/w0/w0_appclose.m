% w0_appclose - what AppCloseDataFcn is on a running app in R2025b (stage W0 of the App Designer window
% plan; App Details' Enable AppCloseDataFcn), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w0_appclose
% App Designer's popout tells a calling app to set dlg.AppCloseDataFcn and read event.App; the app's
% class code does not change. This records where the property lives (matlab.apps.AppBase's metaclass),
% whether setting it works on an app saved without the option, and what reaches the function on delete.
here = fileparts(mfilename('fullpath'));
addpath(fullfile(fileparts(here), 'ds8', 'rt', 'accept'));
mc = ?matlab.apps.AppBase;
fprintf('== matlab.apps.AppBase properties\n');
for p = mc.PropertyList'
    fprintf('  %s: Hidden %d, SetAccess %s, GetAccess %s, Transient %d\n', p.Name, p.Hidden, ...
        acc(p.SetAccess), acc(p.GetAccess), p.Transient);
end
fprintf('== matlab.apps.AppBase methods (not handle''s)\n');
hm = {?handle}; hm = {hm{1}.MethodList.Name};
for m = mc.MethodList'
    if ~any(strcmp(m.Name, hm)), fprintf('  %s: Hidden %d, Access %s, Static %d\n', m.Name, m.Hidden, acc(m.Access), m.Static); end
end
app = AcceptForm(5);
fprintf('isprop AppCloseDataFcn %d\n', isprop(app, 'AppCloseDataFcn'));
try
    v = app.AppCloseDataFcn;
    fprintf('AppCloseDataFcn default: %s %s\n', class(v), mat2str(size(v)));
catch e
    fprintf('read AppCloseDataFcn -> %s: %s\n', e.identifier, e.message);
end
global W0_CLOSE %#ok<GVMIS> the callback reports here
W0_CLOSE = {};
try
    app.AppCloseDataFcn = @(src, event) w0_appclose_seen(src, event);
    fprintf('set AppCloseDataFcn: ok\n');
catch e
    fprintf('set AppCloseDataFcn -> %s: %s\n', e.identifier, e.message);
end
delete(app);
drawnow;
fprintf('after delete: %d call(s)\n', numel(W0_CLOSE));
for k = 1:numel(W0_CLOSE), fprintf('  %s\n', W0_CLOSE{k}); end
app = AcceptForm(5);
try
    app.AppCloseDataFcn = 'notafunction';
    fprintf('set AppCloseDataFcn to a char: ok\n');
catch e
    fprintf('set AppCloseDataFcn to a char -> %s: %s\n', e.identifier, e.message);
end
delete(app);
fprintf('done\n');

function s = acc(a)
if ischar(a), s = a; elseif iscell(a), s = strjoin(cellfun(@(x) x.Name, a, 'UniformOutput', false), ','); else, s = class(a); end
end

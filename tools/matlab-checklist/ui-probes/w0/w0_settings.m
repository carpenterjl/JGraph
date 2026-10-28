% w0_settings - App Designer's settings in R2025b and the editor settings its Code View reads (stage W0
% of the App Designer window plan), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w0_settings
% Walks each settings group and prints every setting with its class, active value, factory value and
% whether a personal value is set. Nothing is written.
s = settings;
roots = {'matlab.appdesigner', 'matlab.editor.displaysettings', 'matlab.editor.spelling', ...
    'matlab.editor.codefolding', 'matlab.editor.language.matlab', 'matlab.editor.tab', ...
    'matlab.fonts.codefont', 'matlab.appearance.figure', ...
    };
for r = roots
    try
        g = eval(['s.' r{1}]);
    catch e
        fprintf('== %s: %s\n', r{1}, e.message);
        continue
    end
    walk(g, r{1});
end

% The web client also reads these by name (from the bundle); properties() does not list them all.
named = {'codeview', {'ShowAppMigrationTips', 'ThemedReadonlyBackgroundColor'}
    'compatibility', {'ShowBackwardCompatibilityWarningTip'}
    'componentlibrary', {'Favorites', 'CategoryOrder', 'ViewMode'}
    'fileformat', {'CreateNewAppsAs', 'ShowNewPlainTextInfoMessage'}
    'window', {'ShowTitleFilePath', 'ShowTitleFileExtension'}
    'designview', {'AlignmentHints', 'SizeIndicator'}};
fprintf('== matlab.appdesigner settings the client reads by name\n');
for k = 1:size(named, 1)
    grp = named{k, 1};
    has = hasGroup(s.matlab.appdesigner, grp);
    fprintf('  group %s: hasGroup %d\n', grp, has);
    for n = named{k, 2}
        if ~has, continue, end
        g = s.matlab.appdesigner.(grp);
        if hasSetting(g, n{1})
            v = g.(n{1});
            fprintf('    %s : active %s, factory %s\n', n{1}, show(@() v.ActiveValue), show(@() v.FactoryValue));
        else
            fprintf('    %s : not a setting\n', n{1});
        end
    end
end

function walk(g, path)
fprintf('== %s\n', path);
names = sort(properties(g));
groups = {};
for k = 1:numel(names)
    n = names{k};
    v = g.(n);
    if isa(v, 'matlab.settings.SettingsGroup')
        groups{end + 1} = n; %#ok<AGROW> settings first, then the groups under this one
    elseif isa(v, 'matlab.settings.Setting')
        a = show(@() v.ActiveValue);
        f = show(@() v.FactoryValue);
        p = 'no';
        try, if hasPersonalValue(v), p = 'yes'; end, catch, p = '?'; end %#ok<CTCH>
        fprintf('  %s : active %s, factory %s, personal %s\n', n, a, f, p);
    end
end
for k = 1:numel(groups)
    walk(g.(groups{k}), [path '.' groups{k}]);
end
end

function t = show(get)
try
    x = get();
    if ischar(x) || isstring(x), t = sprintf('%s "%s"', class(x), x);
    elseif isnumeric(x) || islogical(x), t = sprintf('%s %s', class(x), mat2str(x));
    elseif iscell(x), t = sprintf('cell %s', mat2str(size(x)));
    else, t = class(x);
    end
catch e
    t = ['<' e.identifier '>'];
end
end

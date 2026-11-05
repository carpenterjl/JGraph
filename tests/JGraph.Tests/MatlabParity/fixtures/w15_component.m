% record: -noFigureWindows
% w15_component.m -- a custom UI component authored as App Designer's code engine writes one (App Designer window
% plan W15, ADR 0305): helpers\W15Counter.m made with an invisible figure as its parent - its position, units,
% events, public callback properties, the button's push through the callback, update's write, the managed
% properties' validation - and appdesigner.customcomponent.configureMetadata's and removeMetadata's refusals in
% R2025b's words. The registry is written and removed in a temporary folder. configureMetadata on a valid class
% opens R2025b's dialog, which a headless JGraph refuses: not printed. The rows that differ by decision are div rows.
work = tempname;
mkdir(work);
copyfile(which('W15Counter'), work);
cleanupWork = onCleanup(@() rmdir(work, 's'));

% --- the component -----------------------------------------------------------------------------------------
fig = uifigure('Visible', 'off');
c = W15Counter('Parent', fig);
fprintf('CHK|class|%s|exact\n', class(c));
fprintf('CHK|position|%s|exact\n', mat2str(c.Position));
fprintf('CHK|units|%s|exact\n', c.Units);
fprintf('CHK|events|%s|exact\n', strjoin(sort(events(c))', ','));
fprintf('CHK|callbacks|%d %d %d|exact\n', isprop(c, 'ClearedFcn'), isprop(c, 'ValueChangedFcn'), isprop(c, 'NoSuchFcn'));
fprintf('CHK|value|%g|exact\n', c.Value);
fprintf('CHK|limits|%s|exact\n', mat2str(c.Limits));
drawnow;
fprintf('CHK|text_after_setup|%s|exact\n', c.buttonText());
c.Value = 5;
drawnow;
fprintf('CHK|text_after_value|%s|exact\n', c.buttonText());
global w15_fired
w15_fired = 0;
c.ValueChangedFcn = @(src, evt) w15_bump();
c.pushButton();
drawnow;
fprintf('CHK|value_after_push|%g|exact\n', c.Value);
fprintf('CHK|fired|%d|exact\n', w15_fired);
fprintf('CHK|value_500|%s|exact\n', w15_id(@() set(c, 'Value', 500)));
fprintf('CHK|limits_3|%s|exact\n', w15_id(@() set(c, 'Limits', [1 2 3])));
delete(fig);

% --- the refusals ------------------------------------------------------------------------------------------
fprintf('CHK|no_args|%s|exact\n', w15_id(@() appdesigner.customcomponent.configureMetadata()));
fprintf('CHK|number|%s|exact\n', w15_id(@() appdesigner.customcomponent.configureMetadata(1)));
fprintf('CHK|missing|%s|exact\n', strrep(w15_id(@() appdesigner.customcomponent.configureMetadata('w15nosuch.m')), pwd, '<fixtures>'));
fprintf('CHK|missing_no_ext|%s|exact\n', strrep(w15_id(@() appdesigner.customcomponent.removeMetadata('w15nosuch')), pwd, '<fixtures>'));
fprintf('CHK|keyword|%s|exact\n', w15_id(@() appdesigner.customcomponent.configureMetadata('for')));
fprintf('CHK|bad_name|%s|exact\n', w15_id(@() appdesigner.customcomponent.configureMetadata('1bad.m')));
fprintf('CHK|two_args|%s|exact\n', w15_id(@() appdesigner.customcomponent.removeMetadata('W15Counter.m', 2)));
% The message R2025b hands the wrong extension ("must have a .txt extension"); JGraph names the two it takes.
fprintf('CHK|txt|%s|div=ADR0305\n', w15_id(@() appdesigner.customcomponent.configureMetadata('w15fn.txt')));
fprintf('CHK|function_file|%s|exact\n', strrep(w15_id(@() appdesigner.customcomponent.configureMetadata(which('w15fn'))), which('w15fn'), '<w15fn>'));
fprintf('CHK|plain_class|%s|exact\n', strrep(w15_id(@() appdesigner.customcomponent.removeMetadata(which('W15Plain'))), which('W15Plain'), '<W15Plain>'));

% --- the registry ------------------------------------------------------------------------------------------
component = fullfile(work, 'W15Counter.m');
% No registry file: R2025b fails on its own isKey (MATLAB:UndefinedFunction); JGraph has nothing to remove.
fprintf('CHK|remove_no_registry|%s|div=ADR0305\n', w15_id(@() appdesigner.customcomponent.removeMetadata(component)));
mkdir(fullfile(work, 'resources'));
registry = fullfile(work, 'resources', 'appDesigner.json');
fid = fopen(registry, 'w'); fprintf(fid, '{not json'); fclose(fid);
% R2025b has no text for its InvalidModelErrorMsg key; JGraph says the warning's first sentence.
fprintf('CHK|remove_invalid_id|%s|exact\n', strtok(w15_id(@() appdesigner.customcomponent.removeMetadata(component)), ' '));
fid = fopen(registry, 'w');
fprintf(fid, '%s', ['{"components":[{"className":"W15Counter","componentName":"Counter","version":"1.0","description":"","category":"My Components",' ...
    '"icon":"","authorName":"","authorEmail":"","avatar":"","avatarDark":"","defaultPosition":[1,1,320,240]},' ...
    '{"className":"Other","componentName":"Other","version":"1.0","description":"","category":"Widgets",' ...
    '"icon":"","authorName":"","authorEmail":"","avatar":"","avatarDark":"","defaultPosition":[1,1,100,100]}],"schema":3,"MATLABRelease":"R2025b"}']);
fclose(fid);
appdesigner.customcomponent.removeMetadata(component);
text = fileread(registry);
fprintf('CHK|removed_one|%s|exact\n', strrep(strrep(text, newline, '\n'), char(13), ''));
appdesigner.customcomponent.removeMetadata(component);
fprintf('CHK|removed_again|%s|exact\n', strrep(strrep(fileread(registry), newline, '\n'), char(13), ''));
clear w15_fired global

function s = w15_id(f)
    try
        f();
        s = 'no error';
    catch e
        % R2025b's InvalidModelErrorMsg has no catalog text: reading its message throws.
        try
            m = strtok(e.message, newline);
        catch
            m = '<message unreadable>';
        end
        s = [e.identifier ' :: ' m]; % a CHK value must not hold the | delimiter
    end
end

function w15_bump
    global w15_fired
    w15_fired = w15_fired + 1;
end

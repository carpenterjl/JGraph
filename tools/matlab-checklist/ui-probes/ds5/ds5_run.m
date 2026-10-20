% DS5 (designer plan): R2025b runs an app the designer's code edits built - CallbacksApp, written by
% the local test AppDesignerModelSaveTests.ANewAppHoldsAppDesignersWholeModel with JGRAPH_DS6_OUT
% naming ds6/rt - and App Designer's reader binds its callbacks. The app has a button with a callback,
% an edit field bound to a callback of its own and to the button's as well, a startup function with two
% input parameters, a single running instance, a property Count and a function func.
%
% Run with DS5_APP naming the folder holding CallbacksApp.mlapp (the default is ds6/rt/new/03_CallbacksApp).
% One line per check, then a tally. Headless: -noFigureWindows.
here = fileparts(mfilename('fullpath'));
folder = getenv('DS5_APP');
if isempty(folder), folder = fullfile(here, '..', 'ds6', 'rt', 'new', '03_CallbacksApp'); end
addpath(folder);
checks = 0; failed = 0;

% --- running ----------------------------------------------------------------------------------------
try
    % Count is a private property; what it holds shows in the label the callbacks set.
    app = CallbacksApp(2, 3);
    checks = checks + 1; ok = isa(app, 'CallbacksApp') && strcmp(app.Label.Text, '5'); failed = failed + ~ok;
    note(ok, sprintf('startupFcn(app, a, b) ran with the input arguments: the label reads ''%s''', app.Label.Text));

    app.Button.ButtonPushedFcn(app.Button, []);
    checks = checks + 1; ok = strcmp(app.Label.Text, '60'); failed = failed + ~ok;
    note(ok, sprintf('the button''s callback ran and called func: the label reads ''%s''', app.Label.Text));

    % The edit field's ValueChangingFcn is bound to the button's callback too.
    checks = checks + 1; ok = isa(app.EditField.ValueChangingFcn, 'function_handle'); failed = failed + ~ok;
    note(ok, 'the edit field''s ValueChangingFcn is bound');
    app.EditField.ValueChangingFcn(app.EditField, []);
    checks = checks + 1; ok = strcmp(app.Label.Text, '70'); failed = failed + ~ok;
    note(ok, sprintf('the shared callback ran from the edit field: the label reads ''%s''', app.Label.Text));

    % A single running instance: a second construction returns the running app.
    again = CallbacksApp(1, 1);
    checks = checks + 1; ok = again == app && strcmp(app.Label.Text, '70'); failed = failed + ~ok;
    note(ok, 'a second CallbacksApp(1, 1) is the running instance, its startup not run again');
    delete(app);
    checks = checks + 1; ok = ~isvalid(app); failed = failed + ~ok;
    note(ok, 'delete(app) deleted it');
catch e
    failed = failed + 1; checks = checks + 1;
    note(false, ['running: ' getReport(e, 'basic')]);
end

% --- App Designer's reader ----------------------------------------------------------------------------
try
    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fullfile(folder, 'CallbacksApp.mlapp'));
    data = des.getAppData();
    code = data.code;
    checks = checks + 1; ok = strcmp(code.ClassName, 'CallbacksApp'); failed = failed + ~ok;
    note(ok, sprintf('code copy: class %s', code.ClassName));
    names = {code.Callbacks.Name};
    checks = checks + 1; ok = isequal(names, {'ButtonPushed', 'EditFieldValueChanged'}); failed = failed + ~ok;
    note(ok, ['code copy: callbacks ' strjoin(names, ', ')]);
    checks = checks + 1; ok = strcmp(code.StartupCallback.Name, 'startupFcn') && strcmp(code.InputParameters, 'a, b'); failed = failed + ~ok;
    note(ok, sprintf('code copy: startup %s(%s)', code.StartupCallback.Name, code.InputParameters));
    checks = checks + 1; ok = isfield(code, 'SingletonMode') && strcmp(code.SingletonMode, 'FOCUS'); failed = failed + ~ok;
    note(ok, 'code copy: SingletonMode FOCUS');
    checks = checks + 1; ok = any(contains(code.EditableSectionCode, 'Count = 0')); failed = failed + ~ok;
    note(ok, 'code copy: the editable section holds the property');
    fig = data.components.UIFigure;
    button = findobj(fig, '-property', 'DesignTimeProperties', '-and', 'Type', 'uibutton');
    checks = checks + 1; ok = numel(button) == 1 && strcmp(button.ButtonPushedFcn, 'ButtonPushed'); failed = failed + ~ok;
    note(ok, 'tree: the button names its callback');
    field = findobj(fig, 'Type', 'uieditfield');
    checks = checks + 1; ok = numel(field) == 1 && strcmp(field.ValueChangedFcn, 'EditFieldValueChanged') && strcmp(field.ValueChangingFcn, 'ButtonPushed'); failed = failed + ~ok;
    note(ok, 'tree: the edit field names both callbacks');
    % ComponentData is rebuilt on load from the tree's callback names.
    cb = code.Callbacks;
    bound = arrayfun(@(c) numel(c.ComponentData), cb);
    checks = checks + 1; ok = isequal(bound, [2 1]); failed = failed + ~ok;
    note(ok, sprintf('reader: callbacks bound to %s components', mat2str(bound)));
catch e
    failed = failed + 1; checks = checks + 1;
    note(false, ['reader: ' getReport(e, 'basic')]);
end
fprintf('%d checks, %d failed\n', checks, failed);

function note(ok, what)
    if ok, fprintf('ok    %s\n', what); else, fprintf('FAIL  %s\n', what); end
end

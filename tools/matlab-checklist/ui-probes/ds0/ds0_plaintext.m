% DS0 (designer plan): R2025b's plain-text app format, which MathWorks announced for R2026b and
% which R2025b already carries behind the feature AppDesignerPlainTextFileFormat. Asks whether the
% feature is off by default, whether an .m app with a %[app:layout] appendix is refused with it off,
% and - with it switched on for this session only - whether App Designer's own load builds its
% canvas from the appendix (no MAT-file, no MCOS) and whether the app runs. The app is written here
% by hand from the grammar read in app_artifact_generator; rt/ (ignored) holds it.
if ~exist('rt', 'dir'), mkdir('rt'); end
folder = fullfile(pwd, 'rt', 'plain');
if ~exist(folder, 'dir'), mkdir(folder); end
fn = fullfile(folder, 'DsPlain.m');
L = {
'classdef DsPlain < matlab.apps.App'
''
'    properties (Access = public)'
'        UIFigure    matlab.ui.Figure'
'        GridLayout  matlab.ui.container.GridLayout'
'        AmpField    matlab.ui.control.NumericEditField'
'        GoButton    matlab.ui.control.Button'
'    end'
''
'    properties (Access = public)'
'        Log = {}'
'    end'
''
'    methods (Access = private)'
''
'        function GoButtonPushed(app, event)'
'            app.Log{end+1} = sprintf(''go %g'', app.AmpField.Value);'
'        end'
'    end'
'end'
''
'%[appendix]'
'%[app:layout]'
'%{'
'<Components>'
'  <UIFigure name="UIFigure">'
'    <Position>[100 100 320 160]</Position>'
'    <Name>''DS0 plain''</Name>'
'    <Children>'
'      <GridLayout name="GridLayout">'
'        <ColumnWidth>{''1x'', ''1x''}</ColumnWidth>'
'        <RowHeight>{22, 22}</RowHeight>'
'        <Children>'
'          <NumericEditField name="AmpField">'
'            <Value>2</Value>'
'            <Layout.Row>1</Layout.Row>'
'            <Layout.Column>[1 2]</Layout.Column>'
'          </NumericEditField>'
'          <Button name="GoButton">'
'            <Text>''Go''</Text>'
'            <ButtonPushedFcn>GoButtonPushed</ButtonPushedFcn>'
'            <Layout.Row>2</Layout.Row>'
'            <Layout.Column>1</Layout.Column>'
'          </Button>'
'        </Children>'
'      </GridLayout>'
'    </Children>'
'  </UIFigure>'
'</Components>'
'%}'
};
fid = fopen(fn, 'w', 'n', 'UTF-8'); fprintf(fid, '%s\n', L{:}); fclose(fid);
addpath(folder);

flag0 = feature('AppDesignerPlainTextFileFormat');
fprintf('feature AppDesignerPlainTextFileFormat default: %d\n', flag0);
fprintf('matlab.apps.App exists: %d\n', exist('matlab.apps.App', 'class') == 8);
try
    m = matlab.ui.internal.MAPPIntegration.getComponentMap();
    k = keys(m);
    fprintf('component map: %d tags; first %s\n', numel(k), strjoin(k(1:min(12, end)), ','));
    v = m("NumericEditField"); fprintf('NumericEditField -> %s args %s\n', v.InformalInterfaceName, string(v.AdditionalArguments));
catch e
    fprintf('component map ERROR %s | %s\n', e.identifier, e.message);
end

% with the feature off
tryRun('off: run', @() DsPlain());
tryLoad('off: getAppData', fn);

% with the feature on, this session only
feature('AppDesignerPlainTextFileFormat', 1);
c = onCleanup(@() feature('AppDesignerPlainTextFileFormat', flag0));
fprintf('feature now: %d\n', feature('AppDesignerPlainTextFileFormat'));
app = tryRun('on: run', @() DsPlain());
if ~isempty(app)
    try
        fprintf('on: run fields: AmpField %g, GoButton text %s, figure name %s\n', app.AmpField.Value, app.GoButton.Text, app.UIFigure.Name);
        f = app.GoButton.ButtonPushedFcn;
        if isa(f, 'function_handle'), fs = func2str(f); else, fs = char(string(f)); end
        fprintf('on: ButtonPushedFcn %s: %s\n', class(f), fs);
        % a user's press, as uitest's press would deliver it
        matlab.uitest.TestCase.forInteractiveUse().press(app.GoButton);
        fprintf('on: after the button: %s\n', strjoin(app.Log, ','));
    catch e
        fprintf('on: run inspect ERROR %s | %s\n', e.identifier, strrep(e.message, newline, ' '));
    end
    delete(app);
end
tryLoad('on: getAppData', fn);

function app = tryRun(label, f)
    app = [];
    try
        app = f();
        fprintf('%s: %s\n', label, class(app));
    catch e
        fprintf('%s ERROR %s | %s\n', label, e.identifier, strrep(e.message, newline, ' '));
    end
end

function tryLoad(label, fn)
    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
        fig = a.components.UIFigure;
        comps = findall(fig, '-property', 'DesignTimeProperties');
        names = arrayfun(@(c) c.DesignTimeProperties.CodeName, comps, 'UniformOutput', false);
        fprintf('%s: canvas %s with %d components: %s\n', label, class(fig), numel(comps), strjoin(names', ','));
        fprintf('%s: code fields %s; callbacks %s\n', label, strjoin(fieldnames(a.code)', ','), strjoin({a.code.Callbacks.Name}, ','));
        b = findall(fig, 'Type', 'uibutton');
        if ~isempty(b), fprintf('%s: button text %s, pushed fcn class %s\n', label, b(1).Text, class(b(1).ButtonPushedFcn)); end
        delete(fig);
    catch e
        fprintf('%s ERROR %s | %s\n', label, e.identifier, strrep(e.message, newline, ' '));
        for s = 1:min(4, numel(e.stack)), fprintf('   at %s:%d\n', e.stack(s).name, e.stack(s).line); end
    end
end

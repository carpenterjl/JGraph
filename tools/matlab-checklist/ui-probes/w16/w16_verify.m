function w16_verify
% W16 (App Designer window plan): the four files of the acceptance set, each read in R2025b as App Designer
% reads one - appdesigner.internal.application.loadApp's status, warning, metadata, the component tree it
% answers with each node's type and code name, the callbacks and what each is bound to, and for the
% component its AppTypeData. The files are rt\accept\*.mlapp, written by the local test
% AppDesignerWindowAcceptanceTests with JGRAPH_W16_OUT naming rt\. The component's registration,
% rt\accept\resources\appDesigner.json, is read as the metadata Model reads it. Headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w16_verify
beep('off');
here = fileparts(mfilename('fullpath'));
root = fullfile(here, 'rt', 'accept');
addpath(root);
say('release %s', version('-release'));
for name = {'W16Counter', 'W16Blank', 'W16Panels', 'W16Composite'}
    fn = fullfile(root, [name{1} '.mlapp']);
    say('== %s', name{1});
    if ~isfile(fn)
        say('no file at %s', fn);
        continue;
    end
    try
        lastwarn('');
        [outcome, data] = appdesigner.internal.application.loadApp(fn);
        say('loadApp: status %s, warning "%s"', string(outcome.Status), lastwarn);
        md = data.AppMetaData;
        say('metadata: AppType %s, Name %s, Min %s', md.AppType, md.Name, md.MinimumSupportedMATLABRelease);
        say('code: class %s, callbacks %d, startup %d, editable lines %d', data.CodeData.ClassName, numel(data.CodeData.Callbacks), ...
            ~isempty(data.CodeData.StartupFcn), numel(data.CodeData.EditableSectionCode));
        for k = 1:numel(data.CodeData.Callbacks)
            cb = data.CodeData.Callbacks(k);
            bound = {};
            for j = 1:numel(cb.ComponentData)
                bound{end + 1} = sprintf('%s.%s', cb.ComponentData(j).CodeName, cb.ComponentData(j).CallbackPropertyName); %#ok<AGROW>
            end
            say('  callback %s bound to %s', cb.Name, strjoin(bound, ','));
        end
        if isfield(data.CodeData, 'AppTypeData') && ~isempty(data.CodeData.AppTypeData)
            say('AppTypeData: %s', jsonencode(data.CodeData.AppTypeData));
        end
        cd = data.ComponentData;
        say('ComponentData: %s with %s', class(cd), strjoin(fieldnames(cd)', ','));
        walk(cd, '  ');
    catch e
        say('loadApp: FAILED %s | %s', e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
    end
end
% the registration as the metadata model reads it
say('== the registry');
try
    reg = jsondecode(fileread(fullfile(root, 'resources', 'appDesigner.json')));
    c = reg.components(1);
    say('registry: schema %d, %s; %s as %s in %s, version %s, defaultPosition %s, icon %d chars, avatar %d chars', reg.schema, reg.MATLABRelease, ...
        c.className, c.componentName, c.category, c.version, mat2str(c.defaultPosition(:)'), numel(c.icon), numel(c.avatar));
    m = appdesigner.internal.usercomponent.metadata.Model(fullfile(root, 'W16Counter.mlapp'));
    say('Model: validity %s, categories %s', jsonencode(m.getModelValidity()), strjoin(cellstr(m.getCategories()), ','));
    md = m.getComponentMetadata();
    say('Model: className %s, componentName %s, category %s, version %s, defaultPosition %s', md.className, md.componentName, md.category, md.version, mat2str(md.defaultPosition(:)'));
catch e
    say('registry: FAILED %s | %s', e.identifier, strtok(e.message, newline));
end
end

function walk(n, indent)
% The tree loadApp answers: each node's Type and code name, then its children.
if ~isstruct(n), say('%s(%s)', indent, class(n)); return; end
for i = 1:numel(n)
    t = ''; if isfield(n, 'Type'), t = char(n(i).Type); end
    cn = '';
    if isfield(n, 'PropertyValues') && isstruct(n(i).PropertyValues)
        pv = n(i).PropertyValues;
        if isfield(pv, 'DesignTimeProperties') && isstruct(pv.DesignTimeProperties) && isfield(pv.DesignTimeProperties, 'CodeName')
            cn = char(pv.DesignTimeProperties.CodeName);
        end
        if isempty(cn) && isfield(pv, 'CodeName'), cn = char(pv.CodeName); end
    end
    extra = '';
    if isfield(n, 'PropertyValues') && isstruct(n(i).PropertyValues) && isfield(n(i).PropertyValues, 'Step')
        extra = sprintf(' Step %s', mat2str(n(i).PropertyValues.Step));
    end
    say('%s%s %s%s', indent, t, cn, extra);
    if isfield(n, 'Children') && ~isempty(n(i).Children), walk(n(i).Children, [indent '  ']); end
end
end

function say(varargin)
fprintf([varargin{1} '\n'], varargin{2:end});
end

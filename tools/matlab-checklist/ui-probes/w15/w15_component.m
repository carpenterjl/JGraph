function w15_component
% W15 (App Designer window plan): custom component authoring, measured headless in R2025b.
%   1. The class texts App Designer's code engine writes for a component (templates/comp1..comp4,
%      derived from its ComponentAppCodeEngineConfig behaviors) compile and run: construction with and
%      without a parent, properties, events, the public callback, update, postSetupFcn, the stored
%      component code R2025b's generator makes under the CustomUIComponent app type.
%   2. A control .mlapp of comp3 written through MLAPPSerializer with the AppTypeData the client sends
%      (events, managed properties, the update method), read back through loadApp: the shapes.
%   3. App Designer's Run of a component (UserComponentRunner) and its parent figure.
%   4. configureMetadata and removeMetadata: the refusals, and the headless call.
%   5. validateAndInferPropertyDetails and validateInspectorPropertyValue over the dialog's cases.
beep('off');
here = fileparts(mfilename('fullpath'));
rt = fullfile(here, 'rt', 'w15');
if isfolder(rt), rmdir(rt, 's'); end
mkdir(rt);
copyfile(fullfile(here, 'templates', '*.m'), rt);
previous = cd(rt);
restore = onCleanup(@() cd(previous));
say('release %s', version('-release'));

%% 1 the texts
say('== 1 texts');
fig = uifigure('Visible', 'off');
for k = 1:4
    name = sprintf('comp%d', k);
    try
        mc = meta.class.fromName(name);
        say('%s: superclasses %s', name, strjoin({mc.SuperclassList.Name}, ','));
        c = feval(name, 'Parent', fig);
        say('%s: Position [%s], Units %s, class %s', name, num2str(c.Position), c.Units, class(c));
        pub = properties(c);
        say('%s: properties %s', name, strjoin(pub(1:min(6, end))', ','));
        ev = events(c);
        say('%s: events %s', name, strjoin(ev', ','));
        say('%s: callback props %s', name, strjoin(appdesigner.internal.usercomponent.getCallbackPropertyNames(c), ','));
        if k == 3
            say('comp3: Value %g after postSetup (Count, Button and Scale are private, as they should be: %d %d %d)', c.Value, isprop(c, 'Count'), isprop(c, 'Button'), isprop(c, 'Scale'));
            c.Value = 5; drawnow;
            say('comp3: after Value=5 (the button is private; its text is read in w15_verify through the fixture helper)');
            hits = 0;
            c.ValueChangedFcn = @(src, evt) assignin('base', 'w15hits', 1);
            l = addlistener(c, 'ValueChanged', @(src, evt) disp('listener ran'));
            say('comp3: the button is private; its callback is run through the fixture helper W15Counter.pushButton in the parity fixture');
            c.Value = c.Value + 1; notify(c, 'ValueChanged'); % what the callback does
            drawnow;
            say('comp3: after push Value %g, hit var %d', c.Value, evalin('base', 'exist(''w15hits'', ''var'')'));
            delete(l);
            try
                c.Value = 500;
            catch e
                say('comp3: Value=500 refused %s | %s', e.identifier, strtok(e.message, newline));
            end
            try
                c.Limits = [1 2 3];
            catch e
                say('comp3: Limits=[1 2 3] refused %s | %s', e.identifier, strtok(e.message, newline));
            end
        end
        if k == 4
            say('comp4: BackgroundColor [%s], Visible %s, Title %s, Label private %d, Hidden props %s', num2str(c.BackgroundColor), c.Visible, c.Title, ~isprop(c, 'Label'), ...
                strjoin(pub(contains(pub, 'Title'))', ','));
            try
                v = c.Label; %#ok<NASGU>
                say('comp4: Label read from outside ok');
            catch e
                say('comp4: Label read refused %s', e.identifier);
            end
        end
        delete(c);
    catch e
        say('%s: FAILED %s | %s', name, e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
    end
end
% no parent: the auto-created figure
try
    c = comp3;
    say('no parent: parent class %s, parent Position [%s], comp Position [%s], parent Visible %s, Name "%s"', class(c.Parent), num2str(c.Parent.Position), num2str(c.Position), c.Parent.Visible, c.Parent.Name);
    delete(c.Parent);
catch e
    say('no parent: FAILED %s | %s', e.identifier, strtok(e.message, newline));
end

% the generator under the CustomUIComponent app type: what App Designer stores as ComponentCode
say('== 1b generator');
adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
root = uifigure('Visible', 'off');
root.Position = [1 1 320 240];
grid = uigridlayout(root);
button = uibutton(grid, 'push');
button.Layout.Row = 1; button.Layout.Column = 1;
button.Text = 'Button';
label = uilabel(root);
label.Position = [10 10 100 22];
for h = {root, grid, button, label}
    hh = h{1};
    try
        code = gen.getComponentGenerationCode(hh, eval(adapters(class(hh))), 'CustomUIComponent', 'light');
        say('generated %s (component):', class(hh));
        for i = 1:numel(code), say('  | %s', code{i}); end
        code = gen.getComponentGenerationCode(hh, eval(adapters(class(hh))), 'Standard', 'light');
        say('generated %s (app):', class(hh));
        for i = 1:numel(code), say('  | %s', code{i}); end
    catch e
        say('generated %s: FAILED %s | %s', class(hh), e.identifier, strtok(e.message, newline));
    end
end

%% 2 the control .mlapp
say('== 2 control');
ctl = fullfile(rt, 'ctl');
mkdir(ctl);
try
    % the design-time tree as App Designer holds a component's: the figure stands for the component
    addprop(root, 'DesignTimeProperties');
    root.DesignTimeProperties = struct('CodeName', 'CustomUIComponent', 'GroupId', '', 'ComponentCode', {{}});
    addprop(grid, 'DesignTimeProperties');
    grid.DesignTimeProperties = struct('CodeName', 'GridLayout', 'GroupId', '', 'ComponentCode', {{}});
    addprop(button, 'DesignTimeProperties');
    button.DesignTimeProperties = struct('CodeName', 'Button', 'GroupId', '', 'ComponentCode', {{}});
    button.ButtonPushedFcn = 'ButtonPushed';
    delete(label);
    for h = {root, grid, button}
        hh = h{1};
        code = gen.getComponentGenerationCode(hh, eval(adapters(class(hh))), 'CustomUIComponent', 'light');
        hh.DesignTimeProperties.ComponentCode = code(:);
    end
    text = fileread(fullfile(rt, 'comp3.m'));
    s = appdesigner.internal.serialization.MLAPPSerializer(fullfile(ctl, 'comp3.mlapp'), root);
    s.OverwriteTargetFile = true;
    s.MatlabCodeText = text;
    s.EditableSectionCode = {'    properties (Access = private)'; '        Count % Description'; '    end'; '    '};
    s.Callbacks = struct('Name', {'ButtonPushed'}, 'Code', {{'            comp.Value = comp.Value + 1;'; '            notify(comp, ''ValueChanged'');'}});
    s.StartupCallback = struct('Name', 'postSetupFcn', 'Code', {{'            comp.Count = 0;'}});
    json = ['{"HasCallbackEvents":[{"Name":"Cleared","Description":""},{"Name":"ValueChanged","Description":"Fires when the value changes"}],' ...
        '"Methods":{"update":{"Name":"update","Code":["            comp.Button.Text = num2str(comp.Value);"]}},' ...
        '"ManagedUserProperties":[' ...
        '{"Name":"Scale","Description":"A private scale","DataType":"double","DefaultValue":"1","Size":[],"Attributes":["Access = private"],"ValidationFunctions":[]},' ...
        '{"Name":"Limits","Description":"The range","DataType":"double","DefaultValue":"[0 100]","Size":["1","2"],"Attributes":[],"ValidationFunctions":[{"Name":"mustBeNumeric"}]},' ...
        '{"Name":"Value","Description":"","DataType":"double","DefaultValue":"0","Size":["1","1"],"Attributes":[],"ValidationFunctions":[{"Name":"mustBeInRange","InputArguments":"0, 100, \"inclusive\""}]}]}'];
    s.AppTypeData = jsondecode(json);
    describe('sent AppTypeData', s.AppTypeData);
    md0 = s.Metadata;
    md0.Name = 'comp3';
    md0.AppType = 'CustomUIComponent';
    md0.MinimumSupportedMATLABRelease = 'R2022a';
    s.Metadata = md0;
    s.save();
    say('control saved: %d bytes', dir(fullfile(ctl, 'comp3.mlapp')).bytes);
    delete(root);
catch e
    say('control: FAILED %s | %s', e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
end
try
    lastwarn('');
    [outcome, data] = appdesigner.internal.application.loadApp(fullfile(ctl, 'comp3.mlapp'));
    say('loadApp: outcome fields %s; status %s', strjoin(fieldnames(outcome)', ','), string(outcome.Status));
    say('loadApp: warning "%s"', lastwarn);
    say('loadApp: data fields %s', strjoin(fieldnames(data)', ','));
    say('loadApp: code fields %s', strjoin(fieldnames(data.CodeData)', ','));
    md = data.AppMetaData;
    say('loadApp: metadata class %s, fields %s', class(md), strjoin(fieldnames(md)', ','));
    say('loadApp: metadata AppType %s, Min %s, Name %s', md.AppType, md.MinimumSupportedMATLABRelease, md.Name);
    describe('loaded AppTypeData', data.CodeData.AppTypeData);
    say('loaded AppTypeData json: %s', jsonencode(data.CodeData.AppTypeData));
    describe('loaded StartupFcn', data.CodeData.StartupFcn);
    describe('loaded Callbacks', data.CodeData.Callbacks);
    describe('loaded EditableSectionCode', data.CodeData.EditableSectionCode);
    say('loadApp: ComponentData class %s, fields %s', class(data.ComponentData), strjoin(fieldnames(data.ComponentData)', ','));
    say('loadApp: AppNameData %s; CompatibilityData %s; GroupData class %s', jsonencode(data.AppNameData), jsonencode(data.CompatibilityData), class(data.GroupData));
    f = data.ComponentData.UIFigure;
    say('loadApp: figure class %s, Position [%s], Name "%s", CodeName %s, children %s', class(f), num2str(f.Position), f.Name, f.DesignTimeProperties.CodeName, strjoin(arrayfun(@(h) class(h), allchild(f), 'UniformOutput', false)', ','));
    say('loadApp: figure ComponentCode: %s', strjoin(f.DesignTimeProperties.ComponentCode', ' | '));
    delete(f);
catch e
    say('loadApp: FAILED %s | %s', e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
end
% the saved .mlapp runs as a component class
try
    prev = cd(ctl);
    clear comp3
    c = comp3('Parent', fig);
    say('mlapp class: which %s, Value %g', which('comp3'), c.Value);
    delete(c);
    cd(prev);
catch e
    cd(rt);
    say('mlapp class: FAILED %s | %s', e.identifier, strtok(e.message, newline));
end

%% 3 Run as App Designer runs a component
say('== 3 run');
try
    clear comp3
    runner = appdesigner.internal.apprun.UserComponentRunner.instance();
    rc = runner.run(fullfile(rt, 'comp3.m'));
    p = rc.Parent;
    say('run: comp class %s, parent class %s, parent Position [%s], comp Position [%s], Visible %s', class(rc), class(p), num2str(p.Position), num2str(rc.Position), p.Visible);
    say('run: IsRunningInAppDesigner %d (%s), RunningInstanceFullFileName %s', isprop(p, 'IsRunningInAppDesigner'), class(p.IsRunningInAppDesigner), p.RunningInstanceFullFileName);
    say('run: RunningAppInstance prop %d', isprop(p, 'RunningAppInstance'));
    % the instantiator's resize of the auto-created parent
    p.Position = [p.Position(1) p.Position(2) rc.Position(1) + rc.Position(3) rc.Position(2) + rc.Position(4)];
    say('run: parent resized to [%s]', num2str(p.Position));
    closer = appdesigner.internal.appcloser.RunningInstanceCloserFactory.createCloser('CustomUIComponent', rc);
    say('run: closer class %s', class(closer));
    closer.closeRunningInstance();
    say('run: after close comp valid %d, parent valid %d', isvalid(rc), isvalid(p));
catch e
    say('run: FAILED %s | %s', e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
end

%% 4 configureMetadata and removeMetadata
say('== 4 metadata');
refuse(@() appdesigner.customcomponent.configureMetadata(), 'no args');
refuse(@() appdesigner.customcomponent.configureMetadata(1), 'number');
refuse(@() appdesigner.customcomponent.configureMetadata({'comp1.m'}), 'cell');
refuse(@() appdesigner.customcomponent.configureMetadata('comp1.txt'), 'txt');
refuse(@() appdesigner.customcomponent.configureMetadata('nosuch.m'), 'missing');
refuse(@() appdesigner.customcomponent.configureMetadata('nosuch'), 'missing no ext');
refuse(@() appdesigner.customcomponent.configureMetadata('for'), 'keyword');
refuse(@() appdesigner.customcomponent.configureMetadata('1bad.m'), 'bad name');
refuse(@() appdesigner.customcomponent.configureMetadata(fullfile(rt, 'nofolder', 'x.m')), 'bad folder');
fid = fopen(fullfile(rt, 'plainfn.m'), 'w'); fprintf(fid, 'function plainfn\nend\n'); fclose(fid);
fid = fopen(fullfile(rt, 'PlainClass.m'), 'w'); fprintf(fid, 'classdef PlainClass < handle\nend\n'); fclose(fid);
fid = fopen(fullfile(rt, 'Broken.m'), 'w'); fprintf(fid, 'classdef Broken < matlab.ui.componentcontainer.ComponentContainer\n  methods (Access = protected)\n    function setup(comp)\n      error(''setup failed'');\n    end\n    function update(comp)\n    end\n  end\nend\n'); fclose(fid);
fid = fopen(fullfile(rt, 'Abstr.m'), 'w'); fprintf(fid, 'classdef Abstr < matlab.ui.componentcontainer.ComponentContainer\nend\n'); fclose(fid);
rehash;
refuse(@() appdesigner.customcomponent.configureMetadata('plainfn.m'), 'function file');
refuse(@() appdesigner.customcomponent.configureMetadata('PlainClass.m'), 'plain class');
refuse(@() appdesigner.customcomponent.removeMetadata('PlainClass.m'), 'remove plain class');
refuse(@() appdesigner.customcomponent.removeMetadata('nosuch.m'), 'remove missing');
refuse(@() appdesigner.customcomponent.removeMetadata(), 'remove no args');
refuse(@() appdesigner.customcomponent.removeMetadata('comp1.m', 2), 'remove two args');
% removeMetadata with no resources folder: silent?
try
    appdesigner.customcomponent.removeMetadata('comp1.m');
    say('removeMetadata comp1 (no json): ok, resources folder exists %d', isfolder(fullfile(rt, 'resources')));
catch e
    say('removeMetadata comp1 (no json): %s | %s', e.identifier, strtok(e.message, newline));
end
% an invalid json
mkdir(fullfile(rt, 'resources'));
fid = fopen(fullfile(rt, 'resources', 'appDesigner.json'), 'w'); fprintf(fid, '{not json'); fclose(fid);
refuse(@() appdesigner.customcomponent.removeMetadata('comp1.m'), 'remove with invalid json');
try
    m = appdesigner.internal.usercomponent.metadata.Model(fullfile(rt, 'comp1.m'));
    say('model over invalid json: valid %d', m.getModelValidity());
catch e
    say('model over invalid json: %s | %s', e.identifier, strtok(e.message, newline));
end
delete(fullfile(rt, 'resources', 'appDesigner.json'));
% the headless call on a valid component: what comes up
try
    before = findall(groot, 'Type', 'figure');
    appdesigner.customcomponent.configureMetadata('comp1.m');
    after = findall(groot, 'Type', 'figure');
    fresh = setdiff(after, before);
    say('configureMetadata comp1 headless: returned; new figures %d', numel(fresh));
    for f = fresh'
        say('  figure Name "%s", Visible %s, Position [%s]', f.Name, f.Visible, num2str(f.Position));
    end
    delete(fresh);
catch e
    say('configureMetadata comp1 headless: %s | %s', e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
end
% the view model's validations and the register path for comp3 (the model, minus the window)
try
    model = appdesigner.internal.usercomponent.metadata.Model(fullfile(rt, 'comp3.m'));
    payload = struct('Metadata', model.getComponentMetadata(), 'Categories', {model.getCategories()}, 'Directory', model.getDirectory(), 'ModelValidity', model.getModelValidity(), 'FilePath', fullfile(rt, 'comp3.m'));
    vm = appdesigner.internal.usercomponent.metadata.MetadataUIViewModel(payload);
    md = vm.getMetadata();
    say('comp3 defaults: description "%s", category %s, version %s, icon %s', md.description, md.category, md.version, md.icon(max(1, end-30):end));
    say('validate: name "" %d, name " " %d, name "x" %d; category "" %d; version "1" %d "1.0" %d "1.0.1" %d "a.b" %d; email "" %d "a@b" %d "a@b.cd" %d', ...
        vm.validateComponentName(''), vm.validateComponentName(' '), vm.validateComponentName('x'), vm.validateComponentCatgory(''), ...
        vm.validateComponentVersion('1'), vm.validateComponentVersion('1.0'), vm.validateComponentVersion('1.0.1'), vm.validateComponentVersion('a.b'), ...
        vm.validateAuthorEmail(''), vm.validateAuthorEmail('a@b'), vm.validateAuthorEmail('a@b.cd'));
    [short, full] = vm.getFilePath();
    say('file path shown "%s" (full %d chars)', short, numel(full));
catch e
    say('view model: %s | %s', e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
end
% register comp3 as ds7 did (the register path without its window), then the json
try
    model = appdesigner.internal.usercomponent.metadata.Model(fullfile(rt, 'comp3.m'));
    md = model.getComponentMetadata();
    md = rmfield(md, 'status');
    md.componentName = 'Counter';
    md.category = 'Widgets';
    md.description = sprintf('Line one\nLine two');
    md.icon = 'data:image/png;base64,iVBORw0KGgo=';
    md.authorName = 'Pat';
    md.authorEmail = 'pat@example.com';
    md.version = '2.3';
    md.avatar = '';
    md.avatarDark = '';
    md.defaultPosition = [1 1 320 240];
    model.registerComponent(md);
    say('registered comp3: json follows');
    printFile(fullfile(rt, 'resources', 'appDesigner.json'));
    % update the same class: fields merged
    model = appdesigner.internal.usercomponent.metadata.Model(fullfile(rt, 'comp3.m'));
    md2 = struct('className', 'comp3', 'componentName', 'Counter2', 'version', '2.4');
    model.updateComponent(md2);
    say('updated comp3: json follows');
    printFile(fullfile(rt, 'resources', 'appDesigner.json'));
    % the Broken and Abstr classes through the avatar path: getAvatar's errors
    for nm = {'Broken', 'Abstr', 'comp4'}
        try
            model2 = appdesigner.internal.usercomponent.metadata.Model(fullfile(rt, [nm{1} '.m']));
            payload = struct('Metadata', model2.getComponentMetadata(), 'Categories', {model2.getCategories()}, 'Directory', model2.getDirectory(), 'ModelValidity', model2.getModelValidity(), 'FilePath', fullfile(rt, [nm{1} '.m']));
            vm2 = appdesigner.internal.usercomponent.metadata.MetadataUIViewModel(payload);
            got = {};
            addlistener(vm2, 'RegistrationErrorEvent', @(src, evt) assignin('base', 'w15err', evt.ErrorMessage));
            addlistener(vm2, 'UpdateModelEvent', @(src, evt) assignin('base', 'w15upd', evt.Metadata));
            evalin('base', 'clear w15err w15upd');
            md3 = vm2.getMetadata(); % the view model took status out already
            lastwarn('');
            vm2.registerComponent(md3);
            if evalin('base', 'exist(''w15err'', ''var'')')
                say('register %s: error "%s"', nm{1}, strrep(evalin('base', 'w15err'), newline, ' / '));
            elseif evalin('base', 'exist(''w15upd'', ''var'')')
                u = evalin('base', 'w15upd');
                say('register %s: UpdateModelEvent, defaultPosition [%s], warning "%s"', nm{1}, num2str(u.defaultPosition), strtok(lastwarn, newline));
            else
                say('register %s: nothing raised', nm{1});
            end
        catch e
            say('register %s: FAILED %s | %s', nm{1}, e.identifier, strtok(e.message, newline));
        end
    end
    % remove both
    appdesigner.customcomponent.removeMetadata(fullfile(rt, 'comp3.m'));
    say('removed comp3: json follows');
    printFile(fullfile(rt, 'resources', 'appDesigner.json'));
catch e
    say('register: FAILED %s | %s', e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
end
try
    say('validated mlapp: %s', appdesigner.internal.usercomponent.metadata.getValidatedCustomComponentFile(fullfile(ctl, 'comp3.mlapp')));
    say('validated mlapp no ext: %s', appdesigner.internal.usercomponent.metadata.getValidatedCustomComponentFile(fullfile(ctl, 'comp3')));
catch e
    say('validated mlapp: %s | %s', e.identifier, strtok(e.message, newline));
end

%% 5 the property dialog's validation
say('== 5 property validation');
cases = {
    'double', '0', 'Value', {}, {}
    'double', '', 'Value', {}, {}
    '', '', 'Value', {}, {}
    '', '5', 'Value', {}, {}
    '', '''abc''', 'Name', {}, {}
    'double', 'abc', 'Value', {}, {}
    'double', '''abc''', 'Value', {}, {}
    'double', '5', 'Value', {}, {'mustBePositive(Value)'}
    'double', '-5', 'Value', {}, {'mustBePositive(Value)'}
    'double', '5', 'Value', {}, {'mustBeLessThan(Value, 3)'}
    'double', '[1 2]', 'Value', {'1', '2'}, {}
    'double', '[1 2 3]', 'Value', {'1', '2'}, {}
    'double', '7', 'Value', {':', '1'}, {}
    'char', '''hi''', 'Text', {}, {}
    'char', '"hi"', 'Text', {}, {}
    'string', '"hi"', 'Text', {}, {}
    'string', '''hi''', 'Text', {}, {}
    'logical', 'true', 'Flag', {}, {}
    'logical', '1', 'Flag', {}, {}
    'logical', '2', 'Flag', {}, {}
    'cell', '{''a'', ''b''}', 'Items', {}, {}
    'cell', '{''a''; ''b''}', 'Items', {}, {}
    'cell', '{''a'', ''b''; ''c'', ''d''}', 'Items', {}, {}
    'int32', '3', 'Count', {}, {}
    'single', '2.5', 'Count', {}, {}
    'datetime', 'datetime(2020,1,1)', 'When', {}, {}
    'double', '1:3', 'Value', {'1', ':'}, {}
    'double', '0.1', 'Value', {}, {'mustBeInRange(Value, 0, 1)'}
    'matlab.lang.OnOffSwitchState', '''on''', 'State', {}, {}
    'double', '[]', 'Value', {}, {}
    'struct', 'struct(''a'', 1)', 'S', {}, {}
    'double', '1e3', 'Value', {}, {}
    'double', 'pi', 'Value', {}, {}
    'double', '2+3', 'Value', {}, {}
    'string', '["a" "b"]', 'Text', {}, {}
    'char', '[''ab''; ''cd'']', 'Text', {}, {}
    'double', '1', 'Value', {'1'}, {}
    'double', 'true', 'Value', {}, {}
    'logical', 'false', 'Flag', {'1', '1'}, {'mustBeNumericOrLogical(Flag)'}
    };
for i = 1:size(cases, 1)
    info = struct('ClassName', cases{i, 1}, 'DefaultValue', cases{i, 2}, 'Name', cases{i, 3}, 'Size', {cases{i, 4}}, 'ValidationFunctions', {cases{i, 5}});
    label = sprintf('case %d [%s | %s | size {%s} | %s]', i, cases{i, 1}, cases{i, 2}, strjoin(cases{i, 4}, ','), strjoin(cases{i, 5}, ','));
    try
        [cn, dv, idv, r] = appdesigner.internal.usercomponent.validateAndInferPropertyDetails(info);
        rs = '';
        if isstruct(r)
            rs = sprintf('%s', strjoin(cellfun(@(f) sprintf('%s=%s', f, shortText(r.(f))), fieldnames(r)', 'UniformOutput', false), ';'));
        end
        say('%s -> class "%s", default "%s", inferred %s (%s %s), renderer {%s}', label, cn, shortText(dv), shortText(idv), class(idv), mat2str(size(idv)), rs);
    catch e
        say('%s -> ERROR %s | %s', label, e.identifier, strrep(strtrim(e.message), newline, ' / '));
    end
end
% the inspector's edit of a default value
icases = {
    'double', 0, '5', 'Value', {}, {}
    'double', 0, 'abc', 'Value', {}, {}
    'double', 0, '[1 2]', 'Value', {}, {}
    'logical', true, false, 'Flag', {}, {}
    'char', 'hi', 'there', 'Text', {}, {}
    'string', "hi", 'there', 'Text', {}, {}
    'cell', {'a'}, {'x', 'y'}, 'Items', {}, {}
    'double', 5, '-1', 'Value', {}, {'mustBePositive(Value)'}
    };
for i = 1:size(icases, 1)
    info = struct('ClassName', icases{i, 1}, 'InferredDefaultValue', icases{i, 2}, 'UserEnteredValue', icases{i, 3}, 'Name', icases{i, 4}, 'Size', {icases{i, 5}}, 'ValidationFunctions', {icases{i, 6}});
    label = sprintf('inspector %d [%s | %s -> %s]', i, icases{i, 1}, shortText(icases{i, 2}), shortText(icases{i, 3}));
    try
        [cn, dv, idv, ~] = appdesigner.internal.usercomponent.validateInspectorPropertyValue(info);
        say('%s -> class "%s", default "%s", inferred %s (%s)', label, cn, shortText(dv), shortText(idv), class(idv));
    catch e
        say('%s -> ERROR %s | %s', label, e.identifier, strrep(strtrim(e.message), newline, ' / '));
    end
end
delete(fig);
say('w15_component done');
end

function say(varargin)
fprintf('%s\n', sprintf(varargin{:}));
end

function refuse(f, label)
try
    f();
    fprintf('%s: no error\n', label);
catch e
    try
        msg = strrep(strtrim(e.message), newline, ' / ');
    catch inner
        msg = ['<message unreadable: ' inner.message '>'];
    end
    fprintf('%s: %s | %s\n', label, e.identifier, msg);
end
end

function printFile(file)
if ~isfile(file), fprintf('  (no file %s)\n', file); return; end
t = fileread(file);
fprintf('  %d chars, CR %d, trailing newline %d\n', numel(t), sum(t == 13), ~isempty(t) && t(end) == 10);
lines = splitlines(t);
for i = 1:numel(lines), fprintf('  json| %s\n', lines{i}); end
end

function describe(label, v, depth)
if nargin < 3, depth = 0; end
pad = repmat(' ', 1, 2 * depth + 2);
if isstruct(v)
    fprintf('%s%s: struct %s fields %s\n', pad, label, mat2str(size(v)), strjoin(fieldnames(v)', ','));
    for k = 1:numel(v)
        fn = fieldnames(v);
        for j = 1:numel(fn)
            describe(sprintf('%s(%d).%s', label, k, fn{j}), v(k).(fn{j}), depth + 1);
        end
    end
elseif iscell(v)
    fprintf('%s%s: cell %s\n', pad, label, mat2str(size(v)));
    for k = 1:numel(v)
        describe(sprintf('%s{%d}', label, k), v{k}, depth + 1);
    end
else
    fprintf('%s%s: %s %s = %s\n', pad, label, class(v), mat2str(size(v)), shortText(v));
end
end

function s = shortText(v)
try
    if ischar(v)
        s = ['''' strrep(v, newline, '\n') ''''];
    elseif isstring(v)
        s = ['"' char(strjoin(v(:)', '|')) '"'];
    elseif isnumeric(v) || islogical(v)
        s = mat2str(v);
    elseif iscell(v)
        s = ['{' strjoin(cellfun(@shortText, v(:)', 'UniformOutput', false), ', ') '}'];
    elseif isstruct(v)
        s = ['struct(' strjoin(fieldnames(v)', ',') ')'];
    else
        s = class(v);
    end
catch
    s = class(v);
end
if numel(s) > 120, s = [s(1:117) '...']; end
end

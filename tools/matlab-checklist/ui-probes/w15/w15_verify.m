function w15_verify
% W15 (App Designer window plan): the .mlapp JGraph's designer writes for a custom UI component, read in
% R2025b as App Designer reads one - loadApp's CodeData (its AppTypeData against the control's of
% w15_component section 2), the metadata, and the class constructed from the package. The file is
% rt/jgraph/comp3.mlapp, written by ComponentAuthoringTests.AComponentSavesAsAnMlappAndOpensAgain with
% JGRAPH_W15_OUT naming that folder; the control is rt/w15/ctl/comp3.mlapp from the probe.
beep('off');
here = fileparts(mfilename('fullpath'));
jg = fullfile(here, 'rt', 'jgraph', 'comp3.mlapp');
ctl = fullfile(here, 'rt', 'w15', 'ctl', 'comp3.mlapp');
say('release %s', version('-release'));
if ~isfile(jg)
    say('no JGraph file at %s', jg);
    return;
end
try
    lastwarn('');
    [outcome, data] = appdesigner.internal.application.loadApp(jg);
    say('loadApp: status %s, warning "%s"', string(outcome.Status), lastwarn);
    say('loadApp: code fields %s', strjoin(fieldnames(data.CodeData)', ','));
    md = data.AppMetaData;
    say('loadApp: AppType %s, Min %s, Name %s', md.AppType, md.MinimumSupportedMATLABRelease, md.Name);
    say('loaded AppTypeData json: %s', jsonencode(data.CodeData.AppTypeData));
    say('loaded Callbacks: %s', jsonencode(data.CodeData.Callbacks));
    say('loaded StartupFcn: %s', jsonencode(data.CodeData.StartupFcn));
    say('loaded EditableSectionCode: %s', jsonencode(data.CodeData.EditableSectionCode));
    roots = fieldnames(data.ComponentData);
    say('loadApp: ComponentData fields %s', strjoin(roots', ','));
    f = data.ComponentData.(roots{1});
    say('loadApp: figure class %s, CodeName %s, children %s', class(f), f.DesignTimeProperties.CodeName, strjoin(arrayfun(@(h) class(h), allchild(f), 'UniformOutput', false)', ','));
    say('loadApp: figure ComponentCode: %s', strjoin(f.DesignTimeProperties.ComponentCode', ' | '));
    kids = allchild(f);
    for k = 1:numel(kids)
        say('  child %s CodeName %s code: %s', class(kids(k)), kids(k).DesignTimeProperties.CodeName, strjoin(kids(k).DesignTimeProperties.ComponentCode', ' | '));
    end
    delete(f);
catch e
    say('loadApp: FAILED %s | %s', e.identifier, strrep(getReport(e, 'basic'), newline, ' / '));
end
if isfile(ctl)
    try
        [~, control] = appdesigner.internal.application.loadApp(ctl);
        a = jsonencode(data.CodeData.AppTypeData);
        b = jsonencode(control.CodeData.AppTypeData);
        say('AppTypeData equals control: %d', strcmp(a, b));
        if ~strcmp(a, b)
            say('  control: %s', b);
        end
        say('class text equals control: %d', strcmp(data.CodeData.ClassName, control.CodeData.ClassName));
        croots = fieldnames(control.ComponentData);
        delete(control.ComponentData.(croots{1}));
    catch e
        say('control compare: FAILED %s | %s', e.identifier, strtok(e.message, newline));
    end
end
% the saved .mlapp runs as a component class
try
    prev = cd(fileparts(jg));
    clear comp3
    fig = uifigure('Visible', 'off');
    c = comp3('Parent', fig);
    say('mlapp class: which %s, Position [%s], Value %g, events %s', which('comp3'), num2str(c.Position), c.Value, strjoin(events(c)', ','));
    c.Value = 7; drawnow;
    say('mlapp class: Value set to %g and update run (the button is private)', c.Value);
    delete(fig);
    cd(prev);
catch e
    say('mlapp class: FAILED %s | %s', e.identifier, strtok(e.message, newline));
end
end

function say(varargin)
fprintf([varargin{1} '\n'], varargin{2:end});
end

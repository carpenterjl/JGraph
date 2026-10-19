function [fig, why] = ds1b_reference(text, pathToMLAPP, appType)
% DS1b (designer plan): the tree an app's createComponents makes in R2025b, as App Designer would
% hold it. The method's lines are run one by one in a headless figure (a callback's
% createCallbackFcn gives its method's name, as App Designer holds one at design time), and each
% component is then given the DesignTimeProperties App Designer gives it: its code name, an empty
% group, its image's path relative to the app where the code names one, and its lines as R2025b's
% generator makes them (light theme, the app's type), which is what App Designer stores. Returns
% [] and the reason when the method makes a component that is not a uifigure one.
fig = []; why = '';
% the app's own folder is where a file it names without a path is found
previous = cd(pathToMLAPP);
restore = onCleanup(@() cd(previous));
adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
lines = splitlines(strrep(text, sprintf('\r\n'), newline));
first = find(~cellfun(@isempty, regexp(lines, '^\s*function createComponents\(app\)')), 1);
indent = regexp(lines{first}, '^\s*', 'match', 'once');
last = first + find(strcmp(lines(first+1:end), [indent 'end']), 1);
body = strtrim(lines(first+1:last-1));
app = struct();
names = {};
images = struct();
for i = 1:numel(body)
    s = body{i};
    if isempty(s) || startsWith(s, '%') || startsWith(s, 'pathToMLAPP =') ...
            || (i == numel(body) && ~isempty(regexp(s, '^app\.\w+\.Visible = ''on'';$', 'once')))
        continue
    end
    made = regexp(s, '^app\.(\w+) = (\w+)\(', 'tokens', 'once');
    if ~isempty(made)
        if ~startsWith(made{2}, 'ui'), why = ['not a uifigure component: ' made{2}]; return; end
        names{end+1} = made{1}; %#ok<AGROW>
    end
    image = regexp(s, '^app\.(\w+)\.(?:ImageSource|Icon) = fullfile\(pathToMLAPP, (.*)\);$', 'tokens', 'once');
    if ~isempty(image)
        images.(image{1}) = strjoin(eval(['{' image{2} '}']), '/');
    end
    eval(s);
end
for i = 1:numel(names)
    h = app.(names{i});
    addprop(h, 'DesignTimeProperties');
    d = struct('CodeName', names{i}, 'GroupId', '', 'ComponentCode', {{}});
    if isfield(images, names{i}), d.ImageRelativePath = images.(names{i}); end
    h.DesignTimeProperties = d;
end
fig = app.(names{1});
if strcmp(appType, 'Responsive')
    % A responsive app's grid and its regions carry the flag App Designer's template gives them
    % (ResponsiveAppTemplateData), which no line of code sets.
    grid = findobj(fig.Children, 'flat', 'Type', 'uigridlayout');
    for h = [grid; findobj(grid.Children, 'flat', 'Type', 'uipanel')]'
        h.DesignTimeProperties.AppTypeProperties = struct('IsResponsiveContainer', true);
    end
end
for i = 1:numel(names)
    h = app.(names{i});
    code = gen.getComponentGenerationCode(h, eval(adapters(class(h))), appType, 'light');
    h.DesignTimeProperties.ComponentCode = code(:);
end
end

function name = createCallbackFcn(~, f, ~)
    % At design time App Designer holds a callback as its method's name.
    name = func2str(f);
end

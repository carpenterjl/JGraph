% DS1 (designer plan): records what R2025b's component code generator writes for the designer's
% fixture app (tests/JGraph.Tests/Scripting/Designer/DesignerSpecApp.m, written in this project,
% not by App Designer). Runs the fixture's createComponents lines one by one in a headless figure
% (a callback's createCallbackFcn gives its method name, as App Designer holds one at design time),
% gives each component the DesignTimeProperties App Designer would (its code name, and an image's
% path relative to the app), then runs the generator as App Designer's DesignTimeController does,
% with the light theme, and finishes each line as App Designer's code model does. Writes
% DesignerSpecApp.generated.txt beside the fixture, one "component <TAB> line" per line.
here = fileparts(mfilename('fullpath'));
folder = fullfile(here, '..', '..', '..', '..', 'tests', 'JGraph.Tests', 'Scripting', 'Designer');
text = fileread(fullfile(folder, 'DesignerSpecApp.m'));
lines = splitlines(strrep(text, sprintf('\r\n'), newline));
first = find(~cellfun(@isempty, regexp(lines, '^\s*function createComponents\(app\)')), 1);
indent = regexp(lines{first}, '^\s*', 'match', 'once');
last = first + find(strcmp(lines(first+1:end), [indent 'end']), 1);
body = strtrim(lines(first+1:last-1));

% an image beside the app, for the fixture's fullfile(pathToMLAPP, 'img', 'logo.png')
pathToMLAPP = fullfile(here, 'rt', 'spec');
if ~isfolder(fullfile(pathToMLAPP, 'img')), mkdir(fullfile(pathToMLAPP, 'img')); end
imwrite(uint8(255 * rand(8, 8, 3)), fullfile(pathToMLAPP, 'img', 'logo.png'));

app = struct();
names = {};
images = struct();
for i = 1:numel(body)
    s = body{i};
    if isempty(s) || startsWith(s, '%') || startsWith(s, 'pathToMLAPP =') || strcmp(s, 'app.UIFigure.Visible = ''on'';')
        continue
    end
    made = regexp(s, '^app\.(\w+) = ui\w+\(', 'tokens', 'once');
    if ~isempty(made), names{end+1} = made{1}; end %#ok<SAGROW>
    image = regexp(s, '^app\.(\w+)\.(?:ImageSource|Icon) = fullfile\(pathToMLAPP, (.*)\);$', 'tokens', 'once');
    if ~isempty(image)
        images.(image{1}) = strjoin(eval(['{' image{2} '}']), '/');
    end
    eval(s);
end

adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
for i = 1:numel(names)
    h = app.(names{i});
    p = addprop(h, 'DesignTimeProperties'); %#ok<NASGU>
    d = struct('CodeName', names{i}, 'GroupId', '', 'ComponentCode', {{}});
    if isfield(images, names{i}), d.ImageRelativePath = images.(names{i}); end
    h.DesignTimeProperties = d;
end

fid = fopen(fullfile(folder, 'DesignerSpecApp.generated.txt'), 'w', 'n', 'UTF-8');
cleanup = onCleanup(@() fclose(fid));
total = 0;
for i = 1:numel(names)
    h = app.(names{i});
    adapter = eval(adapters(class(h)));
    code = gen.getComponentGenerationCode(h, adapter, 'Standard', 'light');
    parent = '';
    if isprop(h.Parent, 'DesignTimeProperties'), parent = h.Parent.DesignTimeProperties.CodeName; end
    code = strrep(strrep(code(:), 'ad_CODENAME_ad', names{i}), 'ad_PARENTCODENAME_ad', parent);
    for k = 1:numel(code)
        m = regexp(code{k}, '^app\.\w+\.(\w+) = ''(\w+)'';$', 'tokens', 'once');
        if ~isempty(m) && (endsWith(m{1}, 'Fcn') || endsWith(m{1}, 'Callback'))
            code{k} = regexprep(code{k}, '= ''(\w+)'';$', '= createCallbackFcn(app, @$1, true);');
        end
    end
    if isfield(images, names{i})
        rel = strrep(images.(names{i}), '/', ''', ''');
        which = 'Icon'; if isprop(h, 'ImageSource'), which = 'ImageSource'; end
        line = sprintf('app.%s.%s = fullfile(pathToMLAPP, ''%s'');', names{i}, which, rel);
        at = find(contains(code, '.ImageSource =') | contains(code, '.Icon ='), 1);
        if isempty(at), code{end+1} = line; else, code{at} = line; end %#ok<AGROW>
    end
    for k = 1:numel(code)
        fprintf(fid, '%s\t%s\n', names{i}, code{k});
    end
    total = total + numel(code);
    fprintf('%-20s %-42s %2d lines\n', names{i}, class(h), numel(code));
end
fprintf('%d components, %d lines\n', numel(names), total);

function name = createCallbackFcn(~, f, ~)
    % At design time App Designer holds a callback as its method's name.
    name = func2str(f);
end

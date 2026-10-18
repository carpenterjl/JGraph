% DS1 (designer plan): the oracle for JGraph's createComponents writer. Runs R2025b's component
% code generator (ComponentCodeGenerator over the adapters, as App Designer's DesignTimeController
% calls it: with the app's type and its figure's theme) over every canvas component of the apps
% R2025b ships, finishes each line as App Designer's code model does (a callback's quoted name
% becomes createCallbackFcn, an image beside the app becomes fullfile(pathToMLAPP, ...)), and writes
% them to rt/ds1_oracle.txt, one "app <TAB> component <TAB> line" per line, the app as its path
% under toolbox/ with forward slashes. rt/ is ignored: the
% lines are MathWorks' and stay on this machine, read by the local test
% AppDesignerDesignModelTests. Only counts are printed. Headless.
files = dir(fullfile(matlabroot, 'toolbox', '**', '*.mlapp'));
adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
if ~isfolder('rt'), mkdir('rt'); end
fid = fopen(fullfile('rt', 'ds1_oracle.txt'), 'w', 'n', 'UTF-8');
cleanup = onCleanup(@() fclose(fid));
tally = containers.Map();
for k = 1:numel(files)
    fn = fullfile(files(k).folder, files(k).name);
    try
        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
        meta = des.getAppMetadata();
    catch e
        bump(tally, ['load error ' e.identifier]); continue
    end
    appType = ''; if isfield(meta, 'AppType'), appType = meta.AppType; end
    fig = a.components.UIFigure;
    theme = '';
    if isprop(fig, 'Theme') && ~isempty(fig.Theme), theme = fig.Theme.BaseColorStyle; end
    bump(tally, sprintf('theme %s', theme));
    comps = findall(fig, '-property', 'DesignTimeProperties');
    for c = 1:numel(comps)
        obj = comps(c);
        cls = class(obj);
        if ~isKey(adapters, cls), bump(tally, ['no adapter ' cls]); continue; end
        try
            adapter = eval(adapters(cls));
            code = gen.getComponentGenerationCode(obj, adapter, appType, theme);
        catch e
            bump(tally, ['generator error ' e.identifier]); continue
        end
        name = obj.DesignTimeProperties.CodeName;
        parent = '';
        if isprop(obj.Parent, 'DesignTimeProperties'), parent = obj.Parent.DesignTimeProperties.CodeName; end
        code = strrep(strrep(code(:), 'ad_CODENAME_ad', name), 'ad_PARENTCODENAME_ad', parent);
        code = regexprep(code, '^(app\.\w+\.\w+) = ''(\w+)'';$', '$1 = createCallbackFcn(app, @$2, true);', 'once');
        % the code model keeps a non-callback property's quoted value: undo the rewrite for those
        for i = 1:numel(code)
            m = regexp(code{i}, '^app\.\w+\.(\w+) = createCallbackFcn', 'tokens', 'once');
            if ~isempty(m) && ~(endsWith(m{1}, 'Fcn') || endsWith(m{1}, 'Callback'))
                code{i} = regexprep(code{i}, '= createCallbackFcn\(app, @(\w+), true\);$', '= ''$1'';');
            end
        end
        if isfield(obj.DesignTimeProperties, 'ImageRelativePath') && ~isempty(obj.DesignTimeProperties.ImageRelativePath)
            rel = strjoin(strsplit(obj.DesignTimeProperties.ImageRelativePath, '/'), ''', ''');
            which = 'Icon'; if isprop(obj, 'ImageSource'), which = 'ImageSource'; end
            line = sprintf('app.%s.%s = fullfile(pathToMLAPP, ''%s'');', name, which, rel);
            at = find(contains(code, '.ImageSource =') | contains(code, '.Icon ='), 1);
            if isempty(at), code{end+1} = line; else, code{at} = line; end %#ok<AGROW>
            bump(tally, 'image beside the app');
        end
        key = strrep(erase(fn, [fullfile(matlabroot, 'toolbox') filesep]), filesep, '/');
        for i = 1:numel(code)
            fprintf(fid, '%s\t%s\t%s\n', key, name, code{i});
        end
        bump(tally, 'components');
    end
    delete(fig);
end
k = keys(tally); v = cell2mat(values(tally));
for i = 1:numel(k), fprintf('%5d  %s\n', v(i), k{i}); end

function bump(m, key)
    if isKey(m, key), m(key) = m(key) + 1; else, m(key) = 1; end
end

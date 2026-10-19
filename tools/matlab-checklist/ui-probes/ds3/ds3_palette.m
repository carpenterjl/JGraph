% DS3 (designer plan): what a component App Designer makes for its palette holds before the
% palette's entry names it and places it. For each class the catalog knows (ds1_catalog.json), asks
% the class's adapter for its design-time component (createDesignTimeComponentWithCleanup: the
% component in the parent App Designer makes it in, with the adapter's design-time values - a
% panel's size and title, a table's columns, an axes' labels) and runs R2025b's generator over it
% with the light theme, as ds1_generate does. Records each class's generated lines (code name
% ad_CODENAME_ad) and the component's Position. Headless. Writes ds3_palette.json.
catalog = jsondecode(fileread(fullfile(pwd, '..', 'ds1', 'ds1_catalog.json')));
adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
gen = appdesigner.internal.codegeneration.ComponentCodeGenerator();
rows = {};
for k = 1:numel(catalog.classes)
    c = catalog.classes(k);
    if iscell(catalog.classes), c = catalog.classes{k}; end
    row = struct('type', c.type, 'position', [], 'lines', {{}}, 'error', '');
    try
        adapter = eval(adapters(c.type));
        [obj, cleanups] = adapter.createDesignTimeComponentWithCleanup(); %#ok<ASGLU>
        if isprop(obj, 'Position'), row.position = obj.Position; end
        addprop(obj, 'DesignTimeProperties');
        obj.DesignTimeProperties = struct('CodeName', 'ad_CODENAME_ad', 'GroupId', '', 'ComponentCode', {{}});
        code = gen.getComponentGenerationCode(obj, adapter, 'Standard', 'light');
        row.lines = cellstr(code(:));
        clear cleanups
    catch e
        row.error = [e.identifier ': ' e.message];
    end
    rows{end + 1} = row; %#ok<SAGROW>
end
fid = fopen(fullfile(pwd, 'ds3_palette.json'), 'w', 'n', 'UTF-8');
fwrite(fid, jsonencode(struct('release', version('-release'), 'classes', {rows}), 'PrettyPrint', true));
fclose(fid);
for k = 1:numel(rows)
    r = rows{k};
    fprintf('%-42s %-22s %s\n', r.type, mat2str(r.position), r.error);
    for i = 1:numel(r.lines)
        fprintf('    %s\n', r.lines{i});
    end
end

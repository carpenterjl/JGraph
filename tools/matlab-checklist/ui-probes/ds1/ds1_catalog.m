% DS1 (designer plan): records what JGraph's createComponents writer needs from App Designer's
% component adapters, per uifigure component class, into ds1_catalog.json:
%   - the creation call (getCodeGenCreation, parent written as %PARENT%);
%   - the generated property order (getCodeGenPropertyNames on a design-time component);
%   - which of those have a Mode sibling (a manual mode is what makes them written);
%   - each property's run-time default under the 'unthemed', 'light' and 'dark' themes
%     (getComponentRunTimeDefaults), the values isDefault compares against; a themed default is
%     written only where it differs from the unthemed one.
% Values are written as {c: class, s: size, v: data}; Inf and NaN as strings. Headless. Only the
% base-MATLAB classes (matlab.ui.*, less matlab.ui.scope from a toolbox).
adapters = appdesigner.internal.appmetadata.getProductionComponentAdapterMap();
types = sort(keys(adapters));
themes = {'unthemed', 'light', 'dark'};
out = struct('release', version('-release'), 'classes', {{}});
for k = 1:numel(types)
    type = types{k};
    if ~startsWith(type, 'matlab.ui.') || startsWith(type, 'matlab.ui.scope.'), continue; end
    entry = struct('type', type, 'adapter', adapters(type));
    try
        adapter = eval(adapters(type));
    catch e
        fprintf('%s: adapter error %s\n', type, e.identifier); continue
    end
    creation = adapter.getCodeGenCreation([], 'ad_CODENAME_ad', '%PARENT%');
    entry.creation = creation;
    entry.valueProperty = adapter.ValueProperty;
    entry.orderSpecific = adapter.OrderSpecificProperties;
    try
        [h, cleanups] = adapter.createDesignTimeComponentWithCleanup(); %#ok<ASGLU>
    catch e
        fprintf('%s: design-time component error %s\n', type, e.identifier); continue
    end
    names = adapter.getCodeGenPropertyNames(h);
    defaults = cell(1, numel(themes));
    for t = 1:numel(themes)
        try
            defaults{t} = adapter.getComponentRunTimeDefaults(themes{t});
        catch e
            fprintf('%s: defaults error (%s) %s\n', type, themes{t}, e.identifier);
            defaults{t} = struct();
        end
    end
    props = cell(1, numel(names));
    for i = 1:numel(names)
        p = struct('name', names{i}, 'mode', isprop(h, [names{i} 'Mode']));
        d = struct();
        for t = 1:numel(themes)
            if ~isfield(defaults{t}, names{i}), continue; end
            v = defaults{t}.(names{i});
            if t > 1 && isfield(defaults{1}, names{i}) && isequal(v, defaults{1}.(names{i})), continue; end
            d.(themes{t}) = enc(v);
        end
        p.default = d;
        props{i} = p;
    end
    entry.properties = props;
    out.classes{end+1} = entry;
    fprintf('%-45s %3d properties  %s\n', type, numel(names), creation);
    clear cleanups h
end
fid = fopen('ds1_catalog.json', 'w', 'n', 'UTF-8');
fwrite(fid, jsonencode(out), 'char');
fclose(fid);
fprintf('%d classes written\n', numel(out.classes));

function e = enc(v)
    % A value as {c, s, v}: data column-major, non-finite numbers as strings.
    e = struct('c', class(v), 's', size(v));
    if isa(v, 'matlab.lang.OnOffSwitchState')
        e.c = 'onoff'; e.v = char(v);
    elseif isnumeric(v) || islogical(v)
        x = double(v(:))';
        c = num2cell(x);
        for i = 1:numel(x)
            if isnan(x(i)), c{i} = 'NaN';
            elseif isinf(x(i)), c{i} = sprintf('%sInf', repmat('-', 1, x(i) < 0));
            end
        end
        e.v = c;
    elseif ischar(v)
        e.v = cellstr(v);
    elseif isstring(v)
        e.v = cellstr(v(:)');
    elseif iscell(v)
        e.v = cellfun(@enc, v(:)', 'UniformOutput', false);
    elseif isdatetime(v)
        c = cell(1, numel(v));
        for i = 1:numel(v)
            if isnat(v(i)), c{i} = 'NaT'; else, c{i} = [v(i).Year v(i).Month v(i).Day]; end
        end
        e.v = c;
    elseif isa(v, 'function_handle')
        e.v = func2str(v);
    elseif isenum(v)
        e.c = 'enum'; e.class = class(v); e.v = cellstr(char(v));
    else
        e.c = 'object'; e.class = class(v);
    end
end

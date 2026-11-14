function oi147_verify(varargin)
% Open item 147: R2025b reading an .mlapp whose version 7.3 model JGraph rewrote with a changed
% component tree. Each argument is an .mlapp, optionally 'file|CodeName|Prop' to print one
% component property. Prints one line per check.
for k = 1:numel(varargin)
    spec = strsplit(varargin{k}, '|');
    fn = spec{1};
    [folder, base, ext] = fileparts(fn);
    [~, parent] = fileparts(folder);
    label = [parent '/' base ext];
    try
        parts = fullfile(tempname, 'parts');
        unzip(fn, parts);
        model = fullfile(parts, 'appdesigner', 'appModel.mat');
        fid = fopen(model, 'r'); head = fread(fid, [1 19], 'uint8=>char'); fseek(fid, 0, 'eof'); bytes = ftell(fid); fseek(fid, 512 + 40, 'bof'); eof = fread(fid, 1, 'uint64'); fclose(fid);
        fprintf('%s | header %s | %d bytes | stored eof %d\n', label, head, bytes, eof);
        w = whos('-file', model);
        fprintf('%s | whos %s\n', label, strjoin(sort(arrayfun(@(v) sprintf('%s(%s)', v.name, v.class), w, 'UniformOutput', false))', ' '));
        lastwarn('');
        S = load(model);
        [msg, id] = lastwarn;
        fprintf('%s | load %s; figure %s with %d children; warning [%s] %s\n', label, strjoin(sort(fieldnames(S))', ','), class(S.components.UIFigure), numel(S.components.UIFigure.Children), id, msg);
        all = findall(S.components.UIFigure);
        fprintf('%s | load: %d graphics objects\n', label, numel(all));
        delete(S.components.UIFigure);
        info = h5info(model);
        fprintf('%s | h5 groups %s | datasets %s\n', label, strjoin(sort({info.Groups.Name}), ','), strjoin(sort({info.Datasets.Name}), ','));
        rmdir(fileparts(parts), 's');

        des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
        a = des.getAppData();
        warns = {};
        if ~isempty(des.Warnings), warns = arrayfun(@(x) char(string(x.Id)), des.Warnings, 'UniformOutput', false); end
        objs = findall(a.components.UIFigure);
        names = {};
        for o = objs'
            if isprop(o, 'DesignTimeProperties') && ~isempty(o.DesignTimeProperties) && isprop(o.DesignTimeProperties, 'CodeName')
                names{end + 1} = char(o.DesignTimeProperties.CodeName); %#ok<AGROW>
            end
        end
        fprintf('%s | getAppData: %d objects, %d code names, warnings [%s], callbacks %d\n', label, numel(objs), numel(names), strjoin(warns, ','), numel(a.code.Callbacks));
        if numel(spec) >= 3
            hit = [];
            for o = objs'
                if isprop(o, 'DesignTimeProperties') && ~isempty(o.DesignTimeProperties) && isprop(o.DesignTimeProperties, 'CodeName') && strcmp(o.DesignTimeProperties.CodeName, spec{2})
                    hit = o;
                end
            end
            if isempty(hit)
                % a tree without App Designer's records (the U7b sample): find by the property's value
                fprintf('%s | %s not found by code name\n', label, spec{2});
                for o = objs'
                    if isprop(o, spec{3}), fprintf('%s | %s %s = %s\n', label, class(o), spec{3}, char(string(o.(spec{3})))); end
                end
            else
                fprintf('%s | %s.%s = %s\n', label, spec{2}, spec{3}, char(string(hit.(spec{3}))));
            end
        end
        delete(a.components.UIFigure);
    catch e
        fprintf('%s | ERROR %s | %s\n', label, e.identifier, strrep(e.message, newline, ' '));
    end
end
end

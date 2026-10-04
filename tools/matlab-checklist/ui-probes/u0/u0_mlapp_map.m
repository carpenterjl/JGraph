% U0: how the classdef text of every shipped .mlapp maps onto appModel.mat's `code` variable.
files = dir(fullfile(matlabroot,'toolbox','**','*.mlapp'));
fprintf('%d shipped .mlapp files\n', numel(files));
nOk = 0; problems = {};
for k = 1:numel(files)
    fn = fullfile(files(k).folder, files(k).name);
    try
        txt = matlab.internal.getCode(fn);
        txt = strrep(char(txt), sprintf('\r\n'), newline);
        r = appdesigner.internal.serialization.FileReader(fn);
        c = r.readAppCodeData();
    catch e
        problems{end+1} = sprintf('%s: read error %s', files(k).name, e.message); %#ok<SAGROW>
        continue
    end
    lines = splitlines(txt);
    why = {};
    % class name
    if ~startsWith(strtrim(lines{1}), ['classdef ' c.ClassName]), why{end+1} = 'ClassName not on line 1'; end
    % callbacks: body lines verbatim, directly after "function Name(app, event)"
    if ~isfield(c,'Callbacks'), c.Callbacks = struct('Name',{},'Code',{}); end
    for j = 1:numel(c.Callbacks)
        cb = c.Callbacks(j);
        at = find(~cellfun(@isempty, regexp(lines, ['^\s*function\s+' cb.Name '\s*\(app'], 'once')));
        if numel(at) ~= 1, why{end+1} = sprintf('callback %s signature found %d times', cb.Name, numel(at)); continue; end %#ok<AGROW>
        body = lines(at+1 : at+numel(cb.Code));
        if ~isequal(body(:)', cb.Code(:)'), why{end+1} = sprintf('callback %s body differs', cb.Name); end %#ok<AGROW>
        if ~strcmp(strtrim(lines{at+numel(cb.Code)+1}), 'end'), why{end+1} = sprintf('callback %s not followed by end', cb.Name); end %#ok<AGROW>
    end
    % startup
    if isfield(c,'StartupCallback') && ~isempty(c.StartupCallback) && isfield(c.StartupCallback,'Name') && ~isempty(c.StartupCallback.Name)
        sc = c.StartupCallback;
        at = find(~cellfun(@isempty, regexp(lines, ['^\s*function\s+' sc.Name '\s*\(app'], 'once')));
        if numel(at) ~= 1
            why{end+1} = 'startup signature not unique';
        else
            sig = strtrim(lines{at});
            want = ['function ' sc.Name '(app'];
            ip = ''; if isfield(c,'InputParameters'), ip = c.InputParameters; end
            if ~isempty(ip), want = [want ', ' ip]; end
            if ~strcmp(sig, [want ')']), why{end+1} = sprintf('startup signature "%s" vs InputParameters "%s"', sig, ip); end %#ok<AGROW>
            if ~isequal(lines(at+1:at+numel(sc.Code))', sc.Code(:)'), why{end+1} = 'startup body differs'; end
        end
    end
    % editable section: contiguous run of lines
    esc = {}; if isfield(c,'EditableSectionCode'), esc = c.EditableSectionCode(:)'; end
    fns = fieldnames(c)'; fprintf('  %-40s fields: %s\n', files(k).name, strjoin(fns, ','));
    if ~isempty(esc)
        hit = 0;
        for s = 1:numel(lines)-numel(esc)+1
            if isequal(lines(s:s+numel(esc)-1)', esc), hit = s; break; end
        end
        if hit == 0, why{end+1} = 'EditableSectionCode not a contiguous run'; %#ok<AGROW>
        elseif k <= 3
            fprintf('  %s: EditableSection at lines %d-%d; line before: "%s"; line after: "%s"\n', files(k).name, hit, hit+numel(esc)-1, lines{max(hit-1,1)}, lines{min(hit+numel(esc), numel(lines))});
        end
    end
    if isempty(why), nOk = nOk + 1; else, problems{end+1} = sprintf('%s: %s', files(k).name, strjoin(why, '; ')); end %#ok<SAGROW>
end
fprintf('%d of %d map exactly\n', nOk, numel(files));
fprintf('%s\n', problems{:});
% One app's region skeleton, for the plan
fn = fullfile(matlabroot,'toolbox','comm','comm','+comm','+internal','+bertool','DataExport.mlapp');
txt = splitlines(strrep(char(matlab.internal.getCode(fn)), sprintf('\r\n'), newline));
fprintf('--- DataExport.mlapp skeleton (lines with keywords or comments) ---\n');
for i = 1:numel(txt)
    L = txt{i};
    if ~isempty(regexp(L, '^\s*(classdef|properties|methods|function|events|enumeration|%)', 'once')) || strcmp(strtrim(L),'end') && numel(L) - numel(strtrim(L)) <= 4
        fprintf('%4d: %s\n', i, L);
    end
end
c = appdesigner.internal.serialization.FileReader(fn).readAppCodeData();
fprintf('AppTypeData: %s\n', jsonencode(c.AppTypeData));

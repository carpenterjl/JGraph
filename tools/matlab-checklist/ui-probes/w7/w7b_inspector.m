% W7b (App Designer window plan): what R2025b's inspector client is handed for each component class
% - the same JSON the InspectorFactory publishes to the client (getProxyViewMapJSON), read from its
% registration cache: for each class the sheet's properties (name, display name, class name,
% tooltip) and its groups with their nested items. Headless; no figure is made. Writes
% w7b_inspector_raw.json (the AppDesigner application only, one row per class).
f = internal.matlab.inspector.peer.InspectorFactory.getInstance();
raw = jsondecode(f.getProxyViewMapJSON());
if ~iscell(raw), raw = num2cell(raw); end
rows = {};
for k = 1:numel(raw)
    r = raw{k};
    if strcmp(r.application, 'AppDesigner')
        rows{end + 1} = struct('className', r.className, 'defaults', r.defaults); %#ok<SAGROW>
    end
end
fid = fopen(fullfile(pwd, 'w7b_inspector_raw.json'), 'w', 'n', 'UTF-8');
fwrite(fid, jsonencode(struct('release', version('-release'), 'classes', {rows})));
fclose(fid);
fprintf('%d AppDesigner classes of %d\n', numel(rows), numel(raw));

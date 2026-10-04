% U0 save-back round trip, part C: does R2025b accept the spliced file, and see every edit?
fn = fullfile(pwd,'rt','out','DataExport.mlapp');
r = appdesigner.internal.serialization.FileReader(fn);
c = r.readAppCodeData();
k = find(strcmp({c.Callbacks.Name}, 'CancelButtonPushed'));
fprintf('callback edit present: %d\n', strcmp(c.Callbacks(k).Code{1}, '            disp(''edited-by-jgraph'');'));
fprintf('editable-section edit present: %d\n', any(contains(c.EditableSectionCode, 'JGraphEdited')));
fprintf('startup edit present: %d\n', strcmp(c.StartupCallback.Code{1}, '      disp(''startup-edited'');'));
txt = char(matlab.internal.getCode(fn));
fprintf('runnable text has all three edits: %d\n', contains(txt,'edited-by-jgraph') && contains(txt,'JGraphEdited') && contains(txt,'startup-edited'));
try
    d = r.readAppDesignerData();
    fprintf('readAppDesignerData ok: fields %s\n', strjoin(fieldnames(d)', ','));
catch e
    fprintf('readAppDesignerData ERROR %s | %s\n', e.identifier, e.message);
end
try
    m = r.readAppMetadata(); fprintf('readAppMetadata ok: %s\n', class(m));
catch e
    fprintf('readAppMetadata ERROR %s | %s\n', e.identifier, e.message);
end
% App Designer's own full load (what opening the file in App Designer does first), headless.
try
    des = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn);
    fprintf('deserializer: %s\n', class(des));
    appData = des.getAppData();
    fprintf('getAppData ok: %s\n', class(appData));
catch e
    fprintf('deserializer ERROR %s | %s\n', e.identifier, e.message);
end
addpath(fullfile(pwd,'rt','out'));
fprintf('which DataExport: %s\n', which('DataExport'));
mc = meta.class.fromName('DataExport');
fprintf('meta.class has JGraphEdited: %d\n', any(strcmp({mc.PropertyList.Name}, 'JGraphEdited')));

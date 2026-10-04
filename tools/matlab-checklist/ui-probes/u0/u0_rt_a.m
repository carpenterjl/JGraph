% U0 save-back round trip, part A: produce the edited code text and an uncompressed `code` variable.
src = fullfile(matlabroot,'toolbox','comm','comm','+comm','+internal','+bertool','DataExport.mlapp');
if ~exist('rt','dir'), mkdir('rt'); end
copyfile(src, fullfile('rt','DataExport.mlapp'));
c = appdesigner.internal.serialization.FileReader(src).readAppCodeData();
txt = strrep(char(matlab.internal.getCode(src)), sprintf('\r\n'), newline);
lines = splitlines(txt);
% Edit 1: a callback body (CancelButtonPushed)
k = find(strcmp({c.Callbacks.Name}, 'CancelButtonPushed'));
at = find(~cellfun(@isempty, regexp(lines, '^\s*function\s+CancelButtonPushed\s*\(app', 'once')));
newLine = '            disp(''edited-by-jgraph'');';
lines = [lines(1:at); {newLine}; lines(at+1:end)];
c.Callbacks(k).Code = [{newLine}, c.Callbacks(k).Code(:)'];
% Edit 2: the editable section (a new private property inside the first user block)
esc = c.EditableSectionCode(:)';
for s = 1:numel(lines)-numel(esc)+1
    if isequal(lines(s:s+numel(esc)-1)', esc), break; end
end
propLine = '        JGraphEdited = 1 % added by the round-trip probe';
lines = [lines(1:s); {propLine}; lines(s+1:end)];
c.EditableSectionCode = [esc(1), {propLine}, esc(2:end)];
% Edit 3: the startup body
at = find(~cellfun(@isempty, regexp(lines, '^\s*function\s+startupFcn\s*\(app', 'once')));
stLine = '      disp(''startup-edited'');';
lines = [lines(1:at); {stLine}; lines(at+1:end)];
c.StartupCallback.Code = [{stLine}, c.StartupCallback.Code(:)'];
code = c; %#ok<NASGU>
save(fullfile('rt','code_v6.mat'), 'code', '-v6');
fid = fopen(fullfile('rt','document_text.m'), 'w', 'n', 'UTF-8'); fwrite(fid, strjoin(lines, newline), 'char'); fclose(fid);
fprintf('part A done: %d lines\n', numel(lines));

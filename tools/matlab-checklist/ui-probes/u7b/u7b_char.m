% U7b: how R2025b stores a char row with code units above 255 in a level-5 MAT-file, and what it
% reads back from the sample JGraph saved (rt/edited/sample), whose copy holds such a line.
if ~exist('rt', 'dir'), mkdir('rt'); end
s = char([65 233 20013 8364 66]); %#ok<NASGU>
t = 'plain'; %#ok<NASGU>
save(fullfile('rt', 'char_v6.mat'), 's', 't', '-v6');
b = fileread_bytes(fullfile('rt', 'char_v6.mat'));
fprintf('v6 bytes after the header: %s\n', sprintf('%02x ', b(129:end)));
q = load(fullfile('rt', 'char_v6.mat'));
fprintf('round trip: %s\n', mat2str(double(q.s)));

fn = fullfile(pwd, 'rt', 'edited', 'sample', 'U7bApp.mlapp');
z = unzip(fn, fullfile(pwd, 'rt', 'sample_parts'));
m = fullfile(pwd, 'rt', 'sample_parts', 'appdesigner', 'appModel.mat');
w = warning('off', 'all');
L = load(m, 'code');
warning(w);
line = L.code.EditableSectionCode{find(contains(L.code.EditableSectionCode, 'Extra = 7'), 1)};
fprintf('load of the model: %s\n', mat2str(double(line(end-6:end))));
c = appdesigner.internal.serialization.FileReader(fn).readAppCodeData();
line = c.EditableSectionCode{find(contains(c.EditableSectionCode, 'Extra = 7'), 1)};
fprintf('readAppCodeData: %s\n', mat2str(double(line(end-6:end))));
txt = char(matlab.internal.getCode(fn));
k = strfind(txt, 'Extra = 7');
seg = txt(k:k+60); seg = seg(1:find(seg == newline, 1) - 1);
fprintf('text line ends: %s\n', mat2str(double(seg(end-6:end))));

% A MAT-file JGraph's own `save` wrote with the same char row (the gated test leaves it).
j = load(fullfile(pwd, 'rt', 'edited', 'jgraph_char.mat'));
fprintf('JGraph-written MAT-file: %s %s\n', mat2str(double(j.s)), j.t);
fprintf('JGraph-written struct: fields %s; %g %s\n', strjoin(fieldnames(j.st)', ','), j.st.first, j.st.second);

function b = fileread_bytes(f)
    fid = fopen(f, 'r'); b = fread(fid, inf, 'uint8=>double')'; fclose(fid);
end

% u11_hgm - what the hgM variable of an R2025b .fig holds. Headless.
here = fileparts(mfilename('fullpath'));
out = fullfile(here, 'figs');
f = figure('Visible', 'off', 'Name', 'tiny', 'Color', [1 0 0]);
ax = axes(f);
plot(ax, [1 2 3], [4 5 6], 'g', 'Tag', 'L1');
savefig(f, fullfile(out, 'u11_tiny.fig'));
delete(f);
s = load(fullfile(out, 'u11_tiny.fig'), '-mat');
g = s.hgM_080000.GraphicsObjects;
fprintf('GraphicsObjects class %s\n', class(g));
mc = metaclass(g);
for p = mc.PropertyList'
    fprintf('  prop %s (Transient=%d Dependent=%d)\n', p.Name, p.Transient, p.Dependent);
end
w = warning('off', 'MATLAB:structOnObject');
st = struct(g);
warning(w);
fn = fieldnames(st);
for i = 1:numel(fn)
    v = st.(fn{i});
    fprintf('  field %s: %s %s\n', fn{i}, class(v), mat2str(size(v)));
end
if isfield(st, 'Format3Data')
    d = st.Format3Data;
    fprintf('Format3Data: %s %s\n', class(d), mat2str(size(d)));
    if isa(d, 'uint8')
        fid = fopen(fullfile(out, 'u11_tiny.f3d'), 'w'); fwrite(fid, d, 'uint8'); fclose(fid);
        fprintf('first bytes: %s\n', mat2str(d(1:min(64, end))'));
    elseif iscell(d)
        for i = 1:numel(d), fprintf('  cell %d: %s %s\n', i, class(d{i}), mat2str(size(d{i}))); end
    end
end
% the documented route back
h = hgload(fullfile(out, 'u11_tiny.fig'));
fprintf('hgload: %s, line color %s\n', class(h), mat2str(findobj(h, 'Tag', 'L1').Color));
delete(h);
% a string and a datetime in a plain MAT-file, for the subsystem layout
str = "hello"; strs = ["a" "bc"; "def" "g"]; dt = datetime(2026, 10, 7); %#ok<NASGU>
save(fullfile(out, 'u11_strings.mat'), 'str', 'strs');

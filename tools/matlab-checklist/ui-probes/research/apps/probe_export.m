% probe_export.m - GUIDE "Export to MATLAB file" without GUIDE, then run the exported single file.
here = pwd;
diary(fullfile(here, 'probe_export.out')); diary on
set(groot, 'DefaultFigureVisible', 'off');
gp = fullfile(here, 'guide_pair');
addpath(gp);
outDir = fullfile(here, 'guide_exported');
if ~isfolder(outDir), mkdir(outDir); end
out = fullfile(outDir, 'myguide_export.m');
try
    export.internal.exportGUIDEApp(fullfile(gp, 'myguide.fig'), out);
    fprintf('export ok: %d bytes\n', dir(out).bytes);
catch e
    fprintf('export err: %s | %s\n', e.identifier, e.message);
    for k = 1:numel(e.stack), fprintf('   at %s:%d\n', e.stack(k).name, e.stack(k).line); end
end
cd(here);
if isfile(out)
    rmpath(gp);
    addpath(outDir);
    try
        h = myguide_export('Visible', 'off');
        hs = guidata(h);
        fprintf('exported app runs: fields %s\n', strjoin(fieldnames(hs)', ', '));
        delete(h);
    catch e
        fprintf('exported run err: %s | %s\n', e.identifier, e.message);
    end
end
delete(findall(groot, 'Type', 'figure'));
diary off

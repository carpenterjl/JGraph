% w13_variants - which part of a toolbox file R2025b's reader objects to (stage W13's parity work), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w13_variants
% Reads every .mltbx in rt\jgraph\variants with mlAddonGetProperties and toolboxVersion and prints the
% refusal's report where there is one. Nothing is installed.
here = fileparts(mfilename('fullpath'));
folder = fullfile(here, 'rt', 'jgraph', 'variants');
files = dir(fullfile(folder, '*.mltbx'));
for k = 1:numel(files)
    t = fullfile(files(k).folder, files(k).name);
    fprintf('== %s\n', files(k).name);
    try
        p = mlAddonGetProperties(t);
        fprintf('mlAddonGetProperties ok: %s %s %s\n', p.name, p.version, p.GUID);
    catch e
        fprintf('mlAddonGetProperties -> %s: %s\n', e.identifier, strrep(e.message, newline, ' / '));
        for c = 1:numel(e.cause), fprintf('   cause: %s\n', strrep(e.cause{c}.message, newline, ' / ')); end
    end
    try
        fprintf('toolboxVersion: %s\n', matlab.addons.toolbox.toolboxVersion(t));
    catch e
        fprintf('toolboxVersion -> %s\n', e.identifier);
    end
    try
        r = matlab.internal.addons.metadata.MltbxMetadataReader(t);
        fprintf('MltbxMetadataReader ok\n');
    catch e
        fprintf('MltbxMetadataReader -> %s: %s\n', e.identifier, strrep(e.message, newline, ' / '));
    end
end
fprintf('done\n');

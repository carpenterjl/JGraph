% Stage K10's archive built by JGraph (compiler and SDK plan): what R2025b's C shared library
% k10lib carries, made from the same files. Until stage K11 builds C shared libraries, the .NET
% MATLAB Data API target (stage K9) makes it, writing the archive beside its assembly; run-k10.ps1
% appends it to k10lib.dll compiled from R2025b's generated k10lib.c. Run under jgraph -batch with
% K10_OUT naming the build folder, whose src holds the probe's sources. One key<TAB>value line.
out = getenv('K10_OUT');
src = fullfile(out, 'src');
cd(src);
listing = dir(fullfile(src, '*.m'));
names = sort({listing.name});
d = fullfile(out, 'lib');
mkdir(d);
try
    mcc('-W', 'dotnet:k10lib,api=matlab-data', '-T', 'link:lib', '-d', d, names{:});
    fprintf('build\tok\n');
catch ME
    fprintf('build\tERR %s | %s\n', ME.identifier, strrep(ME.message, newline, ' / '));
end

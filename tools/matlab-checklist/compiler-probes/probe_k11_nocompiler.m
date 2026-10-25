function probe_k11_nocompiler(work)
%PROBE_K11_NOCOMPILER Builds a C shared library where no C compiler can be found (compiler and SDK
%plan, stage K11), with mcc -W lib and compiler.build.cSharedLibrary, and prints the refusal.
%record-k11.ps1 runs it under matlab -batch -noFigureWindows with ProgramFiles(x86) pointed at an
%empty folder, so mbuild's search finds no Visual Studio; run-k11.ps1 runs it under jgraph -batch
%with JGraph's search pointed nowhere. The builds are not inside evalc, so whatever the compiler
%step prints before the refusal reaches the transcript. One key<TAB>value line per question.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
copyfile(fullfile(here, 'src', 'k11', 'k11plain.m'), work);
old = cd(work);
cleanup = onCleanup(@() cd(old));
d = fullfile(work, 'mcc');
mkdir(d);
lastwarn('', '');
fprintf('mcc\tstart\n');
try
    mcc('-W', 'lib:k11nc', '-T', 'link:lib', '-d', d, 'k11plain.m');
    fprintf('mcc\tok\n');
catch ME
    fprintf('mcc\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
k0_warned('mcc');
listing = dir(d);
fprintf('mcc.files\t%s\n', strjoin(sort({listing(~[listing.isdir]).name}), ' | '));
d = fullfile(work, 'codegen');
mkdir(d);
try
    mcc('-W', 'lib:k11cg', '-T', 'codegen', '-d', d, 'k11plain.m');
    fprintf('codegen\tok\n');
catch ME
    fprintf('codegen\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
listing = dir(d);
fprintf('codegen.files\t%s\n', strjoin(sort({listing(~[listing.isdir]).name}), ' | '));
d = fullfile(work, 'api');
fprintf('api\tstart\n');
try
    compiler.build.cSharedLibrary(fullfile(work, 'k11plain.m'), 'OutputDir', d);
    fprintf('api\tok\n');
catch ME
    fprintf('api\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
k0_warned('api');
if exist(d, 'dir')
    listing = dir(d);
    fprintf('api.files\t%s\n', strjoin(sort({listing(~[listing.isdir]).name}), ' | '));
end
end

% probe_dup.m - .m vs .mlapp precedence; an .mlapp need not derive from AppBase.
here = pwd;
diary(fullfile(here, 'probe_dup.out')); diary on
addpath(fullfile(here, 'build', 'dup'));
w = which('Same', '-all');
fprintf('which -all Same:\n'); fprintf('   %s\n', w{:});
fprintf('Same.Src = %s\n', Same.Src);
fprintf('exist Plain = %d, which = %s\n', exist('Plain'), which('Plain'));
p = Plain(); fprintf('class = %s\n', class(p));
try
    c = matlab.internal.getCode(which('Plain'));
    fprintf('getCode(Plain.mlapp) chars = %d\n', numel(c));
catch e, fprintf('getCode err %s\n', e.message); end
try
    type(which('Plain'));
catch e, fprintf('type err %s\n', e.message); end
diary off

function k0_path(varargin)
%K0_PATH What the path, the current folder and files outside the archive do in a deployed
%session (compiler and SDK plan, stage K0). The runner starts it in a scratch folder that holds
%k0_outside.m (a function not compiled in) and outside.txt.
k0_quiet;
p0 = path;
k0_try('pwd', @() pwd);
k0_try('exist.k0_outside', @() exist('k0_outside')); %#ok<EXIST>
k0_try('which.k0_outside', @() which('k0_outside'));
k0_try('call.k0_outside', @() feval('k0_outside'));
k0_try('exist.outside.txt', @() exist('outside.txt', 'file'));
k0_try('which.outside.txt', @() which('outside.txt'));
k0_try('fileread.outside.txt', @() strtrim(fileread('outside.txt')));
k0_try('fileread.inside.txt', @() strtrim(fileread('inside.txt')));
k0_try('fileread.ctfroot.inside.txt', @() strtrim(fileread(fullfile(ctfroot, 'k0_path', 'inside.txt'))));
k0_try('exist.inside.txt', @() exist('inside.txt', 'file'));
k0_try('which.inside.txt', @() which('inside.txt'));

here = pwd;
k0_do('addpath.here', @() addpath(here));
k0_try('path.changed.after.addpath', @() ~strcmp(path, p0));
k0_try('which.k0_outside.after.addpath', @() which('k0_outside'));
k0_try('call.k0_outside.after.addpath', @() feval('k0_outside'));
k0_do('rmpath.here', @() rmpath(here));
p1 = path;
parts = strsplit(p1, pathsep);
k0_do('rmpath.first', @() rmpath(parts{1}));
k0_try('path.changed.after.rmpath', @() ~strcmp(path, p1));
path(p1);
k0_do('path.set', @() path(p1, tempdir));
k0_try('path.changed.after.path2', @() ~strcmp(path, p1));
path(p1);
k0_do('cd.tempdir', @() cd(tempdir));
k0_try('pwd.after.cd', @() pwd);
k0_try('which.k0_show.after.cd', @() which('k0_show'));
k0_do('cd.back', @() cd(here));
k0_try('call.run.outside_script', @() evalc('run(''outside_script.m'')'));
k0_try('call.run.inside_script', @() evalc('k0_inside_script'));
k0_try('call.eval.defines', @() eval('k0_q = 7; k0_q * 2'));
k0_try('call.str2func.outside', @() func2str(str2func('k0_outside')));
k0_try('call.str2func.outside.call', @() feval(str2func('k0_outside')));
k0_try('call.feval.k0_show', @() feval('k0_show', 1));
k0_try('exist.k0_inside_helper', @() exist('k0_inside_helper')); %#ok<EXIST>
k0_try('call.feval.k0_inside_helper', @() feval('k0_inside_helper'));
k0_try('ls.ctfroot', @() sort(cellstr(ls(fullfile(ctfroot, 'k0_path')))'));
end

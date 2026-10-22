function k0_env(varargin)
%K0_ENV What a deployed session answers about itself, and what the functions R2025b's runtime
%leaves out do when called (compiler and SDK plan, stage K0).
k0_quiet;
fprintf('nargin\t%d\n', nargin);
for k = 1:nargin, fprintf('arg%d\t%s\n', k, k0_show(varargin{k})); end

k0_try('isdeployed', @() isdeployed);
k0_try('ismcc', @() ismcc);
k0_try('ctfroot', @() ctfroot);
k0_try('mcrversion.2', @() mcrv2());
k0_try('mcrversion.3', @() mcrv3());
k0_try('mcrcachedir', @() mcrcachedir);
k0_try('matlabroot', @() matlabroot);
k0_try('prefdir', @() prefdir);
k0_try('userpath', @() userpath);
k0_try('tempdir', @() tempdir);
k0_try('pwd', @() pwd);
k0_try('mfilename', @() mfilename);
k0_try('mfilename.fullpath', @() mfilename('fullpath'));
k0_try('version', @() version);
k0_try('version.release', @() version('-release'));
k0_try('ver.matlab', @() verfield());
k0_try('computer', @() computer);
k0_try('usejava.jvm', @() usejava('jvm'));
k0_try('usejava.desktop', @() usejava('desktop'));
k0_try('license', @() license);
k0_try('license.test.MATLAB', @() license('test', 'MATLAB'));
k0_try('license.test.Signal_Toolbox', @() license('test', 'Signal_Toolbox'));
k0_try('feature.numcores', @() feature('numcores'));
k0_try('maxNumCompThreads', @() maxNumCompThreads);
k0_try('getenv.MCR_CACHE_ROOT', @() getenv('MCR_CACHE_ROOT'));
k0_try('path.count', @() numel(strsplit(path, pathsep)));
k0_try('path.first', @() firstPath(6));
k0_try('getmcruserdata.ParallelProfile', @() getmcruserdata('ParallelProfile'));
k0_do('setmcruserdata', @() setmcruserdata('k0key', 'k0value'));
k0_try('getmcruserdata.k0key', @() getmcruserdata('k0key'));
k0_try('getmcruserdata.missing', @() getmcruserdata('nosuchkey'));

names = {'k0_env', 'k0_show', 'data.mat', 'edit', 'help', 'doc', 'lookfor', 'dbstop', 'dbstack', ...
    'keyboard', 'input', 'profile', 'publish', 'checkcode', 'pcode', 'mex', 'savepath', 'more', ...
    'echo', 'commandwindow', 'clc', 'home', 'guide', 'inspect', 'deployprint', 'isstandalone', ...
    'deploypcode', 'mcrinstaller', 'figure', 'uifigure', 'exportgraphics', 'jsondecode', ...
    'getSecret', 'setSecret', 'restoredefaultpath', 'rehash', 'inmem', 'mlock', 'run', 'open', ...
    'type', 'which', 'waitforallfiguresclosed', 'mainFuncForNumericStandaloneApplication'};
for k = 1:numel(names)
    n = names{k};
    k0_try(['exist.' n], @() exist(n)); %#ok<EXIST>
    k0_try(['which.' n], @() which(n));
end

k0_do('call.edit', @() edit('k0_nosuch'));
k0_do('call.help', @() help('sin'));
k0_try('call.help.out', @() help('sin'));
k0_do('call.doc', @() doc('sin'));
k0_try('call.lookfor', @() evalc('lookfor sine'));
k0_do('call.dbstop', @() dbstop('in', 'k0_show'));
k0_try('call.dbstack', @() numel(dbstack));
k0_do('call.profile', @() profile('on'));
k0_do('call.pcode', @() pcode('k0_show'));
k0_do('call.mex', @() mex('-help'));
k0_do('call.savepath', @() savepath);
k0_do('call.more', @() more('on'));
k0_do('call.echo', @() echo('on'));
% echo on really echoes here: every later line of library code would be printed.
k0_try('call.echo.off.output', @() evalc('echo(''off'')'));
k0_do('call.commandwindow', @() commandwindow);
k0_do('call.checkcode', @() checkcode('k0_show'));
k0_do('call.publish', @() publish('k0_show'));
k0_do('call.restoredefaultpath', @() restoredefaultpath);
k0_do('call.rehash', @() rehash);
k0_try('call.inmem', @() numel(inmem));
k0_do('call.mlock', @() mlock);
k0_do('call.clc', @() clc);
k0_do('call.home', @() home);
k0_try('call.evalc.clc', @() evalc('clc'));
k0_try('call.type', @() evalc('type k0_show'));
k0_do('call.getSecret', @() getSecret('k0nosuch'));
k0_do('call.setSecret', @() setSecret('k0nosuch'));
k0_try('call.eval', @() eval('1+2'));
k0_try('call.evalc.disp', @() evalc('disp(42)'));
k0_try('call.str2func', @() feval(str2func('@(x) x + 1'), 1));
k0_try('call.evalin.base', @() evalin('base', '3*3'));
k0_do('call.assignin.base', @() assignin('base', 'k0v', 4));
k0_try('call.evalin.base.k0v', @() evalin('base', 'k0v'));
end

function v = mcrv2()
[a, b] = mcrversion;
v = [a b];
end

function v = mcrv3()
[a, b, c] = mcrversion;
v = [a b c];
end

function v = verfield()
s = ver('MATLAB');
v = {s.Name, s.Version, s.Release};
end

function v = firstPath(n)
p = strsplit(path, pathsep);
v = p(1:min(n, numel(p)));
end

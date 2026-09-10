cd(fileparts(mfilename('fullpath')));
addpath(fullfile('..','lib'));
disp(['A max({1,2}) = ' num2str(max({1,2}))]);
disp(['B mean([1 2 3]) = ' num2str(mean([1 2 3]))]);
w = which('mean','-all'); disp('C which -all mean:'); disp(w(1:min(3,end)));
disp(['D linspace = ' num2str(linspace(0,1,3))]);
w = which('linspace','-all'); disp('E which -all linspace:'); disp(w(1:min(2,end)));
try; disp(['F builtin(''mean'') = ' num2str(builtin('mean',[1 2 3]))]); catch e; disp(['F builtin mean err: ' e.message]); end
disp(['G builtin() with cur/builtin.m present: ' num2str(builtin('max',[1 5 3]))]);
disp(['H which builtin: ' which('builtin')]);
f = @max; disp(['I handle max on cell = ' num2str(f({1}))]);
try; callsecret(); disp('J lib saw private (unexpected)'); catch e; disp(['J lib cannot see private: ' e.identifier]); end
disp(['K numel local = ' num2str(numel([1 2 3]))]);
disp(['L numel builtin = ' num2str(builtin('numel',[1 2 3]))]);
disp(['M exist mean = ' num2str(exist('mean')) ' exist numel = ' num2str(exist('numel'))]);
disp(['N exist mean file = ' num2str(exist('mean','file')) ' exist mean builtin = ' num2str(exist('mean','builtin'))]);
disp(['O which numel: ' which('numel')]);
disp(['P feval mean = ' num2str(feval('mean',[1 2 3])) ' feval numel = ' num2str(feval('numel',[1 2 3]))]);
disp(['Q str2func mean = ' num2str(feval(str2func('mean'),[1 2 3]))]);
disp(['R exist for a var named max: ' num2str(exist('max'))]);
x = 3; disp(['S exist x = ' num2str(exist('x'))]);
disp(['T max([1 5 3]) with numel local & max.m in cur = ' num2str(max([1 5 3]))]);
disp(['U sum([1 2 3]) = ' num2str(sum([1 2 3]))]);
function n = numel(x)
n = -444;
end

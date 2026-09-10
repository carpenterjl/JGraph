cd(fileparts(mfilename('fullpath')));
disp(['A max([1 5 3]) = ' num2str(max([1 5 3]))]);
disp(['B builtin max = ' num2str(builtin('max',[1 5 3]))]);
disp(['C which max: ' which('max')]);
w = which('max','-all'); disp(['D which -all count = ' num2str(numel(w))]); disp(w);
disp(['E exist max = ' num2str(exist('max'))]);
disp(['F exist max builtin = ' num2str(exist('max','builtin'))]);
addpath(fullfile('..','lib'));
disp(['G helper (cur vs lib, lib added front) = ' helper()]);
disp(['H max after addpath lib = ' num2str(max([1 5 3]))]);
disp(['I usesecret = ' usesecret()]);
try; secret(); disp('J secret visible from script (unexpected)'); catch e; disp(['J secret hidden: ' e.identifier]); end
ascript; disp(['K ascript ran: exist zz = ' num2str(exist('zz','var'))]);
t = Thing(3); disp(['L size(t) = ' size(t)]); disp(['M size([1 2]) = ' mat2str(size([1 2]))]);
disp(['N localshadow = ' num2str(localshadow())]);
max = 7; disp(['O variable max: ' num2str(max)]); try; max([1 5 3]); catch e; disp(['P index error: ' e.identifier]); end
clear max; disp(['Q after clear max = ' num2str(max([1 5 3]))]);
builtin = 5; disp(['R builtin as variable = ' num2str(builtin)]); clear builtin;
disp(['S feval max = ' num2str(feval('max',[1 5 3]))]);
f = @max; disp(['T handle max = ' num2str(f([1 5 3]))]); disp(['U func2str = ' func2str(f)]);
import pkg.imp; disp(['V imported imp = ' imp()]);
disp(['W exist helper = ' num2str(exist('helper'))]);
disp(['X which helper: ' which('helper')]);
disp(['Y builtin sum = ' num2str(builtin('sum',[1 2 3]))]);
try; builtin('helper'); catch e; disp(['Z builtin helper: ' e.message]); end

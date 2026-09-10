% Where does evalin resolve function names: the target's file, the evaluating function's file, or neither?
root = fileparts(fileparts(mfilename('fullpath')));
addpath(fullfile(root, 'lib'));
v = 'probe0c-v';
on = ObjNo();
r = pf2();
disp(r);
fprintf('--- from a script run by name (worker2 in wdir)\n');
addpath(fullfile(root, 'wdir'));
worker2
disp(w2);
fprintf('--- evalin from an anonymous body context via g2\n');
f = maker2();
disp(f(1));
fprintf('=== done\n');

function r = helper(x)
r = sprintf('probe0c-helper(%d)', x);
end

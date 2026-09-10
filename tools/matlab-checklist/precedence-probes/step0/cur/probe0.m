% Step-0 probes still owed by the plan. Run from cur/ with lib/ and wdir/ on the path.
root = fileparts(fileparts(mfilename('fullpath')));
addpath(fullfile(root, 'lib'));
addpath(fullfile(root, 'wdir'));
v = 'probe0-v';
function_results = {};
say = @(tag, val) fprintf('%s => %s\n', tag, show(val));

fprintf('=== A. @sum on user objects (sum.m in cur returns -555; Obj has sum, ObjNo does not)\n');
o = Obj(); on = ObjNo();
hs = @sum;
try, say('sum(o)', sum(o)); catch e, say('sum(o)', e.message); end
try, say('sum(1,o)', sum(1,o)); catch e, say('sum(1,o)', e.message); end
try, say('sum(on)', sum(on)); catch e, say('sum(on)', e.message); end
try, say('sum(1,on)', sum(1,on)); catch e, say('sum(1,on)', e.message); end
try, say('sum([1 2])', sum([1 2])); catch e, say('sum([1 2])', e.message); end
try, say('hs(o)', hs(o)); catch e, say('hs(o)', e.message); end
try, say('hs(1,o)', hs(1,o)); catch e, say('hs(1,o)', e.message); end
try, say('hs(on)', hs(on)); catch e, say('hs(on)', e.message); end
try, say('hs(1,on)', hs(1,on)); catch e, say('hs(1,on)', e.message); end
try, say('hs([1 2])', hs([1 2])); catch e, say('hs([1 2])', e.message); end
try, say('cellfun(hs,{o,on,[1 2]})', cellfun(hs, {o, on, [1 2]}, 'UniformOutput', false)); catch e, say('cellfun(hs,...)', e.message); end
try, say('feval(''sum'',o)', feval('sum', o)); catch e, say('feval(''sum'',o)', e.message); end
try, say('feval(''sum'',on)', feval('sum', on)); catch e, say('feval(''sum'',on)', e.message); end
try, say('feval(hs,on)', feval(hs, on)); catch e, say('feval(hs,on)', e.message); end
cd(fullfile(root, 'lib'));
try, say('after cd lib: hs(on)', hs(on)); catch e, say('after cd lib: hs(on)', e.message); end
try, say('after cd lib: sum(on)', sum(on)); catch e, say('after cd lib: sum(on)', e.message); end
cd(fullfile(root, 'cur'));

fprintf('\n=== B. nested handle escaping its parent\n');
h1 = nestmaker(); h2 = nestmaker();
say('h1() h1() h2()', [h1() h1() h2()]);
fi = functions(h1); say('functions(h1).type/function', {fi.type, fi.function});
clear nestmaker
say('after clear nestmaker: h1()', h1());

fprintf('\n=== C. evalin(''caller'') from a path function, caller is this script (local helper here)\n');
say('pf() from probe0', pf());
say('pfp() from probe0 (private phelp)', pfp());
worker
say('worker: pf()', worker_pf);
say('worker: pfp()', worker_pfp);
say('worker: helper(1) direct', worker_helper);
say('worker: phelp() direct', worker_phelp);
say('probe0 after worker: helper(1)', helper(1));

fprintf('\n=== D. g through an escaped @(x) g(x, v) from maker.m (maker local helper, maker v)\n');
f = maker();
r = f(5);
say('g via f: evalin helper()', r.helper);
say('g via f: evalin v', r.v);
say('g via f: assignin seen', r.assign);
say('probe0 has seen?', exist('seen', 'var'));
r2 = g(2); say('g direct from probe0: helper', r2.helper); say('g direct: v', r2.v); say('probe0 seen now', exist('seen', 'var'));
say('g direct: dbstack from anon', r.stack);

fprintf('\n=== E. g called from a classdef property default (DefClass local helper)\n');
try
    d = DefClass();
    say('DefClass.p.helper', d.p.helper);
    say('DefClass.p.v', d.p.v);
    say('DefClass.p.assign', d.p.assign);
    say('DefClass.p.stack', d.p.stack);
catch e
    say('DefClass()', e.message);
end

fprintf('\n=== F. globals under builtin names\n');
try
    global size
    size = [10 20 30];
    say('size(end)', size(end));
    say('size(:)''', size(:)');
    say('size(2:end)', size(2:end));
    size(2)
    say('numel via builtin(''size'',size)', builtin('size', size));
catch e
    say('global size', e.message);
end
try
    global disp
    disp = @(x) x * 2;
    say('disp(4)', disp(4));
    disp(5)
    y = disp(6); say('y = disp(6)', y);
catch e
    say('global disp', e.message);
end
fprintf('=== done\n');

function r = helper(x)
    r = sprintf('probe0-helper(%d)', x);
end

function s = show(v)
    if ischar(v), s = v;
    elseif isstring(v), s = char(v);
    elseif isnumeric(v) || islogical(v), s = mat2str(v);
    elseif iscell(v), s = ['{' strjoin(cellfun(@show, v, 'UniformOutput', false), ' | ') '}'];
    elseif isstruct(v), s = jsonencode(v);
    else, s = class(v);
    end
end

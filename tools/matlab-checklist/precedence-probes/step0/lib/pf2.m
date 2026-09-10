function r = pf2()
% Like pf, but this file has its own local helper and lib/ has its own private/phelp2.
r = struct();
try, r.helper = evalin('caller', 'helper(1)'); catch e, r.helper = ['ERR: ' e.message]; end
try, r.phelp = evalin('caller', 'phelp2()'); catch e, r.phelp = ['ERR: ' e.message]; end
try, r.v = evalin('caller', 'v'); catch e, r.v = ['ERR: ' e.message]; end
try, r.sumv = evalin('caller', 'sum([1 2])'); catch e, r.sumv = ['ERR: ' e.message]; end
try, r.sumon = evalin('caller', 'sum(on)'); catch e, r.sumon = ['ERR: ' e.message]; end
try, r.base_helper = evalin('base', 'helper(1)'); catch e, r.base_helper = ['ERR: ' e.message]; end
try, r.eval_helper = eval('helper(1)'); catch e, r.eval_helper = ['ERR: ' e.message]; end
try, r.feval_helper = feval('helper', 1); catch e, r.feval_helper = ['ERR: ' e.message]; end
try, r.str2func_helper = feval(str2func('helper'), 1); catch e, r.str2func_helper = ['ERR: ' e.message]; end
try, r.handle_helper = feval(@helper, 1); catch e, r.handle_helper = ['ERR: ' e.message]; end
end

function r = helper(x)
r = sprintf('pf2-local-helper(%d)', x);
end

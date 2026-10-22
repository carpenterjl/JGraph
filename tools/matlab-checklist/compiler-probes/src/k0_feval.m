function k0_feval(varargin)
%K0_FEVAL Call a function the dependency analysis cannot see (its name is only in a string).
%Built twice: k0_feval (no pragma; k0_target is not compiled in) and k0_fevalp, whose main
%k0_fevalp.m carries the %#function pragma.
k0_quiet;
name = ['k0_', 'target'];
k0_try('exist.k0_target', @() exist(name)); %#ok<EXIST>
k0_try('feval.k0_target', @() feval(name, 2));
k0_try('str2func.k0_target', @() feval(str2func(name), 3));
k0_try('eval.k0_target', @() eval([name '(4)']));
end

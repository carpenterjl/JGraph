% net_import.m -- import for .NET names (interop plan, stage 3). An import belongs to the code of
% the function it is written in, is resolved when that file is parsed (so a file importing System.*
% loads the default runtime before its first line runs), applies to the whole function wherever it
% is written, reaches nested and anonymous functions and eval, and does not reach local functions,
% callees or scripts. The imports are therefore in helper functions (helpers/ix_imp_*.m), called
% only after this script has chosen the runtime and loaded the assembly.

dotnetenv("core", Version="8");
p = interop_paths();
NET.addAssembly(p.assembly);

report('system', ix_imp_system());
forms = {'type', 'typewild', 'member', 'user', 'usertype', 'conflict', 'fncall', 'late', ...
    'branch', 'nested', 'anon', 'eval', 'callee'};
for k = 1:numel(forms)
    report(forms{k}, ix_imp_forms(forms{k}));
end
ix_chk('bad_type', ix_id(@() ix_imp_bad_type()));
ix_chk('var_clash', ix_id(@() ix_imp_var_clash()));
ix_chk('clear_in_function', ix_id(@() ix_imp_clear()));
ix_chk('nosuch_wildcard', ix_show(ix_imp_nosuch_wild()));
ix_chk('base_list_empty', ix_show(import));

function report(form, r)
for k = 1:size(r, 1)
    ix_chk([form '_' r{k, 1}], r{k, 2});
end
end

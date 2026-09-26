function ip_import_body()
% IP_IMPORT_BODY  The body of probe_net_import. An import belongs to the code of the file it is
%   written in, not to a workspace, so evalin strings cannot see it. Every case is therefore a
%   generated file whose checks are written out literally, one try/catch per check.
a = ip_assets();
NET.addAssembly(a.assembly);
root = tempname; mkdir(root);
addpath(root);
cleanup = onCleanup(@() ip_import_cleanup(root));

run_case(root, 'wild', {'import System.*'}, {
    'list',       'import'
    'Math',       'Math.Max(1, 2)'
    'String',     'class(String(''x''))'
    'nested.ns',  'class(Collections.ArrayList())'
    'DayOfWeek',  'DayOfWeek.Monday'
    });
run_case(root, 'type', {'import System.Math'}, {
    'Math',       'Math.Max(1, 2)'
    'bare',       'Max(1, 2)'
    });
run_case(root, 'typewild', {'import System.Math.*'}, {
    'Max',        'Max(1, 2)'
    'PI',         'PI'
    });
run_case(root, 'member', {'import System.Math.Max'}, {
    'Max',        'Max(1, 2)'
    });
run_case(root, 'user', {'import JGTest.*'}, {
    'Members',    'Members(3).Value'
    'Statics',    'Statics.Hello()'
    'plot',       'class(plot)'
    'Sum.Of',     'Sum.Of([1 2])'
    'sum',        'sum([1 2])'
    'max.Thing',  'max.Thing().Where()'
    'max.fn',     'max([1 3])'
    });
run_case(root, 'user.type', {'import JGTest.plot'}, {
    'plot',       'class(plot)'
    });
run_case(root, 'nosuch.wild', {'import No.Such.*'}, {
    'list',       'import'
    });
run_case(root, 'nosuch.type', {'import System.NoSuchType'}, {
    'list',       'import'
    });
run_case(root, 'conflict.wild', {'import System.Timers.*', 'import System.Threading.*'}, {
    'Timer',      'class(Timer(100))'
    'list',       'import'
    });
run_case(root, 'conflict.explicit', {'import System.Timers.Timer', 'import System.Threading.Timer'}, {
    'Timer',      'class(Timer(100))'
    });
run_case(root, 'vs.string.fn', {'import System.String'}, {
    'string',     'class(string(''x''))'
    'String',     'class(String(''x''))'
    });
run_case(root, 'function.form', {'import(''System.Math'')'}, {
    'Math',       'Math.Max(1, 2)'
    'list',       'import'
    });
run_case(root, 'return.value', {'L = import(''System.Math'');'}, {
    'L',          'L'
    'Math',       'Math.Max(1, 2)'
    });
run_case(root, 'clear.in.script', {'import System.Math', 'clear import'}, {
    'Math',       'Math.Max(1, 2)'
    });
run_case(root, 'var.over.import', {'import System.Math', 'Math = 3;'}, {
    'Math',       'Math'
    });
run_case(root, 'evalin', {'import System.Math'}, {
    'eval',       'eval(''Math.Max(1, 2)'')'
    'evalc.list', 'evalc(''disp(import)'')'
    });

% ---- functions
w = @(rel, text) ip_write(fullfile(root, rel), text);
w('ip_imp_fn.m', sprintf('function r = ip_imp_fn()\nimport System.Math.*\nr = Max(4, 5);\nend'));
w('ip_imp_fn_list.m', sprintf('function r = ip_imp_fn_list()\nimport System.*\nimport JGTest.Members\nr = import;\nend'));
w('ip_imp_fn_clear.m', sprintf('function r = ip_imp_fn_clear()\nimport System.Math.*\nclear import\nr = Max(4, 5);\nend'));
w('ip_imp_fn_late.m', sprintf('function r = ip_imp_fn_late()\nr = Max(4, 5);\nimport System.Math.*\nend'));
w('ip_imp_fn_branch.m', sprintf('function r = ip_imp_fn_branch(flag)\nif flag\n    import System.Math.*\nend\nr = Max(4, 5);\nend'));
w('ip_imp_fn_local.m', sprintf('function r = ip_imp_fn_local()\nimport System.Math.*\nr = helper();\nend\nfunction r = helper()\nr = Max(4, 5);\nend'));
w('ip_imp_fn_nested.m', sprintf('function r = ip_imp_fn_nested()\nimport System.Math.*\nr = inner();\n    function r = inner()\n        r = Max(4, 5);\n    end\nend'));
w('ip_imp_fn_anon.m', sprintf('function f = ip_imp_fn_anon()\nimport System.Math.*\nf = @() Max(4, 5);\nend'));
w('ip_imp_caller.m', sprintf('function r = ip_imp_caller()\nimport System.Math.*\nr = ip_imp_callee();\nend'));
w('ip_imp_callee.m', sprintf('function r = ip_imp_callee()\nr = import;\nend'));
w('ip_imp_calls_script.m', sprintf('function r = ip_imp_calls_script()\nimport System.Math.*\nip_imp_script_uses;\nr = script_result;\nend'));
w('ip_imp_script_uses.m', sprintf('script_result = Max(6, 7);'));
w('ip_imp_script_imports.m', sprintf('import System.Math.*\nscript_imported = 1;'));
w('ip_imp_after_script.m', sprintf('function r = ip_imp_after_script()\nip_imp_script_imports;\nr = import;\nend'));
rehash;
ip_pr('fn.wild', 'ip_imp_fn()');
ip_pr('fn.list', 'ip_imp_fn_list()');
ip_pr('fn.clear', 'ip_imp_fn_clear()');
ip_pr('fn.late', 'ip_imp_fn_late()');
ip_pr('fn.branch.true', 'ip_imp_fn_branch(true)');
ip_pr('fn.branch.false', 'ip_imp_fn_branch(false)');
ip_pr('fn.local', 'ip_imp_fn_local()');
ip_pr('fn.nested', 'ip_imp_fn_nested()');
ip_pr('fn.anon', 'feval(ip_imp_fn_anon())');
ip_pr('fn.caller.callee', 'ip_imp_caller()');
ip_pr('fn.calls.script', 'ip_imp_calls_script()');
ip_pr('fn.after.script.imports', 'ip_imp_after_script()');
end

function run_case(root, name, imports, checks)
% One generated script: the import lines, then one literal try/catch per check.
file = ['ip_case_' regexprep(name, '\W', '_')];
lines = imports(:)';
for k = 1:size(checks, 1)
    key = ['case.' name '.' checks{k, 1}];
    lines{end + 1} = sprintf(['try, v = %s; fprintf(''%%s\\t%%s\\n'', ''%s'', ip_describe(v)); ' ...
        'catch e, fprintf(''%%s\\tERR %%s %%s\\n'', ''%s'', e.identifier, strrep(e.message, newline, '' | '')); end'], ...
        checks{k, 2}, key, key); %#ok<AGROW>
end
ip_write(fullfile(root, [file '.m']), strjoin(lines, newline));
rehash;
try
    evalc_out = evalc(file);
    fprintf('%s', evalc_out);
catch e
    fprintf('case.%s\tFILE ERR %s %s\n', name, e.identifier, strrep(e.message, newline, ' | '));
end
end

function ip_import_cleanup(root)
rmpath(root);
rmdir(root, 's');
end

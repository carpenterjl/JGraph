function probe1
% Non-graphics probe of MATLAB Compiler surface in R2025b
names = {'isdeployed','ismcc','ctfroot','mcrversion','mcrinstaller','mcrcachedir', ...
    'getmcruserdata','setmcruserdata','deployprint','isstandalone','deploytool', ...
    'applicationCompiler','webAppCompiler','libraryCompiler','productionServerCompiler', ...
    'createDeploymentScript','mbuild','deploypcode','printdlg','mcc','compiler.runtime.download', ...
    'compiler.runtime.version','compiler.runtime.installer','compiler.UserInfo','figToImStream', ...
    'matlab.codetools.requiredFilesAndProducts','isdeployable','mclisnumerictype','waitforallfiguresclosed'};
for k = 1:numel(names)
    fprintf('==== which -all %s\n', names{k});
    try
        w = which(names{k}, '-all');
        if ischar(w), w = {w}; end
        fprintf('  %s\n', w{:});
    catch e
        fprintf('  ERR %s\n', e.message);
    end
end
helps = {'isdeployed','ismcc','getmcruserdata','setmcruserdata','deployprint','deploytool', ...
    'applicationCompiler','createDeploymentScript','compiler.runtime.download','compiler.runtime.version', ...
    'compiler.runtime.installer','compiler.package.installer','compiler.runtime.customInstaller','mbuild'};
for k = 1:numel(helps)
    fprintf('==== help %s\n', helps{k});
    try
        disp(help(helps{k}));
    catch e
        fprintf('  ERR %s\n', e.message);
    end
end
fprintf('==== isdeployed=%d ismcc=%d\n', isdeployed, ismcc);
try, fprintf('==== ctfroot = %s\n', ctfroot); catch e, disp(e.message); end
try, [a,b,c] = mcrversion; fprintf('==== mcrversion %d.%d.%d\n', a,b,c); catch e, disp(e.message); end
try, fprintf('==== mcrcachedir = %s\n', mcrcachedir); catch e, disp(e.message); end
try, v = compiler.runtime.version; disp('==== compiler.runtime.version'); disp(v); catch e, disp(e.message); end
try
    rc = ?matlab.depfun.internal.requirementsConstants;
    disp('==== requirementsConstants props');
    for p = rc.PropertyList'
        if p.Constant
            try
                val = matlab.depfun.internal.requirementsConstants.(p.Name);
                if ischar(val) || isstring(val) || iscellstr(val)
                    s = strjoin(cellstr(string(val)), ' | ');
                    if strlength(s) > 400, s = extractBefore(s, 400); end
                    fprintf('  %s = %s\n', p.Name, s);
                elseif isnumeric(val) || islogical(val)
                    fprintf('  %s = %s\n', p.Name, mat2str(val));
                else
                    fprintf('  %s : %s\n', p.Name, class(val));
                end
            catch
            end
        end
    end
catch e, disp(e.message); end
cls = {'compiler.build.StandaloneApplicationOptions','compiler.build.WebAppArchiveOptions', ...
    'compiler.build.ExcelAddInOptions','compiler.package.InstallerOptions','compiler.package.DockerOptions', ...
    'compiler.build.Results','compiler.runtime.Dependencies'};
for k = 1:numel(cls)
    mc = meta.class.fromName(cls{k});
    fprintf('==== class %s\n', cls{k});
    for p = mc.PropertyList'
        if strcmp(p.GetAccess,'public') && ~p.Hidden
            d = '';
            if p.HasDefault
                try, d = evalc('disp(p.DefaultValue)'); d = strtrim(d); catch, end
            end
            fprintf('  %s  [Dependent=%d]  default: %s\n', p.Name, p.Dependent, strrep(d, newline, ' '));
        end
    end
end
msgs = {'MCR:clc:NoClc','MCR:home:NoHome','Compiler:runtime:downloadCommand', ...
    'MCR:runtimeInitializationChecks:USER_MUST_SET_ENV_VAR'};
for k = 1:numel(msgs)
    try, fprintf('==== msg %s: %s\n', msgs{k}, getString(message(msgs{k}))); catch e, fprintf('==== msg %s ERR %s\n', msgs{k}, e.message); end
end
end

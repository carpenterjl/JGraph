function probe3
rc = 'matlab.depfun.internal.requirementsConstants';
for nm = {'WebAppMultiwindowUnsupportedList','WebAppPrintingUnsupportedList','undeployableBuiltins','PCTEnhancedMATLABFcns'}
    v = eval([rc '.' nm{1}]);
    fprintf('==== %s (%d)\n', nm{1}, numel(v));
    fprintf('  %s\n', v{:});
end
for f = {'compiler.project.getStandaloneDesktopAppConfiguration','compiler.project.getWebAppConfiguration', ...
         'compiler.runtime.download','compiler.runtime.installer','compiler.runtime.version'}
    try
        fprintf('==== nargin(%s)=%d nargout=%d\n', f{1}, nargin(f{1}), nargout(f{1}));
    catch e
        fprintf('==== nargin(%s) ERR %s\n', f{1}, e.message);
    end
end
for m = {'Compiler:deploytool:JavaAPIToBeRemoved','Compiler:deploytool:DeploytoolBuildPackageToBeRemoved', ...
         'Compiler:deploytool:DeploytoolFunctionalityToBeRemoved','Compiler:deploytool:HadoopCompilerRemoved'}
    try
        fprintf('==== %s: %s\n', m{1}, getString(message(m{1}, 'X', 'Y')));
    catch e
        fprintf('==== %s ERR %s\n', m{1}, e.message);
    end
end
end

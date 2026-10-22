function probe2
% mcc -? only prints help text; it does not build anything.
try
    s = evalc('mcc -?');
    fprintf('==== mcc -?\n%s\n', s);
catch e
    fprintf('==== mcc -? ERR %s\n', e.message);
end
hs = {'compiler.project.getStandaloneDesktopAppConfiguration','compiler.project.getWebAppConfiguration', ...
      'compiler.project.getExcelAddInConfiguration','matlab.codetools.requiredFilesAndProducts', ...
      'compiler.build.webAppArchive','compiler.build.excelAddIn','compiler.package.docker', ...
      'compiler.runtime.createDockerImage','compiler.runtime.createInstallerDockerImage', ...
      'compiler.codetools.deployableSupportPackages','webAppCompiler','libraryCompiler','mcrinstaller'};
for k = 1:numel(hs)
    fprintf('==== help %s\n', hs{k});
    try, disp(help(hs{k})); catch e, fprintf('ERR %s\n', e.message); end
end
try
    v = compiler.runtime.version;
    fprintf('==== compiler.runtime.version class=%s\n', class(v)); disp(v);
    if isstruct(v) || isobject(v), disp(v); end
catch e, disp(e.message); end
end

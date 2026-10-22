cd(fileparts(mfilename('fullpath')));
f = fullfile(pwd,'addme.m');
names = {'CSharedLibraryOptions','CppSharedLibraryOptions','DotNETAssemblyOptions','JavaPackageOptions','PythonPackageOptions','ProductionServerArchiveOptions','ExcelClientForProductionServerOptions','COMComponentOptions'};
for k = 1:numel(names)
  fprintf('\n######## compiler.build.%s\n', names{k});
  try
    o = feval(['compiler.build.' names{k}], f);
    disp(o)
    mc = metaclass(o);
    for p = mc.PropertyList'
      if strcmp(p.GetAccess,'public') && ~p.Hidden
        fprintf('  PROP %s (Dependent=%d)\n', p.Name, p.Dependent);
      end
    end
  catch e
    fprintf('ERR %s\n', e.message);
  end
end
fprintf('\n######## CppSharedLibraryOptions mwarray\n');
try, disp(compiler.build.CppSharedLibraryOptions(f,'Interface','mwarray')), catch e, disp(e.message), end
fprintf('\n######## DotNETAssemblyOptions matlab-data\n');
try, disp(compiler.build.DotNETAssemblyOptions(f,'Interface','matlab-data')), catch e, disp(e.message), end
fprintf('\n######## package options\n');
try, disp(compiler.package.InstallerOptions('ApplicationName','x')), catch e, disp(e.message), end
try, disp(compiler.package.MicroserviceDockerImageOptions(compiler.build.Results.empty)), catch e, disp(e.message), end
w = which('-all','compiler.build.Results'); disp(w)

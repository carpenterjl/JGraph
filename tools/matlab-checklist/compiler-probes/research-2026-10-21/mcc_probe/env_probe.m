function env_probe()
out = fullfile(pwd, 'env');
if ~exist(out, 'dir'), mkdir(out); end
w = @(name, txt) writetxt(fullfile(out, name), txt);
w('ver.txt', evalc('ver'));
w('lic_compiler.txt', sprintf('Compiler=%d CompilerSDK=%d\n', license('test','Compiler'), license('test','MATLAB_Builder_for_dot_net')));
try, w('lic_inuse.txt', evalc('license(''inuse'')')); catch e, w('lic_inuse.txt', e.message); end
try, w('mcc_help.txt', evalc('mcc -?')); catch e, w('mcc_help.txt', e.message); end
try, w('mbuild_query.txt', evalc('mbuild -query')); catch e, w('mbuild_query.txt', e.message); end
try, w('mbuild_query_cpp.txt', evalc('mbuild -query -client mbuild_com')); catch e, w('mbuild_query_cpp.txt', e.message); end
try, w('mex_setup_v.txt', evalc('mex -setup -v')); catch e, w('mex_setup_v.txt', e.message); end
try, w('mex_setup_cpp.txt', evalc('mex -setup C++')); catch e, w('mex_setup_cpp.txt', e.message); end
try, w('pyenv.txt', evalc('disp(pyenv)')); catch e, w('pyenv.txt', e.message); end
names = {'standaloneApplication','standaloneWindowsApplication','pythonPackage','dotNETAssembly','cppSharedLibrary','javaPackage','productionServerArchive','excelAddIn','cSharedLibrary'};
for i = 1:numel(names)
    try, w(['help_', names{i}, '.txt'], evalc(['help compiler.build.', names{i}])); catch e, w(['help_', names{i}, '.txt'], e.message); end
end
w('meta_build_pkg.txt', evalc('m = meta.package.fromName(''compiler.build''); disp({m.ClassList.Name}''); disp({m.FunctionList.Name}'')'));
opts = {'StandaloneApplicationOptions','hello.m'; 'StandaloneWindowsApplicationOptions','hello.m'; ...
    'PythonPackageOptions','addtwo.m'; 'DotNETAssemblyOptions','addtwo.m'; 'CppSharedLibraryOptions','typedadd.m'; ...
    'JavaPackageOptions','addtwo.m'; 'ProductionServerArchiveOptions','addtwo.m'; 'CSharedLibraryOptions','addtwo.m'; 'ExcelAddInOptions','addtwo.m'};
for i = 1:size(opts,1)
    try
        o = feval(['compiler.build.', opts{i,1}], opts{i,2});
        txt = evalc('disp(o)');
        mc = metaclass(o); pl = mc.PropertyList;
        s = '';
        for k = 1:numel(pl)
            s = [s, sprintf('%s  [%s]\n', pl(k).Name, class(o.(pl(k).Name)))]; %#ok<AGROW>
        end
        w(['opts_', opts{i,1}, '.txt'], [txt, newline, '---- properties ----', newline, s]);
    catch e
        w(['opts_', opts{i,1}, '.txt'], e.message);
    end
end
w('runtime.txt', sprintf('matlabroot=%s\nmcrversion: %s\n', matlabroot, evalc('[a,b]=mcrversion; disp([a b])')));
end
function writetxt(f, t)
fid = fopen(f, 'w'); fwrite(fid, t); fclose(fid);
end

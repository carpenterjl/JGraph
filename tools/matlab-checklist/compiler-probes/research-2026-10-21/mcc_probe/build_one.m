function build_one(target)
%BUILD_ONE Run one compiler build in the src folder; log to ../logs/<target>.txt
src = pwd;
root = fileparts(src);
logdir = fullfile(root, 'logs');
if ~exist(logdir, 'dir'), mkdir(logdir); end
outdir = fullfile(root, 'out', target);
if exist(outdir, 'dir'), rmdir(outdir, 's'); end
mkdir(outdir);
cmd = '';
t0 = tic;
ok = true; err = '';
res = [];
try
    switch target
        case 'mcc_m'
            cmd = sprintf('mcc -m hello.m -a data.mat -d ''%s''', outdir);
            txt = evalc('mcc(''-m'', ''hello.m'', ''-a'', ''data.mat'', ''-d'', outdir)');
        case 'mcc_m_v'
            cmd = sprintf('mcc -v -m hello.m -a data.mat -d ''%s''', outdir);
            txt = evalc('mcc(''-v'', ''-m'', ''hello.m'', ''-a'', ''data.mat'', ''-d'', outdir)');
        case 'api_standalone'
            cmd = 'compiler.build.standaloneApplication(''hello.m'', ''AdditionalFiles'', ''data.mat'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.standaloneApplication(''hello.m'', ''AdditionalFiles'', ''data.mat'', ''OutputDir'', outdir);');
        case 'mcc_feval1'
            cmd = 'mcc -m hellofeval.m -d outdir';
            txt = evalc('mcc(''-m'', ''hellofeval.m'', ''-d'', outdir)');
        case 'mcc_feval2'
            cmd = 'mcc -m hellofeval2.m -d outdir';
            txt = evalc('mcc(''-m'', ''hellofeval2.m'', ''-d'', outdir)');
        case 'mcc_e'
            cmd = 'mcc -e hello.m -a data.mat -d outdir';
            txt = evalc('mcc(''-e'', ''hello.m'', ''-a'', ''data.mat'', ''-d'', outdir)');
        case 'api_python'
            cmd = 'compiler.build.pythonPackage({''addtwo.m'',''multi.m'',''shapes.m''}, ''PackageName'', ''jgprobe'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.pythonPackage({''addtwo.m'',''multi.m'',''shapes.m''}, ''PackageName'', ''jgprobe'', ''OutputDir'', outdir);');
        case 'api_dotnet'
            cmd = 'compiler.build.dotNETAssembly({''addtwo.m'',''multi.m'',''shapes.m''}, ''AssemblyName'', ''JgProbe'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.dotNETAssembly({''addtwo.m'',''multi.m'',''shapes.m''}, ''AssemblyName'', ''JgProbe'', ''OutputDir'', outdir);');
        case 'api_dotnet_mda'
            cmd = 'compiler.build.dotNETAssembly(''typedadd.m'', ''Interface'', ''matlab-data'', ''AssemblyName'', ''JgProbeData'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.dotNETAssembly(''typedadd.m'', ''Interface'', ''matlab-data'', ''AssemblyName'', ''JgProbeData'', ''OutputDir'', outdir);');
        case 'api_cpp'
            cmd = 'compiler.build.cppSharedLibrary({''typedadd.m'',''addtwo.m''}, ''Interface'', ''matlab-data'', ''LibraryName'', ''jgcpp'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.cppSharedLibrary({''typedadd.m'',''addtwo.m''}, ''Interface'', ''matlab-data'', ''LibraryName'', ''jgcpp'', ''OutputDir'', outdir);');
        case 'api_cpp_mwarray'
            cmd = 'compiler.build.cppSharedLibrary(''addtwo.m'', ''Interface'', ''mwarray'', ''LibraryName'', ''jgcppmw'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.cppSharedLibrary(''addtwo.m'', ''Interface'', ''mwarray'', ''LibraryName'', ''jgcppmw'', ''OutputDir'', outdir);');
        case 'api_mps'
            cmd = 'compiler.build.productionServerArchive({''addtwo.m'',''multi.m''}, ''ArchiveName'', ''jgmps'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.productionServerArchive({''addtwo.m'',''multi.m''}, ''ArchiveName'', ''jgmps'', ''OutputDir'', outdir);');
        case 'mcc_clib'
            cmd = 'mcc -W lib:libprobe -T link:lib addtwo.m -d outdir';
            txt = evalc('mcc(''-W'', ''lib:libprobe'', ''-T'', ''link:lib'', ''addtwo.m'', ''-d'', outdir)');
        case 'api_java'
            cmd = 'compiler.build.javaPackage(''addtwo.m'', ''PackageName'', ''jgprobe'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.javaPackage(''addtwo.m'', ''PackageName'', ''jgprobe'', ''OutputDir'', outdir);');
        otherwise
            error('unknown target %s', target);
    end
catch e
    ok = false;
    err = getReport(e, 'extended', 'hyperlinks', 'off');
    txt = '';
end
el = toc(t0);
fid = fopen(fullfile(logdir, [target, '.txt']), 'w');
fprintf(fid, 'target: %s\ncommand: %s\nok: %d\nseconds: %.1f\n', target, cmd, ok, el);
fprintf(fid, '---- output ----\n%s\n', txt);
fprintf(fid, '---- error ----\n%s\n', err);
if ~isempty(res)
    fprintf(fid, '---- results ----\n%s\n', evalc('disp(res)'));
    try, fprintf(fid, '---- results.Options ----\n%s\n', evalc('disp(res.Options)')); catch, end
    try, fprintf(fid, '---- results.Files ----\n%s\n', evalc('disp(res.Files)')); catch, end
end
fclose(fid);
end

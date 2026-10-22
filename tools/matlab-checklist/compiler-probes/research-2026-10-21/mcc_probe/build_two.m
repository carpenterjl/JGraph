function build_two(target)
%BUILD_TWO Second round of builds; log to ../logs/<target>.txt
src = pwd;
root = fileparts(src);
logdir = fullfile(root, 'logs');
outdir = fullfile(root, 'out', target);
if exist(outdir, 'dir'), rmdir(outdir, 's'); end
mkdir(outdir);
cmd = ''; t0 = tic; ok = true; err = ''; res = []; txt = '';
try
    switch target
        case 'api_dotnet_core'
            cmd = 'compiler.build.dotNETAssembly({''addtwo.m'',''shapes.m'',''getint8.m'',''getstr.m'',''getcplx.m''}, ''AssemblyName'', ''JgProbeCore'', ''FrameworkVersion'', ''5.0'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.dotNETAssembly({''addtwo.m'',''shapes.m'',''getint8.m'',''getstr.m'',''getcplx.m''}, ''AssemblyName'', ''JgProbeCore'', ''FrameworkVersion'', ''5.0'', ''OutputDir'', outdir);');
        case 'mcc_err'
            cmd = 'mcc -m hellerr.m -d outdir';
            txt = evalc('mcc(''-m'', ''hellerr.m'', ''-d'', outdir)');
        case 'mcc_m_noa'
            cmd = 'mcc -m hello.m -d outdir   (no -a: does load(''data.mat'') autodetect?)';
            txt = evalc('mcc(''-m'', ''hello.m'', ''-d'', outdir)');
        case 'mcc_m_v'
            cmd = 'mcc -v -m hello.m -a data.mat -d outdir';
            txt = evalc('mcc(''-v'', ''-m'', ''hello.m'', ''-a'', ''data.mat'', ''-d'', outdir)');
        case 'api_numeric'
            cmd = 'compiler.build.standaloneApplication(''hellofeval2.m'', ''TreatInputsAsNumeric'', ''on'', ''ExecutableName'', ''helnum'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.standaloneApplication(''hello.m'', ''AdditionalFiles'', ''data.mat'', ''TreatInputsAsNumeric'', ''on'', ''ExecutableName'', ''helnum'', ''OutputDir'', outdir);');
        case 'api_dotnet_mda_full'
            cmd = 'compiler.build.dotNETAssembly({''typedadd.m'',''addtwo.m''}, ''Interface'', ''matlab-data'', ''AssemblyName'', ''JgProbeData2'', ''SampleGenerationFiles'', ''sample_typedadd.m'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.dotNETAssembly({''typedadd.m'',''addtwo.m''}, ''Interface'', ''matlab-data'', ''AssemblyName'', ''JgProbeData2'', ''SampleGenerationFiles'', ''sample_typedadd.m'', ''OutputDir'', outdir);');
        case 'api_python_sample'
            cmd = 'compiler.build.pythonPackage(''multi.m'', ''PackageName'', ''jgsample'', ''SampleGenerationFiles'', ''sample_multi.m'', ''OutputDir'', outdir)';
            txt = evalc('res = compiler.build.pythonPackage(''multi.m'', ''PackageName'', ''jgsample'', ''SampleGenerationFiles'', ''sample_multi.m'', ''OutputDir'', outdir);');
    end
catch e
    ok = false;
    err = getReport(e, 'extended', 'hyperlinks', 'off');
end
el = toc(t0);
fid = fopen(fullfile(logdir, [target, '.txt']), 'w');
fprintf(fid, 'target: %s\ncommand: %s\nok: %d\nseconds: %.1f\n', target, cmd, ok, el);
fprintf(fid, '---- output ----\n%s\n', txt);
fprintf(fid, '---- error ----\n%s\n', err);
if ~isempty(res)
    fprintf(fid, '---- results ----\n%s\n', evalc('disp(res)'));
    try, fprintf(fid, '---- results.Files ----\n%s\n', evalc('disp(res.Files)')); catch, end
end
fclose(fid);
end

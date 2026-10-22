function build_clients()
%BUILD_CLIENTS Compile the C and C++ clients with mbuild; log to ../logs/mbuild_*.txt
root = fileparts(pwd);
logdir = fullfile(root, 'logs');
cdir = fullfile(root, 'clients', 'c');
cppdir = fullfile(root, 'clients', 'cpp');
libdir = fullfile(root, 'out', 'mcc_clib');
cpplibdir = fullfile(root, 'out', 'api_cpp', 'v2', 'generic_interface');
t0 = tic;
try
    txt = evalc('mbuild(''-v'', fullfile(cdir, ''cclient.c''), [''-I'', libdir], fullfile(libdir, ''libprobe.lib''), ''-outdir'', cdir)');
    ok = 1; err = '';
catch e
    txt = ''; ok = 0; err = getReport(e, 'extended', 'hyperlinks', 'off');
end
writelog(fullfile(logdir, 'mbuild_c.txt'), ok, toc(t0), txt, err);
t0 = tic;
try
    txt = evalc('mbuild(''-v'', fullfile(cppdir, ''cppclient.cpp''), [''-I'', cpplibdir], ''-outdir'', cppdir)');
    ok = 1; err = '';
catch e
    txt = ''; ok = 0; err = getReport(e, 'extended', 'hyperlinks', 'off');
end
writelog(fullfile(logdir, 'mbuild_cpp.txt'), ok, toc(t0), txt, err);
end
function writelog(f, ok, el, txt, err)
fid = fopen(f, 'w');
fprintf(fid, 'ok: %d\nseconds: %.1f\n---- output ----\n%s\n---- error ----\n%s\n', ok, el, txt, err);
fclose(fid);
end

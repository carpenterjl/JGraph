% Stage K6's programs built by JGraph's mcc and compiler.build, as probe_k6_build built R2025b's
% (compiler and SDK plan). run-k6.ps1 runs this under the JGraph Runtime's jgraph -batch with K6_OUT
% naming the build folder, whose src holds the probe's sources. One key<TAB>value line per build.
out = getenv('K6_OUT');
src = fullfile(out, 'src');
cd(src);
[x, y] = meshgrid(linspace(0, 1, 64));
imwrite(uint8(255 * cat(3, x, y, 1 - x)), fullfile(src, 'k6_icon.png'));
[x, y] = meshgrid(linspace(0, 1, 200), linspace(0, 1, 100));
imwrite(uint8(255 * cat(3, y, x, 0.5 + 0 * x)), fullfile(src, 'k6_splash.png'));
mlapp = fullfile(src, 'mlapp', 'k6_app.mlapp');

builds = {
    'm_plain',       {'-m', 'k6_hello.m'}
    'e_plain',       {'-e', 'k6_hello.m'}
    'm_named',       {'-m', 'k6_hello.m', '-o', 'k6_named'}
    'm_version',     {'-W', 'main:k6_ver,version=2.3', '-T', 'link:exe', 'k6_hello.m'}
    'e_winmain',     {'-W', 'WinMain:k6_wm,version=4.5.6.7', '-T', 'link:exe', 'k6_hello.m'}
    'm_help',        {'-m', 'k6_hello.m', '-h', fullfile(src, 'k6_help.txt'), '-o', 'k6_helped'}
    'm_separate',    {'-m', 'k6_hello.m', '-C', '-o', 'k6_sep'}
    'm_fig',         {'-m', 'k6_fig.m'}
    'e_fig',         {'-e', 'k6_fig.m'}
    'm_err',         {'-m', 'k6_err.m'}
    'e_err',         {'-e', 'k6_err.m'}
    'e_wait',        {'-e', 'k6_wait.m'}
    'e_app_m',       {'-e', 'k6_app.m'}
    'e_app_mlapp',   {'-e', mlapp}
    'm_app_mlapp',   {'-m', mlapp, '-o', 'k6_app_console'}
    };
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(out, key);
    mkdir(d);
    try
        mcc(args{:}, '-d', d);
        fprintf('mcc.%s\tok\n', key);
    catch ME
        fprintf('mcc.%s\tERR %s | %s\n', key, ME.identifier, strrep(ME.message, newline, ' / '));
    end
end

built('api_m', @() compiler.build.standaloneApplication('k6_hello.m', 'OutputDir', fullfile(out, 'api_m'), 'ExecutableName', 'k6_api', ...
    'ExecutableIcon', fullfile(src, 'k6_icon.png'), 'ExecutableVersion', '3.4', 'CustomHelpTextFile', fullfile(src, 'k6_help.txt')));
built('api_e', @() compiler.build.standaloneWindowsApplication('k6_wait.m', 'OutputDir', fullfile(out, 'api_e'), ...
    'ExecutableSplashScreen', fullfile(src, 'k6_splash.png')));
built('api_e_default', @() compiler.build.standaloneWindowsApplication('k6_hello.m', 'OutputDir', fullfile(out, 'api_e_default')));
built('api_e_app', @() compiler.build.standaloneWindowsApplication(mlapp, 'OutputDir', fullfile(out, 'api_e_app')));
built('api_m_separate', @() compiler.build.standaloneApplication('k6_hello.m', 'OutputDir', fullfile(out, 'api_m_separate'), 'EmbedArchive', 'off'));
built('api_m_numeric', @() compiler.build.standaloneApplication('k6_hello.m', 'OutputDir', fullfile(out, 'api_m_numeric'), 'TreatInputsAsNumeric', 'on'));

function built(key, f)
try
    r = f();
    fprintf('%s\tok %s | %s\n', key, r.BuildType, strjoin(cellfun(@(p) strrep(p, fileparts(r.Files{1}), '<OUT>'), r.Files', 'UniformOutput', false), ' | '));
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, strrep(ME.message, newline, ' / '));
end
end

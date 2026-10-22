function build_k0(outRoot, only)
%BUILD_K0 Build the stage K0 probe standalones with R2025b's mcc into outRoot (outside the repo).
%   build_k0(outRoot) builds every target; build_k0(outRoot, {'k0_env'}) builds the named ones.
%   Prints one line per build: target<TAB>ok|FAILED<TAB>seconds<TAB>the mcc arguments, then the
%   build's own output and any warning lines, indented. Nothing is run here; run-k0.ps1 runs the
%   programs through run-quiet.ps1.
here = fileparts(mfilename('fullpath'));
src = fullfile(here, 'src');
addpath(src);
assets = fullfile(outRoot, 'assets');
if ~exist(assets, 'dir'), mkdir(assets); end
note = 'k0 data'; v = [1 2 3]; %#ok<NASGU>
save(fullfile(assets, 'data.mat'), 'note', 'v');
writelines('inside text', fullfile(assets, 'inside.txt'));

t = {
    'k0_args',    src, {'-m', 'k0_args.m'}
    'k0_argsn',   src, {'-m', 'k0_args.m', '-n', '-o', 'k0_argsn'}
    'k0_exit',    src, {'-m', 'k0_exit.m'}
    'k0_env',     src, {'-m', 'k0_env.m', '-a', fullfile(assets, 'data.mat')}
    'k0_env_sep', src, {'-m', 'k0_env.m', '-C', '-o', 'k0_env_sep', '-a', fullfile(assets, 'data.mat')}
    'k0_path',    src, {'-m', 'k0_path.m', '-a', fullfile(assets, 'inside.txt'), '-a', fullfile(src, 'k0_inside_helper.m')}
    'k0_feval',   src, {'-m', 'k0_feval.m'}
    'k0_fevalp',  src, {'-m', 'k0_fevalp.m'}
    'k0_stubs',   src, {'-m', 'k0_stubs.m'}
    'k0_figure',  src, {'-m', 'k0_figure.m'}
    'k0_ropts',   src, {'-m', 'k0_ropts.m', '-R', '-startmsg,K0 starting', '-R', '-completemsg,K0 complete', ...
                        '-R', '-logfile,k0_ropts.log', '-R', '-singleCompThread'}
    'k0_startup', fullfile(src, 'startup_case'), {'-m', 'k0_startup.m'}
    };
if nargin >= 2 && ~isempty(only)
    t = t(ismember(t(:, 1), only), :);
end
for k = 1:size(t, 1)
    [name, folder, args] = t{k, :};
    dest = fullfile(outRoot, name);
    if exist(dest, 'dir'), rmdir(dest, 's'); end
    mkdir(dest);
    old = cd(folder);
    lastwarn('', '');
    tic;
    status = 'ok';
    try
        text = evalc('mcc(args{:}, ''-d'', dest)');
    catch ME
        status = 'FAILED';
        text = sprintf('%s | %s', ME.identifier, ME.message);
    end
    secs = toc;
    cd(old);
    fprintf('%s\t%s\t%.1f\tmcc %s\n', name, status, secs, strjoin(args, ' '));
    lines = splitlines(strtrim(text));
    for j = 1:numel(lines)
        if strlength(lines{j}) > 0, fprintf('    %s\n', lines{j}); end
    end
    [msg, id] = lastwarn;
    if ~isempty(msg), fprintf('    warning %s | %s\n', id, strrep(msg, newline, ' / ')); end
end
end

function probe_k7_more(work)
%PROBE_K7_MORE Stage K7's second probe (compiler and SDK plan): what probe_k7_switches left open -
%which identifiers -w takes (the table's short names or MATLAB's message identifiers), whether MATLAB's
%own warning state silences mcc's warnings and whether -w leaves it changed, -A's values, whether -P,
%-u, -b, -S and -H take a value, -U and -T codegen beside -e, and a bundle given too few arguments.
%Run under matlab -batch -noFigureWindows after probe_k7_switches, over the same <work> folder.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
src = fullfile(work, 'src');
old = cd(src);
cleanup = onCleanup(@() cd(old));
H = 'k7_hello.m';
S = fullfile(src, 'startup', 'k7_start.m');
startupId = 'Compiler:compiler:COM_WARN_STARTUP_FILE_INCLUDED';
repeatedId = 'Compiler:compiler:COM_WARN_REPEATED_FILE';

cases = {
    'w_dis_repeated_short',   {'-w', 'disable:repeated_file', '-m', H, H}
    'w_err_repeated_short',   {'-w', 'error:repeated_file', '-m', H, H}
    'w_dis_repeated_full',    {'-w', ['disable:' repeatedId], '-m', H, H}
    'w_err_repeated_full',    {'-w', ['error:' repeatedId], '-m', H, H}
    'w_dis_all_warnings',     {'-w', 'disable:all_warnings', '-m', S}
    'w_err_all_warnings',     {'-w', 'error:all_warnings', '-m', S}
    'w_err_switch_ignored',   {'-w', 'error:switch_ignored', '-g', '-m', H}
    'w_dis_switch_ignored',   {'-w', 'disable:switch_ignored', '-g', '-m', H}
    'w_dis_other_id',         {'-w', 'disable:MATLAB:k7:anything', '-m', S}
    'w_dis_err_id',           {'-w', 'disable:Compiler:compiler:COM_ERR_NO_FILES_SPECIFIED', '-m', S}
    'w_enable_short',         {'-w', 'enable:repeated_file', '-m', H, H}
    'w_list_dis_short',       {'-w', 'disable:repeated_file', '-w', 'list'}
    'w_list_err_short',       {'-w', 'error:switch_ignored', '-w', 'list'}
    'w_list_dis_full',        {'-w', ['disable:' startupId], '-w', 'list'}
    'w_list_disable',         {'-w', 'disable', '-w', 'list'}
    'w_list_error',           {'-w', 'error', '-w', 'list'}
    'w_dis_then_enable',      {'-w', 'disable', '-w', ['enable:' startupId], '-m', S}
    'w_err_then_dis',         {'-w', 'error', '-w', ['disable:' startupId], '-m', S}
    'w_case',                 {'-w', 'DISABLE', '-m', S}
    'w_list_upper',           {'-w', 'LIST'}
    'P_takes_value',          {'-P', '-m', H}
    'u_takes_value',          {'-u', '-m', H}
    'b_takes_value',          {'-b', '-m', H}
    'S_takes_value',          {'-S', '-m', H}
    'H_takes_value',          {'-H', '-m', H}
    'A_annotation',           {'-A', 'annotation:none', '-m', H}
    'A_line',                 {'-A', 'line:on', '-m', H}
    'A_debugline',            {'-A', 'debugline:off', '-m', H}
    'A_none',                 {'-A', 'none', '-m', H}
    'U_e',                    {'-U', '-e', H}
    'T_codegen_e',            {'-e', '-T', 'codegen', H}
    'T_compile_m',            {'-m', '-T', 'compile:exe', H}
    'K_codegen',              {'-K', '-m', '-T', 'codegen', H}
    'B_too_few',              {'-B', 'k7p2.bundle:k7_few', H}
    'B_too_many',             {'-B', 'k7p.bundle:k7_a,k7_b', H}
    'B_spaced_arg',           {'-B', 'k7p.bundle:k7 spaced', H}
    'o_twice',                {'-o', 'k7_first', '-o', 'k7_second', '-m', H}
    'd_twice',                {'-d', fullfile(work, 'b2', 'd_first'), '-m', H}
    'v_list',                 {'-v', '-w', 'list'}
    };
for k = 1:size(cases, 1)
    [key, args] = cases{k, :};
    d = fullfile(work, 'b2', key);
    if ~exist(d, 'dir'), mkdir(d); end
    run(key, [args, {'-d', d}], d);
end

% MATLAB's own warning state, and what -w leaves of it.
d = fullfile(work, 'b2', 'matlab_off');
mkdir(d);
warning('off', startupId);
run('matlab_off', {'-m', S, '-d', d}, d);
warning('on', startupId);
d = fullfile(work, 'b2', 'matlab_off_w_enable');
mkdir(d);
warning('off', startupId);
run('matlab_off_w_enable', {'-w', ['enable:' startupId], '-m', S, '-d', d}, d);
s = warning('query', startupId);
fprintf('after.w_enable.state\t%s\n', s.state);
warning('on', startupId);
d = fullfile(work, 'b2', 'w_disable_state');
mkdir(d);
run('w_disable_state', {'-w', ['disable:' startupId], '-m', S, '-d', d}, d);
s = warning('query', startupId);
fprintf('after.w_disable.state\t%s\n', s.state);
d = fullfile(work, 'b2', 'w_disable_all_state');
mkdir(d);
run('w_disable_all_state', {'-w', 'disable', '-m', S, '-d', d}, d);
s = warning('query', startupId);
fprintf('after.w_disable_all.state\t%s\n', s.state);
s = warning('query', 'all');
fprintf('after.all.default\t%s\n', s(1).state);
d = fullfile(work, 'b2', 'matlab_error');
mkdir(d);
warning('error', startupId); %#ok<WNTAG>
run('matlab_error', {'-m', S, '-d', d}, d);
warning('on', startupId);
end

function run(key, args, d)
lastwarn('', '');
try
    txt = evalc('mcc(args{:})');
    fprintf('mcc.%s\tok\n', key);
    txt = strrep(regexprep(txt, '<a [^>]*>|</a>', ''), sprintf('\r\n'), newline);
    lines = splitlines(txt);
    if ~isempty(lines) && isempty(lines{end}), lines(end) = []; end
    for j = 1:numel(lines)
        fprintf('  |%s\n', lines{j});
    end
catch ME
    fprintf('mcc.%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
k0_warned(['mcc.' key]);
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
names = arrayfun(@(f) sprintf('%s (%d)', strrep(fullfile(f.folder, f.name), [d filesep], ''), f.bytes), listing, 'UniformOutput', false);
fprintf('mcc.%s.files\t%s\n', key, strjoin(sort(names(:)'), ' | '));
end

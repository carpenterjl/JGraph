function probe_k7_switches(work)
%PROBE_K7_SWITCHES Runs R2025b's mcc with every switch stage K6 left (compiler and SDK plan, stage
%K7) - the obsolete ones, -g -G -K -M -f -Y -A -b -c -l -S -u -U -B -w, -T and -W in every form,
%the syntax errors, the path and -R/-Z switches - and prints what each did: ok with what mcc
%printed and the files it wrote, or its refusal, then any warning. Run under matlab -batch
%-noFigureWindows; nothing built is run. One key<TAB>value line per case, mcc's printed lines
%indented under it.
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k7', '*.m'), src);
copyfile(fullfile(here, 'src', 'k7', 'startup'), fullfile(src, 'startup'));
mkdir(fullfile(src, 'k7tb'));
writeText(fullfile(src, 'k7tb', 'k7_tb.m'), sprintf('function k7_tb()\ndisp(''tb'');\nend\n'));
writeText(fullfile(src, 'k7_bad.ico'), 'not an icon');
writeText(fullfile(src, 'k7_opts.xml'), '<config/>');
writeText(fullfile(src, 'k7_license.dat'), 'INCREMENT nothing');
writeText(fullfile(src, 'k7_data.txt'), 'data');
writeText(fullfile(src, 'k7.bundle'), '-m -o k7_bundled');
writeText(fullfile(src, 'k7p.bundle'), '-W main:%1% -T link:exe');
writeText(fullfile(src, 'k7p2.bundle'), sprintf('-W main:%%1%%,version=%%2%%\n-T link:exe'));
writeText(fullfile(src, 'k7nest.bundle'), '-B k7.bundle -v');
old = cd(src);
cleanup = onCleanup(@() cd(old));
H = 'k7_hello.m';
S = fullfile(src, 'startup', 'k7_start.m');
E = 'k7_excl.m';

% Each case: key, arguments, and whether -d <its folder> is added.
cases = {
    % The obsolete switches.
    'obs_O',            {'-O', '-m', H}, true
    'obs_V',            {'-V', '-m', H}, true
    'obs_i',            {'-i', '-m', H}, true
    'obs_z',            {'-z', '-m', H}, true
    'obs_win32',        {'-win32', '-m', H}, true
    'obs_win64',        {'-win64', '-m', H}, true
    'obs_y',            {'-y', '-m', H}, true
    'obs_u',            {'-u', '-m', H}, true
    'obs_S',            {'-S', '-m', H}, true
    'obs_L_m',          {'-L', 'C', '-m', H}, true
    'obs_t_m',          {'-t', '-m', H}, true
    'obs_F_alone',      {'-F', 'k7.prj'}, false
    'obs_F_m',          {'-F', 'k7.prj', '-m', H}, false
    'obs_E_alone',      {'-E', 'k7.prj'}, false
    'obs_x',            {'-x', H}, true
    % Switches beside a standalone build.
    'g',                {'-g', '-m', H}, true
    'G',                {'-G', '-m', H}, true
    'K_ok',             {'-K', '-m', H}, true
    'K_fail_a',         {'-K', '-m', H, '-a', 'k7_nosuch.mat'}, true
    'noK_fail_a',       {'-m', H, '-a', 'k7_nosuch.mat'}, true
    'K_fail_icon',      {'-K', '-m', H, '-r', 'k7_bad.ico'}, true
    'noK_fail_icon',    {'-m', H, '-r', 'k7_bad.ico'}, true
    'M',                {'-M', '-DK7=1', '-m', H}, true
    'M_end',            {'-m', H, '-M'}, true
    'f_missing',        {'-f', 'k7_nosuch.xml', '-m', H}, true
    'f_file',           {'-f', 'k7_opts.xml', '-m', H}, true
    'Y',                {'-Y', 'k7_license.dat', '-m', H}, true
    'Y_missing',        {'-Y', 'k7_nolicense.dat', '-m', H}, true
    'Y_end',            {'-m', H, '-Y'}, true
    'A_all',            {'-A', 'all', '-m', H}, true
    'A_bogus',          {'-A', 'bogus', '-m', H}, true
    'A_flag',           {'-A', '-m', H}, true
    'b_m',              {'-b', '-m', H}, true
    'c_m',              {'-c', '-m', H}, true
    'c_alone',          {'-c', H}, true
    'U_m',              {'-U', '-m', H}, true
    'H_m',              {'-H', '-m', H}, true
    'P_m',              {'-P', 'x', '-m', H}, true
    'D_m',              {'-D', 'x', '-m', H}, true
    'l',                {'-l', H}, true
    'l_o',              {'-l', H, '-o', 'k7lo'}, true
    % -T.
    'T_compile_exe',    {'-W', 'main:k7tc', '-T', 'compile:exe', H}, true
    'T_codegen_W',      {'-W', 'main:k7cg', '-T', 'codegen', H}, true
    'T_codegen_m',      {'-m', '-T', 'codegen', H}, true
    'T_codegen_alone',  {'-T', 'codegen', H}, true
    'T_link_lib_m',     {'-m', '-T', 'link:lib', H}, true
    'T_link_lib_e',     {'-e', '-T', 'link:lib', H}, true
    'T_exe',            {'-T', 'exe', '-m', H}, true
    'T_link_empty',     {'-T', 'link:', '-m', H}, true
    'T_link_exe_only',  {'-T', 'link:exe', H}, true
    'T_compile_lib',    {'-W', 'lib:k7cl', '-T', 'compile:lib', H}, true
    'T_twice',          {'-T', 'link:lib', '-T', 'link:exe', '-W', 'main:k7tt', H}, true
    'W_main_only',      {'-W', 'main:k7wo', H}, true
    % -W.
    'W_lib_noname',     {'-W', 'lib', H}, true
    'W_lib_empty',      {'-W', 'lib:', H}, true
    'W_lib_extra',      {'-W', 'lib:k7a,k7b', H}, true
    'W_lib_toomany',    {'-W', 'lib:a,b,c,d,e,f,g', H}, true
    'W_cpplib_empty',   {'-W', 'cpplib:', H}, true
    'W_cpplib_bad',     {'-W', 'cpplib:k7c,bogus', H}, true
    'W_cpplib_named',   {'-W', 'cpplib:k7c,named', H}, true
    'W_cpplib_api_bad', {'-W', 'cpplib:k7c,api=bogus', H}, true
    'W_dotnet_noclass', {'-W', 'dotnet:k7asm', H}, true
    'W_dotnet_api_bad', {'-W', 'dotnet:k7asm,api=bogus', H}, true
    'W_dotnet_api_late',{'-W', 'dotnet:k7asm,k7cls,api=matlab-data', H}, true
    'W_dotnet_ver_bad', {'-W', 'dotnet:k7asm,k7cls,3.5,Private', H}, true
    'W_java_noclass',   {'-W', 'java:k7pkg', H}, true
    'W_java_badpkg',    {'-W', 'java:7pkg,k7cls', H}, true
    'W_python_empty',   {'-W', 'python:', H}, true
    'W_python_bad',     {'-W', 'python:k7-py', H}, true
    'W_com_empty',      {'-W', 'com:', H}, true
    'W_CTF_noU',        {'-W', 'CTF:k7ctf', H}, true
    'W_mpsxl_empty',    {'-W', 'mpsxl:', H}, true
    'W_excel_empty',    {'-W', 'excel:', H}, true
    'W_spark_noC',      {'-W', 'spark:k7sp,2', H}, true
    'W_hadoop_noconf',  {'-W', 'hadoop:k7hd', H}, true
    'W_main_extra',     {'-W', 'main:k7me,bogus', '-T', 'link:exe', H}, true
    'W_main_toomany',   {'-W', 'main:a,b,c,d,e,f', '-T', 'link:exe', H}, true
    'W_main_empty',     {'-W', 'main:', '-T', 'link:exe', H}, true
    'W_main_digit',     {'-W', 'main:7k', '-T', 'link:exe', H}, true
    'W_main_nocolon',   {'-W', 'main', '-T', 'link:exe', H}, true
    'W_winmain_nocolon',{'-W', 'WinMain', '-T', 'link:exe', H}, true
    'W_two',            {'-W', 'main:k7a', '-W', 'main:k7b', '-T', 'link:exe', H}, true
    'W_main_o',         {'-W', 'main:k7wn', '-o', 'k7other', '-T', 'link:exe', H}, true
    'W_upper',          {'-W', 'MAIN:k7up', '-T', 'link:exe', H}, true
    'W_empty_item',     {'-W', 'main:k7ei,,version=1', '-T', 'link:exe', H}, true
    'W_version_twice',  {'-W', 'main:k7vt,version=1,version=2', '-T', 'link:exe', H}, true
    % The command line's syntax.
    'end_value',        {'-m', H, '-o'}, true
    'empty_value',      {'-o', '', '-m', H}, true
    'no_space',         {'-ok7ns', '-m', H}, true
    'combined_mv',      {'-mv', H}, true
    'combined_vm',      {'-vm', H}, true
    'combined_mC',      {'-mC', H}, true
    'whitespace',       {'-m k7_hello.m'}, true
    'repeated',         {'-m', H, H}, true
    'non_m',            {'-m', H, 'k7_data.txt'}, true
    'debug_t',          {'-/t', '-m', H}, true
    'debug_q',          {'-/?'}, false
    'no_args',          {}, false
    'dash_h_end',       {'-m', H, '-h'}, true
    'help_word',        {'-help'}, false
    'question_m',       {'-m', '-?', H}, true
    % Path switches.
    'p_noN',            {'-p', 'k7tb', '-m', H}, true
    'N_only',           {'-N', '-m', H}, true
    'Np',               {'-N', '-p', 'k7tb', '-m', H}, true
    'p_missing',        {'-N', '-p', 'k7_nosuchdir', '-m', H}, true
    'I_missing',        {'-I', 'k7_nosuchdir', '-m', H}, true
    'I_given',          {'-I', 'k7tb', '-m', H}, true
    % -R and -Z.
    'R_bogus',          {'-R', '-bogus', '-m', H}, true
    'R_nojvm',          {'-R', '-nojvm', '-m', H}, true
    'R_empty',          {'-R', '', '-m', H}, true
    'Z_bogus',          {'-Z', 'k7 no such package', '-m', H}, true
    'Z_none',           {'-Z', 'none', '-m', H}, true
    % -w.
    'w_list',           {'-w', 'list'}, false
    'w_list_m',         {'-w', 'list', '-m', H}, true
    'w_bogus',          {'-w', 'bogus', '-m', H}, true
    'w_end',            {'-m', H, '-w'}, true
    'w_start',          {'-m', S}, true
    'w_start_dis_short',{'-w', 'disable:COM_WARN_STARTUP_FILE_INCLUDED', '-m', S}, true
    'w_start_dis_full', {'-w', 'disable:Compiler:compiler:COM_WARN_STARTUP_FILE_INCLUDED', '-m', S}, true
    'w_start_err_short',{'-w', 'error:COM_WARN_STARTUP_FILE_INCLUDED', '-m', S}, true
    'w_start_err_full', {'-w', 'error:Compiler:compiler:COM_WARN_STARTUP_FILE_INCLUDED', '-m', S}, true
    'w_disable',        {'-w', 'disable', '-m', S}, true
    'w_error',          {'-w', 'error', '-m', S}, true
    'w_enable',         {'-w', 'enable', '-m', S}, true
    'w_off',            {'-w', 'off', '-m', S}, true
    'w_on',             {'-w', 'on', '-m', S}, true
    'w_dis_unknown',    {'-w', 'disable:k7_nosuch_id', '-m', S}, true
    'w_list_after_dis', {'-w', 'disable:COM_WARN_STARTUP_FILE_INCLUDED', '-w', 'list'}, false
    'w_excl',           {'-m', E}, true
    'w_excl_disable',   {'-w', 'disable', '-m', E}, true
    'w_excl_error',     {'-w', 'error', '-m', E}, true
    % -B.
    'B_user',           {'-B', 'k7.bundle', H}, true
    'B_param',          {'-B', 'k7p.bundle:k7_param', H}, true
    'B_param_two',      {'-B', 'k7p2.bundle:k7_p2,1.2', H}, true
    'B_param_none',     {'-B', 'k7p.bundle', H}, true
    'B_param_empty',    {'-B', 'k7p.bundle:', H}, true
    'B_missing',        {'-B', 'k7_nosuch.bundle', H}, true
    'B_nested',         {'-B', 'k7nest.bundle', H}, true
    'B_macro_m',        {'-B', 'macro_option_m', H}, true
    'B_csharedlib',     {'-B', 'csharedlib:k7blib', H}, true
    };
for k = 1:size(cases, 1)
    [key, args, withD] = cases{k, :};
    d = fullfile(work, 'b', key);
    mkdir(d);
    if withD, args = [args, {'-d', d}]; end %#ok<AGROW>
    lastwarn('', '');
    t = tic;
    try
        txt = quietMcc(args);
        fprintf('mcc.%s\tok (%.0f s)\n', key, toc(t));
        printLines(txt);
    catch ME
        fprintf('mcc.%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    k0_warned(['mcc.' key]);
    listFiles(['mcc.' key], d);
    if ~withD
        listFiles(['mcc.' key '.cwd'], src, true);
    end
end
end

function writeText(file, text)
fid = fopen(file, 'w');
fwrite(fid, text);
fclose(fid);
end

function listFiles(key, d, newOnly)
% A folder's files, or (newOnly) the source folder's files the probe did not write itself.
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
names = {};
for k = 1:numel(listing)
    rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
    if nargin > 2 && newOnly && (any(strcmp(rel, {'k7_hello.m', 'k7_excl.m', 'k7_bad.ico', 'k7_opts.xml', ...
            'k7_license.dat', 'k7_data.txt', 'k7.bundle', 'k7p.bundle', 'k7p2.bundle', 'k7nest.bundle'})) ...
            || startsWith(rel, {'startup', 'k7tb'}))
        continue
    end
    names{end + 1} = sprintf('%s (%d)', rel, listing(k).bytes); %#ok<AGROW>
end
if ~isempty(names) || nargin < 3
    fprintf('%s.files\t%s\n', key, strjoin(sort(names), ' | '));
end
end

function printLines(txt)
txt = strrep(txt, sprintf('\r\n'), newline);
txt = regexprep(txt, '<a [^>]*>|</a>', '');
lines = splitlines(txt);
if ~isempty(lines) && isempty(lines{end}), lines(end) = []; end
for j = 1:numel(lines)
    fprintf('  |%s\n', lines{j});
end
end

function txt = quietMcc(args)
% evalc in a named function: a lambda's workspace holds only what its body names.
txt = evalc('mcc(args{:})');
end

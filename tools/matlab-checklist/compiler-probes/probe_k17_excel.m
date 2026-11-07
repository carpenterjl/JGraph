function probe_k17_excel(work, upTo)
%PROBE_K17_EXCEL Records R2025b's Excel builds (compiler and SDK plan, stage K17a): what
%compiler.build.ExcelAddInOptions takes and refuses, what compiler.build.excelAddIn and mcc -W excel
%write for the add-in over src\k17's functions (every argument shape, with and without the .bas,
%the archive embedded or not, a debug build, each version form), what each build adds to the
%user's registry, and the same for the Excel client of the Production Server:
%compiler.build.excelClientForProductionServer from a server archive's Results and from files, each
%marshalling switch alone and together, and compiler.package.excelClientForProductionServer with
%each option. Run under matlab -batch -noFigureWindows; nothing built is run here (record-k17.ps1
%runs the add-in through src\k17client\k17com.ps1 and reads the installers). One key<TAB>value line
%per question, a display or a file's text as indented lines under its key. A second argument
%'addin' stops after the add-in sections (the options, the builds, mcc -W excel, the server archive
%and the add-in beside it): run-k17.ps1 runs JGraph that far until stage K17f gives it the client.
if nargin < 2, upTo = 'all'; end
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k17', '*.m'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));

names = {'k17_quiet', 'k17_none', 'k17_one', 'k17_many', 'k17_two', 'k17_opt', 'k17_varin', 'k17_varout', ...
    'k17_both', 'k17_args', 'k17_noout', 'k17_err', 'k17_plot', 'k17_h1', 'k17_echo', 'k17_pass', 'k17_count', 'k17_make'};
files = cellfun(@(n) fullfile(src, [n '.m']), names, 'UniformOutput', false);
one = files{3};  % k17_one.m
few = files(2:4); % k17_none, k17_one, k17_many
writeText(fullfile(src, 'k17data.txt'), sprintf('data\n'));
copyfile(fullfile(src, 'k17_one.m'), fullfile(src, 'k17_pfun.m'));
pcode(fullfile(src, 'k17_pfun.m'), '-inplace');
delete(fullfile(src, 'k17_pfun.m'));
pfile = fullfile(src, 'k17_pfun.p');

fprintf('matlab.version\t%s\n', version);
try
    v = ver('compiler');
    fprintf('compiler.version\t%s (%s)\n', v.Version, v.Release);
catch ME
    fprintf('compiler.version\tERR %s | %s\n', ME.identifier, k0_flat(ME.message)); % JGraph has no ver (open item 245)
end
try
    cc = mex.getCompilerConfigurations('C++', 'Selected');
    fprintf('mbuild.cpp\t%s %s\n', cc(1).Name, cc(1).Version);
catch ME
    fprintf('mbuild.cpp\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end

%% 1. The add-in options class.
show('opts.default', compiler.build.ExcelAddInOptions(files));
o = compiler.build.ExcelAddInOptions(one);
fprintf('opts.derived.AddInName\t%s\n', o.AddInName);
fprintf('opts.derived.ClassName\t%s\n', o.ClassName);
fprintf('opts.derived.OutputDir\t%s\n', o.OutputDir);
show('opts.properties', properties(o));
fprintf('opts.class\t%s\n', class(o));
fprintf('opts.superclasses\t%s\n', strjoin(superclasses(o)', ' | '));
k0_try('opts.noargs', @() compiler.build.ExcelAddInOptions());
k0_try('opts.odd', @() compiler.build.ExcelAddInOptions(one, 'AddInName'));
k0_try('opts.unknown', @() compiler.build.ExcelAddInOptions(one, 'Bogus', 1));
k0_try('opts.missing', @() compiler.build.ExcelAddInOptions(fullfile(src, 'nosuch.m')));
k0_try('opts.txt', @() compiler.build.ExcelAddInOptions(fullfile(src, 'k17data.txt')));
k0_try('opts.pfile', @() describe(getp(compiler.build.ExcelAddInOptions(pfile), 'FunctionFiles')));
k0_try('opts.string', @() describe(getp(compiler.build.ExcelAddInOptions(string(one), "AddInName", "k17s"), 'AddInName')));
k0_try('opts.case', @() describe(getp(compiler.build.ExcelAddInOptions(one, 'addinname', 'k17c'), 'AddInName')));
k0_try('opts.twice', @() describe(getp(compiler.build.ExcelAddInOptions(one, 'AddInName', 'first', 'AddInName', 'second'), 'AddInName')));
many = repmat({'Verbose', 'on'}, 1, 20);
k0_try('opts.toomany', @() compiler.build.ExcelAddInOptions(one, many{:}));
k0_try('opts.folder', @() describe(getp(compiler.build.ExcelAddInOptions(src), 'FunctionFiles')));
k0_try('opts.cellmixed', @() describe(getp(compiler.build.ExcelAddInOptions({one, pfile}), 'FunctionFiles')));

sets = {
    'AddInName', {'My Addin', '1abc', 'a-b', 'a.b', 'class', 'end', '', 5, "k17str", 'k17_ok', 'K17UPPER', 'a b', sprintf('a\tb'), 'x_'}
    'ClassName', {'', 'a b', 5, "K17Str", 'K17Ok', '1abc', 'a.b', 'class'}
    'AddInVersion', {'', 5, "2.0", 'abc', '1.2.3.4.5', '1.0.0.0', '0', '-1', '1,2', ' 1.0', '70000', '1.2.3.4', '2'}
    'DebugBuild', {'on', 'off', true, 0, 'maybe', 2, "on"}
    'EmbedArchive', {'on', 'off', false, 1, 'x'}
    'GenerateVisualBasicFile', {'on', 'off', true, 0, 'x'}
    'FunctionFiles', {one, {one, files{4}}, string(one), {}, '', pfile, fullfile(src, 'k17data.txt'), fullfile(src, 'nosuch.m'), 5}
    'OutputDir', {fullfile(work, 'o'), 'rel', '', 5}
    'Verbose', {'on', true, 'x'}
    'AdditionalFiles', {fullfile(src, 'k17data.txt'), {fullfile(src, 'k17data.txt'), src}, 'nosuch.txt', ''}
    'AutoDetectDataFiles', {'off', true, 'x'}
    'SupportPackages', {'none', 'autodetect', {'a', 'b'}, ''}
    'ObfuscateArchive', {'on', 'x'}
    'SecretsManifest', {'', 'nosuch.json'}
    };
for k = 1:size(sets, 1)
    [prop, values] = sets{k, :};
    for j = 1:numel(values)
        o = compiler.build.ExcelAddInOptions(one);
        k0_try(sprintf('set.%s.%d', prop, j), @() setAndRead(o, prop, values{j}));
    end
end
% The dependent names after FunctionFiles changes, and the user's name kept.
o = compiler.build.ExcelAddInOptions(one);
o.FunctionFiles = files{4};
fprintf('derived.afterfiles\t%s | %s\n', o.AddInName, o.ClassName);
o.AddInName = 'kept';
o.FunctionFiles = files{5};
fprintf('derived.userkept\t%s | %s\n', o.AddInName, o.ClassName);
k0_try('derived.saveload', @() saveLoad(compiler.build.ExcelAddInOptions(few, 'AddInName', 'k17sl', 'ClassName', 'K17Sl', 'AddInVersion', '2.1', 'GenerateVisualBasicFile', 'on')));
k0_try('build.noargs', @() compiler.build.excelAddIn());
k0_try('build.optsplus', @() compiler.build.excelAddIn(compiler.build.ExcelAddInOptions(one), 'Verbose', 'on'));
k0_try('build.nargout', @() nargout2(@() compiler.build.excelAddIn(one)));
k0_try('build.toomany', @() compiler.build.excelAddIn(one, many{:}));

%% 2. The add-in builds, each into a folder of its own, the registry's changes after each.
reg = regNames();
b = fullfile(work, 'b');
builds = {
    'plain',      {files, 'AddInName', 'k17xl', 'ClassName', 'K17Xl', 'Verbose', 'on'}
    'plain2',     {files, 'AddInName', 'k17xl', 'ClassName', 'K17Xl'}
    'bas',        {files, 'AddInName', 'k17bas', 'ClassName', 'K17Bas', 'GenerateVisualBasicFile', 'on'}
    'noembed',    {files, 'AddInName', 'k17sep', 'ClassName', 'K17Sep', 'EmbedArchive', 'off', 'GenerateVisualBasicFile', 'on'}
    'debug',      {few, 'AddInName', 'k17dbg', 'ClassName', 'K17Dbg', 'DebugBuild', 'on'}
    'v2',         {few, 'AddInName', 'k17v2', 'ClassName', 'K17V2', 'AddInVersion', '2'}
    'v05',        {few, 'AddInName', 'k17v05', 'ClassName', 'K17V05', 'AddInVersion', '0.5'}
    'v3456',      {few, 'AddInName', 'k17v3456', 'ClassName', 'K17V3456', 'AddInVersion', '3.4.5.6', 'GenerateVisualBasicFile', 'on'}
    'vtext',      {few, 'AddInName', 'k17vt', 'ClassName', 'K17Vt', 'AddInVersion', 'abc'}
    'v5parts',    {few, 'AddInName', 'k17v5', 'ClassName', 'K17V5', 'AddInVersion', '1.2.3.4.5'}
    'vbig',       {few, 'AddInName', 'k17vb', 'ClassName', 'K17Vb', 'AddInVersion', '70000'}
    'vzero',      {few, 'AddInName', 'k17vz', 'ClassName', 'K17Vz', 'AddInVersion', '0'}
    'defaults',   {one}
    'additional', {few, 'AddInName', 'k17add', 'ClassName', 'K17Add', 'AdditionalFiles', fullfile(src, 'k17data.txt'), 'EmbedArchive', 'off'}
    'pfile',      {{pfile, one}, 'AddInName', 'k17p', 'ClassName', 'K17P', 'GenerateVisualBasicFile', 'on'}
    'classname',  {one, 'AddInName', 'k17cls', 'ClassName', 'k17cls'}
    };
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(b, key);
    apiBuild(key, @() compiler.build.excelAddIn(args{:}, 'OutputDir', d), d);
    printText(key, d);
    reg = regDelta(key, reg);
end
k0_try('guids.plain.same_as_plain2', @() sameGuids(fullfile(b, 'plain', 'k17xl_idl.idl'), fullfile(b, 'plain2', 'k17xl_idl.idl')));
k0_try('guids.plain.same_as_bas', @() sameGuids(fullfile(b, 'plain', 'k17xl_idl.idl'), fullfile(b, 'bas', 'k17bas_idl.idl')));

% A build left to its own output folder: .\<name>excelAddIn under the current folder.
t = tic;
try
    r = compiler.build.excelAddIn(one, 'AddInName', 'k17own');
    fprintf('nooutdir.Files\t%s\n', strjoin(strrep(r.Files(:)', [src filesep], '<SRC>\'), ' | '));
    listFiles('nooutdir', fullfile(src, 'k17ownexcelAddIn'));
catch ME
    fprintf('nooutdir\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
fprintf('nooutdir.seconds\t%.1f\n', toc(t));
reg = regDelta('nooutdir', reg);

%% 3. mcc -W excel.
m = fullfile(work, 'm');
mccs = {
    'mcc_default',    {'-W', 'excel:k17m,K17M,version=1.0', '-T', 'link:lib', '-b', one}
    'mcc_noversion',  {'-W', 'excel:k17mnv,K17Mnv', '-T', 'link:lib', one}
    'mcc_noclass',    {'-W', 'excel:k17mnc', '-T', 'link:lib', one}
    'mcc_empty',      {'-W', 'excel:', '-T', 'link:lib', one}
    'mcc_nocolon',    {'-W', 'excel', '-T', 'link:lib', one}
    'mcc_badversion', {'-W', 'excel:k17mbv,K17Mbv,version=x.y', '-T', 'link:lib', one}
    'mcc_version2',   {'-W', 'excel:k17mv2,K17Mv2,version=2', '-T', 'link:lib', one}
    'mcc_extra',      {'-W', 'excel:k17mx,K17Mx,version=1.0,other=1', '-T', 'link:lib', one}
    'mcc_bundle',     {'-B', 'cexcel:k17mb,K17Mb,1.0', one}
    'mcc_notarget',   {'-W', 'excel:k17mnt,K17Mnt,version=1.0', one}
    'mcc_separate',   {'-W', 'excel:k17ms,K17Ms,version=1.0', '-T', 'link:lib', '-C', one}
    'mcc_debug',      {'-W', 'excel:k17md,K17Md,version=1.0', '-T', 'link:lib', '-g', one}
    'mcc_codegen',    {'-W', 'excel:k17mcg,K17Mcg,version=1.0', '-T', 'codegen', one}
    'mcc_compile',    {'-W', 'excel:k17mcp,K17Mcp,version=1.0', '-T', 'compile:lib', one}
    'mcc_badname',    {'-W', 'excel:1bad,K17Bad,version=1.0', '-T', 'link:lib', one}
    'mcc_badclass',   {'-W', 'excel:k17mbc,1Bad,version=1.0', '-T', 'link:lib', one}
    'mcc_dotted',     {'-W', 'excel:a.b,K17Dot,version=1.0', '-T', 'link:lib', one}
    'mcc_twofiles',   {'-W', 'excel:k17mtwo,K17Two,version=1.0', '-T', 'link:lib', '-b', files{2}, files{3}}
    'mcc_o',          {'-W', 'excel:k17mo,K17Mo,version=1.0', '-T', 'link:lib', '-o', 'k17other', one}
    'mcc_script',     {'-W', 'excel:k17msc,K17Msc,version=1.0', '-T', 'link:lib', fullfile(src, 'k17_script.m')}
    'mcc_samev2',     {'-W', 'excel:k17m,K17M,version=2.0', '-T', 'link:lib', one}
    'mcc_samefiles',  {'-W', 'excel:k17m,K17M,version=1.0', '-T', 'link:lib', one, files{5}}
    'mcc_sameorder',  {'-W', 'excel:k17m,K17M,version=1.0', '-T', 'link:lib', files{5}, one}
    };
writeText(fullfile(src, 'k17_script.m'), sprintf('%% a script, not a function\nx = 1;\n'));
for k = 1:size(mccs, 1)
    [key, args] = mccs{k, :};
    d = fullfile(m, key);
    mkdir(d);
    lastwarn('', '');
    t = tic;
    try
        txt = quietMcc([args, {'-d', d}]);
        fprintf('%s\tok\n', key);
        printLines(txt);
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    listFiles(key, d);
    fprintf('%s.seconds\t%.1f\n', key, toc(t));
    k0_warned(key);
    reg = regDelta(key, reg);
end
% The GUIDs of one project's names across versions and file lists (mcc_default is k17m,K17M,1.0 of one file).
k0_try('guids.mcc_default.same_as_samev2', @() sameGuids(fullfile(m, 'mcc_default', 'k17m_idl.idl'), fullfile(m, 'mcc_samev2', 'k17m_idl.idl')));
k0_try('guids.mcc_default.same_as_samefiles', @() sameGuids(fullfile(m, 'mcc_default', 'k17m_idl.idl'), fullfile(m, 'mcc_samefiles', 'k17m_idl.idl')));
k0_try('guids.mcc_default.same_as_sameorder', @() sameGuids(fullfile(m, 'mcc_default', 'k17m_idl.idl'), fullfile(m, 'mcc_sameorder', 'k17m_idl.idl')));
k0_try('guids.mcc_default.samev2.which', @() whichGuids(fullfile(m, 'mcc_default', 'k17m_idl.idl'), fullfile(m, 'mcc_samev2', 'k17m_idl.idl')));
printText('mcc_default', fullfile(m, 'mcc_default'));
printFile('mcc_twofiles.bas', fullfile(m, 'mcc_twofiles', 'k17mtwo.bas'));
printFile('mcc_noversion.idl', fullfile(m, 'mcc_noversion', 'k17mnv_idl.idl'));
printFile('mcc_version2.rc', fullfile(m, 'mcc_version2', 'k17mv2.rc'));

%% 4. The Excel client for the Production Server.
c = fullfile(work, 'c');
rArch = apiBuild('arch', @() compiler.build.productionServerArchive(files, 'ArchiveName', 'k17arch', 'OutputDir', fullfile(c, 'arch')), fullfile(c, 'arch'));
archive = fullfile(c, 'arch', 'k17arch.ctf');
rAddin = compiler.build.excelAddIn(one, 'AddInName', 'k17ra', 'ClassName', 'K17Ra', 'OutputDir', fullfile(b, 'ra'));
reg = regDelta('ra', reg);
if strcmp(upTo, 'addin'), return, end

show('copts.results', compiler.build.ExcelClientForProductionServerOptions(rArch));
show('copts.files', compiler.build.ExcelClientForProductionServerOptions(one, archive));
oc = compiler.build.ExcelClientForProductionServerOptions(one, archive);
show('copts.properties', properties(oc));
fprintf('copts.class\t%s\n', class(oc));
fprintf('copts.superclasses\t%s\n', strjoin(superclasses(oc)', ' | '));
fprintf('copts.derived\t%s | %s | %s\n', oc.AddInName, oc.ClassName, oc.OutputDir);
k0_try('copts.noargs', @() compiler.build.ExcelClientForProductionServerOptions());
k0_try('copts.onefile', @() compiler.build.ExcelClientForProductionServerOptions(one));
k0_try('copts.addinresults', @() compiler.build.ExcelClientForProductionServerOptions(rAddin));
k0_try('copts.missingarchive', @() compiler.build.ExcelClientForProductionServerOptions(one, fullfile(c, 'nosuch.ctf')));
k0_try('copts.txtarchive', @() compiler.build.ExcelClientForProductionServerOptions(one, fullfile(src, 'k17data.txt')));
k0_try('copts.noext', @() describe(getp(compiler.build.ExcelClientForProductionServerOptions(one, fullfile(c, 'arch', 'k17arch')), 'ServerArchive')));
k0_try('copts.results.nv', @() describe(getp(compiler.build.ExcelClientForProductionServerOptions(rArch, 'AddInName', 'k17nv', 'ReplaceExcelBlankWithNaN', 'on'), 'AddInName')));
k0_try('copts.results.odd', @() compiler.build.ExcelClientForProductionServerOptions(rArch, 'AddInName'));
k0_try('copts.unknown', @() compiler.build.ExcelClientForProductionServerOptions(one, archive, 'Bogus', 1));
k0_try('copts.toomany', @() compiler.build.ExcelClientForProductionServerOptions(one, archive, many{:}));
csets = {
    'ServerArchive', {archive, fullfile(c, 'arch', 'k17arch'), 'nosuch.ctf', fullfile(src, 'k17data.txt'), '', 5, string(archive)}
    'ReplaceExcelBlankWithNaN', {'on', true, 0, 'x'}
    'ConvertExcelDateToString', {'on', 'off', 'x'}
    'ReplaceNaNToZeroInExcel', {'on', 1, 'x'}
    'ConvertNumericOutToDateInExcel', {'on', false, 'x'}
    'GenerateVisualBasicFile', {'off', 'on', 'x'}
    'AddInName', {'My Client', '1abc', 'k17c_ok', ''}
    'ClassName', {'', 'K17COk', 'a b'}
    'AddInVersion', {'', '2', 'abc'}
    'FunctionFiles', {few, {}, pfile}
    };
for k = 1:size(csets, 1)
    [prop, values] = csets{k, :};
    for j = 1:numel(values)
        oc = compiler.build.ExcelClientForProductionServerOptions(one, archive);
        k0_try(sprintf('cset.%s.%d', prop, j), @() setAndRead(oc, prop, values{j}));
    end
end
k0_try('copts.saveload', @() saveLoad(compiler.build.ExcelClientForProductionServerOptions(few, archive, 'AddInName', 'k17csl', 'ReplaceNaNToZeroInExcel', 'on')));
k0_try('cbuild.noargs', @() compiler.build.excelClientForProductionServer());
k0_try('cbuild.onefile', @() compiler.build.excelClientForProductionServer(one));
k0_try('cbuild.addinresults', @() compiler.build.excelClientForProductionServer(rAddin));
k0_try('cbuild.optsplus', @() compiler.build.excelClientForProductionServer(compiler.build.ExcelClientForProductionServerOptions(one, archive), 'Verbose', 'on'));

cbuilds = {
    'fromresults', {rArch, 'AddInName', 'k17mps', 'ClassName', 'K17Mps', 'Verbose', 'on'}
    'fromfiles',   {files, archive, 'AddInName', 'k17mpsf', 'ClassName', 'K17MpsF', 'Verbose', 'on'}
    'cdefaults',   {rArch}
    'nobas',       {few, archive, 'AddInName', 'k17cnb', 'ClassName', 'K17Cnb', 'GenerateVisualBasicFile', 'off'}
    'cdebug',      {few, archive, 'AddInName', 'k17cdbg', 'ClassName', 'K17Cdbg', 'DebugBuild', 'on', 'Verbose', 'on'}
    'cv2',         {few, archive, 'AddInName', 'k17cv2', 'ClassName', 'K17Cv2', 'AddInVersion', '2'}
    'cv05',        {few, archive, 'AddInName', 'k17cv05', 'ClassName', 'K17Cv05', 'AddInVersion', '0.5'}
    'cvtext',      {few, archive, 'AddInName', 'k17cvt', 'ClassName', 'K17Cvt', 'AddInVersion', 'abc'}
    'blank',       {few, archive, 'AddInName', 'k17blank', 'ClassName', 'K17Blank', 'ReplaceExcelBlankWithNaN', 'on', 'Verbose', 'on'}
    'date',        {few, archive, 'AddInName', 'k17date', 'ClassName', 'K17Date', 'ConvertExcelDateToString', 'on', 'Verbose', 'on'}
    'nan',         {few, archive, 'AddInName', 'k17nan', 'ClassName', 'K17Nan', 'ReplaceNaNToZeroInExcel', 'on', 'Verbose', 'on'}
    'numdate',     {few, archive, 'AddInName', 'k17nd', 'ClassName', 'K17Nd', 'ConvertNumericOutToDateInExcel', 'on', 'Verbose', 'on'}
    'blankdate',   {few, archive, 'AddInName', 'k17bd', 'ClassName', 'K17Bd', 'ReplaceExcelBlankWithNaN', 'on', 'ConvertExcelDateToString', 'on', 'Verbose', 'on'}
    'allswitches', {few, archive, 'AddInName', 'k17all', 'ClassName', 'K17All', 'ReplaceExcelBlankWithNaN', 'on', 'ConvertExcelDateToString', 'on', 'ReplaceNaNToZeroInExcel', 'on', 'ConvertNumericOutToDateInExcel', 'on', 'Verbose', 'on'}
    'onefileall',  {one, archive, 'AddInName', 'k17one', 'ClassName', 'K17One', 'ReplaceNaNToZeroInExcel', 'on', 'ConvertNumericOutToDateInExcel', 'on', 'Verbose', 'on'}
    'cpfile',      {{pfile, one}, archive, 'AddInName', 'k17cp', 'ClassName', 'K17Cp'}
    };
for k = 1:size(cbuilds, 1)
    [key, args] = cbuilds{k, :};
    d = fullfile(c, key);
    R.(key) = apiBuild(key, @() compiler.build.excelClientForProductionServer(args{:}, 'OutputDir', d), d);
    printText(key, d);
    reg = regDelta(key, reg);
end

% mcc -W mpsxl.
mpsxls = {
    'mpsxl_default',  {'-W', 'mpsxl:k17x,K17X,version=1.0', '-T', 'link:lib', '-b', one}
    'mpsxl_noversion', {'-W', 'mpsxl:k17xnv,K17Xnv', '-T', 'link:lib', one}
    'mpsxl_noclass',  {'-W', 'mpsxl:k17xnc', '-T', 'link:lib', one}
    'mpsxl_empty',    {'-W', 'mpsxl:', '-T', 'link:lib', one}
    'mpsxl_group',    {'-W', 'mpsxl:k17xg,K17Xg,version=1.0', '-T', 'link:lib', '-b', files{2}, files{3}, 'class{K17Xg:k17_none.m -replaceBlankWithNaN -convertDateToString,k17_one.m -replaceNaNWithZero -convertNumericToDate}'}
    'mpsxl_groupbug', {'-W', 'mpsxl:k17xgb,K17Xgb,version=1.0', '-T', 'link:lib', '-b', files{2}, files{3}, 'class{K17Xgb:k17_none.m -replaceBlankWithNaN,k17_one.m -replaceBlankWithNaN}'}
    'mpsxl_badswitch', {'-W', 'mpsxl:k17xbs,K17Xbs,version=1.0', '-T', 'link:lib', one, 'class{K17Xbs:k17_one.m -noSuchSwitch}'}
    'mpsxl_bundle',   {'-B', 'cmpsxl:k17xb,K17Xb,1.0', one}
    'mpsxl_debug',    {'-W', 'mpsxl:k17xd,K17Xd,version=1.0', '-T', 'link:lib', '-g', one}
    'mpsxl_notarget', {'-W', 'mpsxl:k17xnt,K17Xnt,version=1.0', one}
    };
x = fullfile(work, 'x');
for k = 1:size(mpsxls, 1)
    [key, args] = mpsxls{k, :};
    d = fullfile(x, key);
    mkdir(d);
    lastwarn('', '');
    t = tic;
    try
        txt = quietMcc([args, {'-d', d}]);
        fprintf('%s\tok\n', key);
        printLines(txt);
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    listFiles(key, d);
    fprintf('%s.seconds\t%.1f\n', key, toc(t));
    k0_warned(key);
    reg = regDelta(key, reg);
end
printText('mpsxl_default', fullfile(x, 'mpsxl_default'));
printText('mpsxl_group', fullfile(x, 'mpsxl_group'));

%% 5. Packaging the Excel client.
rClient = R.fromresults;
clientDll = fullfile(c, 'fromresults', 'k17mps.dll');
clientBas = fullfile(c, 'fromresults', 'k17mps.bas');
[xx, yy] = meshgrid(linspace(0, 1, 64));
imwrite(uint8(255 * cat(3, xx, yy, 1 - xx)), fullfile(src, 'k17_icon.png'));
imwrite(uint8(zeros(8, 8)), gray(2), fullfile(src, 'k17_anim.gif'), 'LoopCount', Inf);
imwrite(uint8(ones(8, 8)), gray(2), fullfile(src, 'k17_anim.gif'), 'WriteMode', 'append');
pem = fullfile(src, 'k17cert.pem');
writeText(pem, sprintf('-----BEGIN CERTIFICATE-----\nMIIBszCCAVmgAwIBAgIUK17\n-----END CERTIFICATE-----\n'));
fprintf('license.java\t%d\n', license('test', 'MATLAB_Builder_for_Java'));
k0_try('popts.results', @() showText(compiler.package.ExcelClientForProductionServerOptions(rClient)));
k0_try('popts.files', @() showText(compiler.package.ExcelClientForProductionServerOptions(clientDll, archive)));
k0_try('popts.properties', @() strjoin(properties(compiler.package.ExcelClientForProductionServerOptions(clientDll, archive))', ' | '));
k0_try('popts.noargs', @() compiler.package.ExcelClientForProductionServerOptions());
k0_try('popts.onearg', @() compiler.package.ExcelClientForProductionServerOptions(clientDll));
k0_try('popts.addinresults', @() compiler.package.ExcelClientForProductionServerOptions(rAddin));
k0_try('popts.archresults', @() compiler.package.ExcelClientForProductionServerOptions(rArch));
k0_try('popts.missingdll', @() compiler.package.ExcelClientForProductionServerOptions(fullfile(c, 'nosuch.dll'), archive));
k0_try('popts.txtdll', @() compiler.package.ExcelClientForProductionServerOptions(fullfile(src, 'k17data.txt'), archive));
k0_try('popts.txtarchive', @() compiler.package.ExcelClientForProductionServerOptions(clientDll, fullfile(src, 'k17data.txt')));
k0_try('popts.nv', @() showText(compiler.package.ExcelClientForProductionServerOptions(rClient, 'ServerURL', 'http://localhost:9910', 'MaxResponseSize', 10, 'ServerTimeOut', 5, 'Version', '2.1', 'InstallerName', 'K17Setup', 'InstallerIcon', fullfile(src, 'k17_icon.png'), 'SSLCertificate', pem, 'OutputDir', fullfile(work, 'p', 'nv'))));
psets = {
    'Version', {'2', '2.1.3', 'a.b', '', 3, '1.0-beta', ' 1.0'}
    'ServerTimeOut', {5, 0, 1.5, -1, '5', Inf}
    'MaxResponseSize', {10, 0, 1.5, -1, '10', Inf, NaN}
    'ServerURL', {'http://localhost:9910', 'nonsense', '', 5, "https://h:1"}
    'SSLCertificate', {pem, 'nosuch.pem', '', 5, ""}
    'InstallerName', {'K17Setup', 'a b', '1abc', '', 5}
    'InstallerIcon', {fullfile(src, 'k17_icon.png'), fullfile(src, 'k17_anim.gif'), 'nosuch.png', ''}
    'ClientLibrary', {clientDll, fullfile(src, 'k17data.txt'), 'nosuch.dll', fullfile(c, 'fromresults', 'k17mps')}
    'ServerArchive', {archive, fullfile(src, 'k17data.txt'), 'nosuch.ctf', fullfile(c, 'arch', 'k17arch')}
    'OutputDir', {fullfile(work, 'p', 'set'), 'rel', ''}
    };
for k = 1:size(psets, 1)
    [prop, values] = psets{k, :};
    for j = 1:numel(values)
        try
            po = compiler.package.ExcelClientForProductionServerOptions(clientDll, archive);
        catch ME
            fprintf('pset.%s.%d\tERR %s | %s\n', prop, j, ME.identifier, k0_flat(ME.message));
            continue
        end
        k0_try(sprintf('pset.%s.%d', prop, j), @() setAndRead(po, prop, values{j}));
    end
end
k0_try('popts.saveload', @() saveLoadP(compiler.package.ExcelClientForProductionServerOptions(clientDll, archive, 'ServerURL', 'http://h:1', 'Version', '3')));

p = fullfile(work, 'p');
packages = {
    'pkg.default',  @() compiler.package.excelClientForProductionServer(rClient, 'OutputDir', fullfile(p, 'default')), fullfile(p, 'default')
    'pkg.named',    @() compiler.package.excelClientForProductionServer(rClient, 'ServerURL', 'http://localhost:9910', 'MaxResponseSize', 10, 'ServerTimeOut', 5, 'Version', '2.1', 'InstallerName', 'K17Setup', 'InstallerIcon', fullfile(src, 'k17_icon.png'), 'OutputDir', fullfile(p, 'named')), fullfile(p, 'named')
    'pkg.cert',     @() compiler.package.excelClientForProductionServer(rClient, 'SSLCertificate', pem, 'ServerURL', 'https://localhost:9911', 'OutputDir', fullfile(p, 'cert')), fullfile(p, 'cert')
    'pkg.files',    @() compiler.package.excelClientForProductionServer({clientDll, clientBas}, clientDll, archive, 'OutputDir', fullfile(p, 'files')), fullfile(p, 'files')
    'pkg.filesplus', @() compiler.package.excelClientForProductionServer({clientDll, clientBas, fullfile(src, 'k17data.txt'), src}, clientDll, archive, 'OutputDir', fullfile(p, 'filesplus')), fullfile(p, 'filesplus')
    'pkg.nolibrary', @() compiler.package.excelClientForProductionServer({clientBas}, clientDll, archive, 'OutputDir', fullfile(p, 'nolibrary')), fullfile(p, 'nolibrary')
    'pkg.options',  @() compiler.package.excelClientForProductionServer(rClient, 'Options', compiler.package.ExcelClientForProductionServerOptions(rClient, 'ServerURL', 'http://o:1', 'OutputDir', fullfile(p, 'options'))), fullfile(p, 'options')
    'pkg.optionsplus', @() compiler.package.excelClientForProductionServer(rClient, 'Options', compiler.package.ExcelClientForProductionServerOptions(rClient), 'Version', '2'), fullfile(p, 'optionsplus')
    'pkg.addinresults', @() compiler.package.excelClientForProductionServer(rAddin, 'OutputDir', fullfile(p, 'addin')), fullfile(p, 'addin')
    'pkg.noargs',   @() compiler.package.excelClientForProductionServer(), ''
    'pkg.repeat',   @() compiler.package.excelClientForProductionServer({clientDll, fullfile(c, 'fromfiles', 'k17mpsf.bas'), clientBas}, clientDll, archive, 'OutputDir', fullfile(p, 'repeat')), fullfile(p, 'repeat')
    'pkg.nooutdir', @() compiler.package.excelClientForProductionServer(rClient), fullfile(src, 'k17mpsExcelClientInstaller')
    };
for k = 1:size(packages, 1)
    [key, f, d] = packages{k, :};
    package(key, f, d);
end
reg = regDelta('packaging', reg); %#ok<NASGU>

%% 6. What the recorder unregisters.
dlls = dir(fullfile(work, '**', '*.dll'));
for k = 1:numel(dlls)
    fprintf('built.dll\t%s\n', fullfile(dlls(k).folder, dlls(k).name));
end
end

%% ---- helpers ------------------------------------------------------------------------------------

function writeText(file, text)
fid = fopen(file, 'w', 'n', 'UTF-8');
fprintf(fid, '%s', text);
fclose(fid);
end

function nargout2(f)
[a, b] = f(); %#ok<ASGLU>
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
if ~ischar(v), v = strtrim(evalc('disp(v)')); end
end

function t = describe(v)
if ischar(v)
    if size(v, 1) > 1, t = sprintf('char %dx%d', size(v, 1), size(v, 2)); else, t = ['''' v '''']; end
elseif isstring(v)
    t = sprintf('string[%s]', strjoin(cellstr(v), ','));
elseif iscell(v)
    t = sprintf('cell{%s}', strjoin(cellfun(@describe, v, 'UniformOutput', false), ','));
elseif isempty(v)
    t = sprintf('%s empty', class(v));
elseif isnumeric(v) || islogical(v)
    t = sprintf('%s %s', class(v), mat2str(v));
else
    t = class(v);
end
end

function v = getp(o, p)
v = o.(p);
end

function t = showText(o)
t = regexprep(evalc('disp(o)'), '<a [^>]*>|</a>', '');
t = strtrim(regexprep(t, '\s*\n\s*', ' | '));
end

function v = saveLoad(o)
f = [tempname '.mat'];
save(f, 'o');
s = load(f);
delete(f);
v = sprintf('%s | %s | %s | %s | %s', class(s.o), s.o.AddInName, s.o.ClassName, s.o.AddInVersion, describe(s.o.FunctionFiles));
end

function v = saveLoadP(o)
f = [tempname '.mat'];
save(f, 'o');
s = load(f);
delete(f);
v = sprintf('%s | %s | %s | %s', class(s.o), s.o.InstallerName, s.o.ServerURL, s.o.Version);
end

function t = whichGuids(a, b)
% Which of the interface, library and class ids two IDL files share, in the IDL's order.
pat = '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}';
ga = regexp(fileread(a), pat, 'match');
gb = regexp(fileread(b), pat, 'match');
names = {'interface', 'library', 'coclass'};
parts = cell(1, min(numel(ga), numel(names)));
for k = 1:numel(parts)
    if any(strcmpi(gb, ga{k})), parts{k} = [names{k} '=same']; else, parts{k} = [names{k} '=differs']; end
end
t = strjoin(parts, ' ');
end

function same = sameGuids(a, b)
ga = regexp(fileread(a), '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}', 'match');
gb = regexp(fileread(b), '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}', 'match');
same = sprintf('%d guids, %d shared', numel(ga), numel(intersect(ga, gb)));
end

function r = apiBuild(key, f, d)
r = [];
lastwarn('', '');
t = tic;
try
    txt = evalc('r = f();');
    if ~isempty(strtrim(txt))
        fprintf('%s.printed\n', key);
        printLines(txt);
    end
    show([key '.results'], r);
    fprintf('%s.BuildType\t%s\n', key, r.BuildType);
    files = cellfun(@(p) strrep(p, [d filesep], '<OUT>\'), r.Files, 'UniformOutput', false);
    fprintf('%s.Files\t%s\n', key, strjoin(files(:)', ' | '));
    show([key '.Options'], r.Options);
    show([key '.RuntimeDependencies'], r.RuntimeDependencies);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
listFiles(key, d);
fprintf('%s.seconds\t%.1f\n', key, toc(t));
k0_warned(key);
end

function package(key, f, d)
% One packaging call: what it printed, its answer or refusal, its warning, and the folder's files.
lastwarn('', '');
t = tic;
try
    txt = evalc('f();');
    if ~isempty(strtrim(txt))
        fprintf('%s.printed\n', key);
        printLines(txt);
    end
    fprintf('%s\tok\n', key);
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
k0_warned(key);
if ~isempty(d), listFiles(key, d); end
fprintf('%s.seconds\t%.1f\n', key, toc(t));
end

function printText(key, d)
% Every generated text file of a build but its archive and the MIDL output.
if ~exist(d, 'dir'), return, end
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
for k = 1:numel(listing)
    [~, ~, ext] = fileparts(listing(k).name);
    if any(strcmpi(ext, {'.bas', '.bat', '.txt', '.json', '.log', '.html', '.idl', '.hpp', '.cpp', '.def', '.rc', '.cs', '.config', '.xml', '.manifest', '.vb'}))
        rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
        printFile([key '.file.' rel], fullfile(listing(k).folder, listing(k).name));
    end
end
end

function listFiles(key, d)
if ~exist(d, 'dir')
    fprintf('%s.files\t(no folder)\n', key);
    return
end
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
names = {};
for k = 1:numel(listing)
    rel = strrep(fullfile(listing(k).folder, listing(k).name), [d filesep], '');
    names{end + 1} = sprintf('%s (%d)', rel, listing(k).bytes); %#ok<AGROW>
end
fprintf('%s.files\t%s\n', key, strjoin(sort(names), ' | '));
end

function printFile(key, file)
if ~exist(file, 'file')
    fprintf('%s\tmissing\n', key);
    return
end
bytes = fileread(file);
fprintf('%s\t%d chars, CRLF %d, ends with newline %d\n', key, numel(bytes), count(bytes, sprintf('\r\n')), endsWith(bytes, newline));
printLines(bytes);
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

function show(key, v)
txt = evalc('disp(v)');
txt = regexprep(txt, '<a [^>]*>|</a>', '');
fprintf('%s\n', key);
lines = splitlines(txt);
for j = 1:numel(lines)
    if strlength(strtrim(lines{j})) > 0, fprintf('  %s\n', lines{j}); end
end
end

function txt = quietMcc(args)
% evalc in a named function: a lambda's workspace holds only what its body names.
txt = evalc('mcc(args{:})');
end

%% ---- the user's registry, through reg.exe ----------------------------------------------------------
% (MATLAB's .NET does not resolve Microsoft.Win32.Registry, so reg.exe lists the keys.)

function roots = regRoots()
roots = {'Software\Classes', 'Software\Classes\CLSID', 'Software\Classes\TypeLib', 'Software\Classes\Interface', ...
    'Software\Classes\Record', 'Software\Classes\AppID', 'Software\Classes\Component Categories', ...
    'Software\Classes\Wow6432Node\CLSID', 'Software\MathWorks', 'Software\Microsoft\Office\16.0\Excel'};
end

function m = regNames()
% The subkey names under each root, for the deltas.
roots = regRoots();
m = containers.Map('KeyType', 'char', 'ValueType', 'any');
for k = 1:numel(roots)
    m(roots{k}) = subkeyNames(roots{k});
end
end

function names = subkeyNames(path)
names = {};
[status, out] = regQuery(sprintf('reg query "HKCU\\%s" 2>nul', path));
if status ~= 0, return, end
prefix = ['HKEY_CURRENT_USER\' path '\'];
lines = splitlines(out);
for k = 1:numel(lines)
    line = strtrim(lines{k});
    if startsWith(line, prefix, 'IgnoreCase', true) && numel(line) > numel(prefix)
        names{end + 1} = line(numel(prefix) + 1:end); %#ok<AGROW>
    end
end
names = sort(names);
end

function now = regDelta(label, before)
% What a step added to or removed from the user's registry, the added keys dumped whole.
now = regNames();
roots = regRoots();
for k = 1:numel(roots)
    root = roots{k};
    added = setdiff(now(root), before(root));
    removed = setdiff(before(root), now(root));
    % A child root's additions are listed under the child, not twice.
    children = roots(startsWith(roots, [root '\']));
    for c = 1:numel(children)
        sub = children{c}(numel(root) + 2:end);
        if ~contains(sub, '\')
            added = setdiff(added, sub);
            removed = setdiff(removed, sub);
        end
    end
    if isempty(added) && isempty(removed), continue, end
    fprintf('%s.registry.%s\tadded %d, removed %d\n', label, strrep(root, '\', '/'), numel(added), numel(removed));
    for a = 1:numel(added)
        dumpKey([root '\' added{a}]);
    end
    for a = 1:numel(removed)
        fprintf('  |- %s\n', strrep([root '\' removed{a}], '\', '/'));
    end
end
end

function [status, out] = regQuery(command)
% reg.exe's answer, or a failure where the engine runs no shell command (JGraph gives a script no
% process execution, so under jgraph -batch every registry delta is empty; run-k17.ps1 reads the
% registry around the whole run instead once stage K17c registers anything).
try
    [status, out] = system(command);
catch
    status = 1;
    out = '';
end
end

function dumpKey(path)
% The key and everything under it, as reg query /s prints it.
[status, out] = regQuery(sprintf('reg query "HKCU\\%s" /s 2>nul', path));
if status ~= 0
    fprintf('  |+ %s (unreadable)\n', strrep(path, '\', '/'));
    return
end
lines = splitlines(out);
for k = 1:numel(lines)
    line = lines{k};
    if isempty(strtrim(line)), continue, end
    if startsWith(line, 'HKEY_CURRENT_USER\')
        fprintf('  |+ %s\n', strrep(line(numel('HKEY_CURRENT_USER\') + 1:end), '\', '/'));
    else
        fprintf('  |    %s\n', strtrim(line));
    end
end
end

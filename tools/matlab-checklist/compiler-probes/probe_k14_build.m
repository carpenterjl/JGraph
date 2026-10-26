function probe_k14_build(work)
%PROBE_K14_BUILD Builds stage K14's Production Server archives (compiler and SDK plan) with
%compiler.build.productionServerArchive and mcc -W CTF, and prints what each build wrote and
%refused. Run under matlab -batch -noFigureWindows over stage K14's functions (src\k14); nothing
%built is run here (record-k14.ps1 lists each archive's entries and runs the server probe). One
%key<TAB>value line per question, a display or a file's text as indented lines under its key.
beep('off'); % the refusals below would each beep
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k14', '*'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old));
fn = @(names) cellfun(@(n) fullfile(src, [n '.m']), names, 'UniformOutput', false);
mainNames = {'k14add', 'k14echo', 'k14multi', 'k14err', 'k14noout', 'k14print', 'k14slow', 'k14nargs', ...
    'k14state', 'k14quiet', 'k14describe', 'k14make'};
sig = fullfile(src, 'k14sig.json');
routes = fullfile(src, 'k14routes.json');
put(src, 'k14script.m', {'% A script.', 'k14v = 1;'});
put(src, 'k14notjson.json', {'{"this is": not json'});
put(src, 'k14noschema.json', {'{"k14add":{"inputs":[{"name":"a"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14oldschema.json', {'{"_schemaVersion":"1.0.0","k14add":{"inputs":[{"name":"a"},{"name":"b"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14newschema.json', {'{"_schemaVersion":"9.0.0","k14add":{"inputs":[{"name":"a"},{"name":"b"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14badprop.json', {'{"_schemaVersion":"1.1.0","k14add":{"inputs":[{"name":"a","colour":"red"},{"name":"b"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14badtype.json', {'{"_schemaVersion":"1.1.0","k14add":{"inputs":[{"name":"a","type":["nosuchtype"]},{"name":"b"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14badsize.json', {'{"_schemaVersion":"1.1.0","k14add":{"inputs":[{"name":"a","type":["double","size=x"]},{"name":"b"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14wrongcount.json', {'{"_schemaVersion":"1.1.0","k14add":{"inputs":[{"name":"a"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14nosuchfn.json', {'{"_schemaVersion":"1.1.0","k14nosuch":{"inputs":[{"name":"a"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14extrafn.json', {'{"_schemaVersion":"1.1.0","k14add":{"inputs":[{"name":"a"},{"name":"b"}],"outputs":[{"name":"c"}]},"k14multi":{"inputs":[{"name":"x"}],"outputs":[{"name":"a"},{"name":"b"},{"name":"c"}]}}'});
put(src, 'k14typedef.json', {'{"_schemaVersion":"1.1.0","_typedefs":{"pt":{"purpose":"a point","type":"struct","fields":[{"name":"x","type":"double"},{"name":"y","type":"double"}]}},"k14add":{"inputs":[{"name":"a","type":"struct:pt"},{"name":"b"}],"outputs":[{"name":"c"}]}}'});
put(src, 'k14array.json', {'[1,2]'});
put(src, 'k14emptyobj.json', {'{}'});
put(src, 'k14routes_noversion.json', {'{"pathmap":[{"match":"^/a$","webhandler":{"component":"k14arch","function":"k14add"}}]}'});
put(src, 'k14routes_badversion.json', {'{"version":"2.0.0","pathmap":[{"match":"^/a$","webhandler":{"component":"k14arch","function":"k14add"}}]}'});
put(src, 'k14routes_nopathmap.json', {'{"version":"1.0.0"}'});
put(src, 'k14routes_emptymap.json', {'{"version":"1.0.0","pathmap":[]}'});
put(src, 'k14routes_nomatch.json', {'{"version":"1.0.0","pathmap":[{"webhandler":{"component":"k14arch","function":"k14add"}}]}'});
put(src, 'k14routes_nohandler.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$"}]}'});
put(src, 'k14routes_nofunction.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"component":"k14arch"}}]}'});
put(src, 'k14routes_extra.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a$","webhandler":{"component":"k14arch","function":"k14add"},"extra":1}],"more":2}'});
put(src, 'k14routes_badregex.json', {'{"version":"1.0.0","pathmap":[{"match":"^/a[$","webhandler":{"component":"k14arch","function":"k14add"}}]}'});
put(src, 'k14routes_notarray.json', {'{"version":"1.0.0","pathmap":{"match":"^/a$","webhandler":{"component":"k14arch","function":"k14add"}}}'});
put(src, 'k14routes_notjson.json', {'{"version":'});

% The main build: the archive the server probe's functions would be deployed as.
d = fullfile(work, 'main');
o = compiler.build.ProductionServerArchiveOptions(fn(mainNames), 'ArchiveName', 'k14arch', 'OutputDir', d, 'Verbose', 'on');
show('main.options', o);
apiBuild('main', @() compiler.build.productionServerArchive(o), d);
printText('main', d);

% Builds of other shapes, each into a folder of its own.
builds = {
    'defaults',    {fullfile(src, 'k14add.m')}
    'twofiles',    {fn({'k14add', 'k14multi'})}
    'signatures',  {fn({'k14add', 'k14multi'}), 'ArchiveName', 'k14sig', 'FunctionSignatures', sig}
    'routes',      {fullfile(src, 'k14add.m'), 'ArchiveName', 'k14rt', 'RoutesFile', routes}
    'both',        {fn({'k14add', 'k14multi'}), 'ArchiveName', 'k14both', 'FunctionSignatures', sig, 'RoutesFile', routes}
    'additional',  {fullfile(src, 'k14add.m'), 'ArchiveName', 'k14ad', 'AdditionalFiles', routes}
    'script',      {fullfile(src, 'k14script.m')}
    'extrafn',     {fullfile(src, 'k14add.m'), 'ArchiveName', 'k14ex', 'FunctionSignatures', fullfile(src, 'k14extrafn.json')}
    'typedef',     {fn({'k14add', 'k14multi'}), 'ArchiveName', 'k14td', 'FunctionSignatures', fullfile(src, 'k14typedef.json')}
    'nodata',      {fullfile(src, 'k14add.m'), 'ArchiveName', 'k14nd', 'AutoDetectDataFiles', 'off'}
    'obfuscate',   {fullfile(src, 'k14add.m'), 'ArchiveName', 'k14ob', 'ObfuscateArchive', 'on'}
    'upper',       {fullfile(src, 'k14add.m'), 'ArchiveName', 'K14Upper'}
    'underscore',  {fullfile(src, 'k14add.m'), 'ArchiveName', '_k14u'}
    'digit',       {fullfile(src, 'k14add.m'), 'ArchiveName', '1k14'}
    'dash',        {fullfile(src, 'k14add.m'), 'ArchiveName', 'k14-d'}
    'dot',         {fullfile(src, 'k14add.m'), 'ArchiveName', 'k14.d'}
    };
for k = 1:size(builds, 1)
    [key, args] = builds{k, :};
    d = fullfile(work, key);
    apiBuild(key, @() compiler.build.productionServerArchive(args{:}, 'OutputDir', d), d);
end
printFile('defaults.readme', fullfile(work, 'defaults', 'readme.txt'));
printFile('signatures.readme', fullfile(work, 'signatures', 'readme.txt'));

% A build left to its own output folder: .\<name>productionServerArchive under the current folder.
t = tic;
try
    r = compiler.build.productionServerArchive(fullfile(src, 'k14add.m'));
    fprintf('nooutdir.Files\t%s\n', strjoin(strrep(r.Files(:)', [src filesep], '<SRC>\'), ' | '));
    listFiles('nooutdir', fullfile(src, 'k14addproductionServerArchive'));
catch ME
    fprintf('nooutdir\tERR %s | %s\n', ME.identifier, k0_flat(ME.message));
end
fprintf('nooutdir.seconds\t%.1f\n', toc(t));

% mcc -W CTF.
mccs = {
    'mcc_default',    {'-W', 'CTF:k14m', '-U', 'k14add.m', 'k14multi.m'}
    'mcc_nou',        {'-W', 'CTF:k14nu', 'k14add.m'}
    'mcc_noname',     {'-W', 'CTF:', '-U', 'k14add.m'}
    'mcc_nocolon',    {'-W', 'CTF', '-U', 'k14add.m'}
    'mcc_lower',      {'-W', 'ctf:k14lc', '-U', 'k14add.m'}
    'mcc_discovery',  {'-W', ['CTF:k14ds,DISCOVERY:' sig], '-U', 'k14add.m', 'k14multi.m'}
    'mcc_routes',     {'-W', ['CTF:k14rt,ROUTES:' routes], '-U', 'k14add.m'}
    'mcc_both',       {'-W', ['CTF:k14bt,DISCOVERY:' sig ',ROUTES:' routes], '-U', 'k14add.m', 'k14multi.m'}
    'mcc_both_rev',   {'-W', ['CTF:k14br,ROUTES:' routes ',DISCOVERY:' sig], '-U', 'k14add.m', 'k14multi.m'}
    'mcc_disc_rel',   {'-W', 'CTF:k14dr,DISCOVERY:k14sig.json', '-U', 'k14add.m', 'k14multi.m'}
    'mcc_disc_missing', {'-W', 'CTF:k14dm,DISCOVERY:nosuch.json', '-U', 'k14add.m'}
    'mcc_disc_nofile', {'-W', 'CTF:k14dn,DISCOVERY', '-U', 'k14add.m'}
    'mcc_disc_empty', {'-W', 'CTF:k14de,DISCOVERY:', '-U', 'k14add.m'}
    'mcc_disc_notjson', {'-W', 'CTF:k14dj,DISCOVERY:k14notjson.json', '-U', 'k14add.m'}
    'mcc_disc_noschema', {'-W', 'CTF:k14dh,DISCOVERY:k14noschema.json', '-U', 'k14add.m'}
    'mcc_disc_oldschema', {'-W', 'CTF:k14do,DISCOVERY:k14oldschema.json', '-U', 'k14add.m'}
    'mcc_disc_newschema', {'-W', 'CTF:k14dw,DISCOVERY:k14newschema.json', '-U', 'k14add.m'}
    'mcc_disc_badprop', {'-W', 'CTF:k14dp,DISCOVERY:k14badprop.json', '-U', 'k14add.m'}
    'mcc_disc_badtype', {'-W', 'CTF:k14dt,DISCOVERY:k14badtype.json', '-U', 'k14add.m'}
    'mcc_disc_badsize', {'-W', 'CTF:k14dz,DISCOVERY:k14badsize.json', '-U', 'k14add.m'}
    'mcc_disc_wrongcount', {'-W', 'CTF:k14dc,DISCOVERY:k14wrongcount.json', '-U', 'k14add.m'}
    'mcc_disc_nosuchfn', {'-W', 'CTF:k14df,DISCOVERY:k14nosuchfn.json', '-U', 'k14add.m'}
    'mcc_disc_extrafn', {'-W', 'CTF:k14dx,DISCOVERY:k14extrafn.json', '-U', 'k14add.m'}
    'mcc_disc_typedef', {'-W', 'CTF:k14dy,DISCOVERY:k14typedef.json', '-U', 'k14add.m', 'k14multi.m'}
    'mcc_disc_array', {'-W', 'CTF:k14da,DISCOVERY:k14array.json', '-U', 'k14add.m'}
    'mcc_disc_emptyobj', {'-W', 'CTF:k14db,DISCOVERY:k14emptyobj.json', '-U', 'k14add.m'}
    'mcc_disc_twice',  {'-W', ['CTF:k14d2,DISCOVERY:' sig ',DISCOVERY:k14extrafn.json'], '-U', 'k14add.m'}
    'mcc_routes_missing', {'-W', 'CTF:k14rm,ROUTES:nosuch.json', '-U', 'k14add.m'}
    'mcc_routes_nofile', {'-W', 'CTF:k14rn,ROUTES', '-U', 'k14add.m'}
    'mcc_routes_empty', {'-W', 'CTF:k14re,ROUTES:', '-U', 'k14add.m'}
    'mcc_routes_notjson', {'-W', 'CTF:k14rj,ROUTES:k14routes_notjson.json', '-U', 'k14add.m'}
    'mcc_routes_noversion', {'-W', 'CTF:k14rv,ROUTES:k14routes_noversion.json', '-U', 'k14add.m'}
    'mcc_routes_badversion', {'-W', 'CTF:k14rb,ROUTES:k14routes_badversion.json', '-U', 'k14add.m'}
    'mcc_routes_nopathmap', {'-W', 'CTF:k14rp,ROUTES:k14routes_nopathmap.json', '-U', 'k14add.m'}
    'mcc_routes_emptymap', {'-W', 'CTF:k14ry,ROUTES:k14routes_emptymap.json', '-U', 'k14add.m'}
    'mcc_routes_nomatch', {'-W', 'CTF:k14ra,ROUTES:k14routes_nomatch.json', '-U', 'k14add.m'}
    'mcc_routes_nohandler', {'-W', 'CTF:k14rh,ROUTES:k14routes_nohandler.json', '-U', 'k14add.m'}
    'mcc_routes_nofunction', {'-W', 'CTF:k14rf,ROUTES:k14routes_nofunction.json', '-U', 'k14add.m'}
    'mcc_routes_extra', {'-W', 'CTF:k14rx,ROUTES:k14routes_extra.json', '-U', 'k14add.m'}
    'mcc_routes_badregex', {'-W', 'CTF:k14rg,ROUTES:k14routes_badregex.json', '-U', 'k14add.m'}
    'mcc_routes_notarray', {'-W', 'CTF:k14rr,ROUTES:k14routes_notarray.json', '-U', 'k14add.m'}
    'mcc_routes_twice', {'-W', ['CTF:k14r2,ROUTES:' routes ',ROUTES:k14routes_extra.json'], '-U', 'k14add.m'}
    'mcc_bogus_part', {'-W', 'CTF:k14bp,BOGUS:x', '-U', 'k14add.m'}
    'mcc_extra_part', {'-W', 'CTF:k14ep,extra', '-U', 'k14add.m'}
    'mcc_version',    {'-W', 'CTF:k14vr,version=1.0', '-U', 'k14add.m'}
    'mcc_u_alone',    {'-U', 'k14add.m'}
    'mcc_u_m',        {'-m', '-U', 'k14add.m'}
    'mcc_u_lib',      {'-W', 'lib:k14ul', '-U', 'k14add.m'}
    'mcc_o',          {'-W', 'CTF:k14on', '-U', '-o', 'k14other', 'k14add.m'}
    'mcc_ofirst',     {'-o', 'k14of', '-W', 'CTF:k14of2', '-U', 'k14add.m'}
    'mcc_tlib',       {'-W', 'CTF:k14tl', '-U', '-T', 'link:lib', 'k14add.m'}
    'mcc_texe',       {'-W', 'CTF:k14te', '-U', '-T', 'link:exe', 'k14add.m'}
    'mcc_codegen',    {'-W', 'CTF:k14cg', '-U', '-T', 'codegen', 'k14add.m'}
    'mcc_separate',   {'-W', 'CTF:k14sp', '-U', '-C', 'k14add.m'}
    'mcc_verbose',    {'-W', 'CTF:k14vb', '-U', '-v', 'k14add.m'}
    'mcc_debug',      {'-W', 'CTF:k14db', '-U', '-g', 'k14add.m'}
    'mcc_nodata',     {'-W', 'CTF:k14xd', '-U', '-X', 'k14add.m'}
    'mcc_additional', {'-W', 'CTF:k14ad', '-U', '-a', 'k14routes.json', 'k14add.m'}
    'mcc_runtime',    {'-W', 'CTF:k14ro', '-U', '-R', '-nojvm', 'k14add.m'}
    'mcc_numeric',    {'-W', 'CTF:k14nm', '-U', '-n', 'k14add.m'}
    'mcc_twice',      {'-W', 'CTF:k14t1', '-W', 'CTF:k14t2', '-U', 'k14add.m'}
    'mcc_badname',    {'-W', 'CTF:1bad', '-U', 'k14add.m'}
    'mcc_dash',       {'-W', 'CTF:a-b', '-U', 'k14add.m'}
    'mcc_dot',        {'-W', 'CTF:a.b', '-U', 'k14add.m'}
    'mcc_space',      {'-W', 'CTF:a b', '-U', 'k14add.m'}
    'mcc_script',     {'-W', 'CTF:k14sc', '-U', 'k14script.m'}
    'mcc_nofiles',    {'-W', 'CTF:k14nf', '-U'}
    'mcc_withpython', {'-W', 'CTF:k14wp', '-W', 'python:k14wp2', '-U', 'k14add.m'}
    'mcc_pcode',      {'-W', 'CTF:k14pc', '-U', '-j', 'k14add.m'}
    };
for k = 1:size(mccs, 1)
    [key, args] = mccs{k, :};
    d = fullfile(work, key);
    mkdir(d);
    lastwarn('', '');
    t = tic;
    try
        txt = quietMcc([args, {'-d', d}]);
        fprintf('%s\tok\n', key);
        printLines(txt);
        listFiles(key, d);
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
        listFiles(key, d);
    end
    fprintf('%s.seconds\t%.1f\n', key, toc(t));
    k0_warned(key);
end
printFile('mcc_default.readme', fullfile(work, 'mcc_default', 'readme.txt'));
printFile('mcc_default.requiredMCRProducts', fullfile(work, 'mcc_default', 'requiredMCRProducts.txt'));
printFile('mcc_default.mccExcludedFiles', fullfile(work, 'mcc_default', 'mccExcludedFiles.log'));

% What the options take.
f1 = fullfile(src, 'k14add.m');
o = compiler.build.ProductionServerArchiveOptions(f1);
show('opts.default', o);
k0_try('opts.properties', @() strjoin(properties(o)', ' '));
k0_try('opts.ArchiveName', @() o.ArchiveName);
k0_try('opts.getName', @() o.getName);
k0_try('opts.OutputDir', @() strrep(o.OutputDir, [src filesep], '<SRC>\'));
names = {'k14ok', '1abc', 'a-b', 'a.b', 'a b', '', repmat('a', 1, 70), 'K14', ['a' char(955)], 'a/b', 'a:b', 'a*b'};
for k = 1:numel(names)
    k0_try(sprintf('opts.name.%d', k), @() [names{k} ' -> ' setAndRead(o, 'ArchiveName', names{k})]);
end
k0_try('opts.name.string', @() setAndRead(o, 'ArchiveName', "k14str"));
k0_try('opts.name.number', @() setAndRead(o, 'ArchiveName', 3));
k0_try('opts.name.cell', @() setAndRead(o, 'ArchiveName', {'k14'}));
o2 = compiler.build.ProductionServerArchiveOptions(f1, 'ArchiveName', 'k14named');
k0_try('opts.named.OutputDir', @() strrep(o2.OutputDir, [src filesep], '<SRC>\'));
k0_try('opts.sig.ok', @() strrep(setAndRead(o, 'FunctionSignatures', sig), [src filesep], '<SRC>\'));
k0_try('opts.sig.rel', @() strrep(setAndRead(o, 'FunctionSignatures', 'k14sig.json'), [src filesep], '<SRC>\'));
k0_try('opts.sig.missing', @() setAndRead(o, 'FunctionSignatures', 'nosuch.json'));
k0_try('opts.sig.empty', @() setAndRead(o, 'FunctionSignatures', ''));
k0_try('opts.sig.emptystring', @() setAndRead(o, 'FunctionSignatures', ""));
k0_try('opts.sig.number', @() setAndRead(o, 'FunctionSignatures', 5));
k0_try('opts.sig.cell', @() setAndRead(o, 'FunctionSignatures', {sig}));
sigs = {'k14notjson', 'k14noschema', 'k14oldschema', 'k14newschema', 'k14badprop', 'k14badtype', 'k14badsize', ...
    'k14wrongcount', 'k14nosuchfn', 'k14extrafn', 'k14typedef', 'k14array', 'k14emptyobj', 'k14routes'};
for k = 1:numel(sigs)
    k0_try(['opts.sig.' sigs{k}], @() strrep(setAndRead(o, 'FunctionSignatures', fullfile(src, [sigs{k} '.json'])), [src filesep], '<SRC>\'));
end
o3 = compiler.build.ProductionServerArchiveOptions(fn({'k14add', 'k14multi'}));
k0_try('opts.sig.twofiles', @() strrep(setAndRead(o3, 'FunctionSignatures', sig), [src filesep], '<SRC>\'));
k0_try('opts.sig.twofiles.extrafn', @() strrep(setAndRead(o3, 'FunctionSignatures', fullfile(src, 'k14extrafn.json')), [src filesep], '<SRC>\'));
k0_try('opts.routes.ok', @() strrep(setAndRead(o, 'RoutesFile', routes), [src filesep], '<SRC>\'));
k0_try('opts.routes.rel', @() strrep(setAndRead(o, 'RoutesFile', 'k14routes.json'), [src filesep], '<SRC>\'));
k0_try('opts.routes.missing', @() setAndRead(o, 'RoutesFile', 'nosuch.json'));
k0_try('opts.routes.empty', @() setAndRead(o, 'RoutesFile', ''));
k0_try('opts.routes.number', @() setAndRead(o, 'RoutesFile', 5));
k0_try('opts.routes.bad', @() strrep(setAndRead(o, 'RoutesFile', fullfile(src, 'k14routes_notjson.json')), [src filesep], '<SRC>\'));
k0_try('opts.routes.sig', @() strrep(setAndRead(o, 'RoutesFile', sig), [src filesep], '<SRC>\'));
k0_try('opts.files.bad', @() compiler.build.ProductionServerArchiveOptions('nosuch.m'));
k0_try('opts.files.txt', @() compiler.build.ProductionServerArchiveOptions(fullfile(here, 'README.md')));
k0_try('opts.files.json', @() compiler.build.ProductionServerArchiveOptions(sig));
k0_try('opts.files.script', @() strjoin(strrep(compiler.build.ProductionServerArchiveOptions(fullfile(src, 'k14script.m')).FunctionFiles(:)', [src filesep], '<SRC>\'), ' '));
k0_try('opts.files.two', @() strjoin(strrep(o3.FunctionFiles(:)', [src filesep], '<SRC>\'), ' '));
k0_try('opts.files.folder', @() compiler.build.ProductionServerArchiveOptions(src));
k0_try('opts.nv.ambiguous', @() compiler.build.ProductionServerArchiveOptions(f1, 'A', 'x'));
k0_try('opts.nv.prefix', @() compiler.build.ProductionServerArchiveOptions(f1, 'ArchiveN', 'k14pre').ArchiveName);
k0_try('opts.nv.case', @() compiler.build.ProductionServerArchiveOptions(f1, 'archivename', 'k14case').ArchiveName);
k0_try('opts.nv.bogus', @() compiler.build.ProductionServerArchiveOptions(f1, 'Bogus', 1));
k0_try('opts.nv.odd', @() compiler.build.ProductionServerArchiveOptions(f1, 'ArchiveName'));
k0_try('opts.nv.Interface', @() compiler.build.ProductionServerArchiveOptions(f1, 'Interface', 'x'));
k0_try('opts.nv.EmbedArchive', @() compiler.build.ProductionServerArchiveOptions(f1, 'EmbedArchive', 'on'));
k0_try('opts.nargin', @() compiler.build.ProductionServerArchiveOptions());
k0_try('build.nargin', @() compiler.build.productionServerArchive());
k0_try('build.opts.and.nv', @() compiler.build.productionServerArchive(o, 'Verbose', 'on'));
k0_try('build.bad.nv', @() compiler.build.productionServerArchive(f1, 'Bogus', 1));
k0_try('build.wrong.opts', @() compiler.build.productionServerArchive(compiler.build.PythonPackageOptions(f1)));
k0_try('build.nargout', @() nargout2(@() compiler.build.productionServerArchive(f1, 'OutputDir', fullfile(work, 'nargout2'))));
end

function nargout2(f)
[a, b] = f(); %#ok<ASGLU>
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
if ~ischar(v), v = strtrim(evalc('disp(v)')); end
end

function apiBuild(key, f, d)
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
    fprintf('%s.Options.class\t%s\n', key, class(r.Options));
    fprintf('%s.Options.ArchiveName\t%s\n', key, r.Options.ArchiveName);
    fprintf('%s.Options.FunctionSignatures\t%s\n', key, strrep(r.Options.FunctionSignatures, [fileparts(d) filesep], '<W>\'));
    fprintf('%s.Options.RoutesFile\t%s\n', key, strrep(r.Options.RoutesFile, [fileparts(d) filesep], '<W>\'));
catch ME
    fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
end
fprintf('%s.seconds\t%.1f\n', key, toc(t));
k0_warned(key);
listFiles(key, d);
end

function printText(key, d)
% Every generated text file of a build but its archive.
if ~exist(d, 'dir'), return, end
listing = dir(fullfile(d, '**', '*'));
listing = listing(~[listing.isdir]);
for k = 1:numel(listing)
    [~, ~, ext] = fileparts(listing(k).name);
    if any(strcmpi(ext, {'.txt', '.json', '.log', '.html'}))
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
    [~, ~, ext] = fileparts(rel);
    if strcmpi(ext, '.ctf')
        names{end + 1} = rel; %#ok<AGROW>
    else
        names{end + 1} = sprintf('%s (%d)', rel, listing(k).bytes); %#ok<AGROW>
    end
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

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

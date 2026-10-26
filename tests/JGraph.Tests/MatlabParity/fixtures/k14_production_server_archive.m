% record: -noFigureWindows
% Compiler and SDK plan, stage K14: Production Server archives - compiler.build.ProductionServerArchiveOptions,
% its defaults, derived names and refusals, the function signatures and routes files it validates,
% and what compiler.build.productionServerArchive and mcc -W CTF refuse before building. Nothing is
% built here (probe_k14_build records the builds), so neither engine writes outside its temp folder.
beep('off');
root = tempname;
src = fullfile(root, 'src');
mkdir(src);
put(src, 'k14f.m', {'function [a, b] = k14f(x, n)', '%K14F A probe function.', 'a = x * n; b = numel(x);', 'end'});
put(src, 'k14g.m', {'function k14g(s)', '%K14G Nothing out.', 'disp(s);', 'end'});
put(src, 'k14s.m', {'% A script.', 'k14v = 1;'});
put(src, 'k14notes.txt', {'notes'});
put(src, 'sig.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["double"],"purpose":"x"},{"name":"n","type":[],"purpose":""}],"outputs":[{"name":"a","type":[],"purpose":""},{"name":"b","type":[],"purpose":""}],"purpose":"A probe function."}}'});
put(src, 'sig_notjson.json', {'{"this is": not json'});
put(src, 'sig_noschema.json', {'{"k14f":{"inputs":[{"name":"x"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_oldschema.json', {'{"_schemaVersion":"1.0.0","k14f":{"inputs":[{"name":"x"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_newschema.json', {'{"_schemaVersion":"9.0.0","k14f":{"inputs":[{"name":"x"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_badprop.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","colour":"red"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_badtype.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["nosuchtype"]},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_badsize.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["double","size=x"]},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_wrongcount.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_nosuchfn.json', {'{"_schemaVersion":"1.1.0","k14nosuch":{"inputs":[{"name":"x"}],"outputs":[{"name":"a"}]}}'});
put(src, 'sig_extrafn.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]},"k14g":{"inputs":[{"name":"s"}],"outputs":[]}}'});
put(src, 'sig_typedef.json', {'{"_schemaVersion":"1.1.0","_typedefs":{"pt":{"purpose":"a point","type":"struct","fields":[{"name":"x","type":"double"},{"name":"y","type":"double"}]}},"k14f":{"inputs":[{"name":"x","type":"struct:pt"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_badtypedef.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":"struct:nosuch"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_array.json', {'[1,2]'});
put(src, 'sig_empty.json', {'{}'});
put(src, 'sig_schemaonly.json', {'{"_schemaVersion":"1.1.0"}'});
put(src, 'sig_badname.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"1x"},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_noname.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"type":["double"]},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'sig_nooutputs.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x"},{"name":"n"}]}}'});
put(src, 'sig_inputs_notarray.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":{"name":"x"},"outputs":[]}}'});
put(src, 'sig_size_ok.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["double","size=1,1"]},{"name":"n","type":["double","size=1,:"]}],"outputs":[{"name":"a","type":["double","size=:,:"]},{"name":"b"}]}}'});
put(src, 'sig_types.json', {'{"_schemaVersion":"1.1.0","k14f":{"inputs":[{"name":"x","type":["double","single","int8","uint8","int16","uint16","int32","uint32","int64","uint64","logical","char","string","cell","struct"]},{"name":"n"}],"outputs":[{"name":"a"},{"name":"b"}]}}'});
put(src, 'routes.json', {'{"version":"1.0.0","pathmap":[{"match":"^/k14/f$","webhandler":{"component":"k14f","function":"k14f"}}]}'});
put(src, 'routes_notjson.json', {'{"version":'});
old = cd(src);

% The options from one file: the defaults and the names derived from it.
o = compiler.build.ProductionServerArchiveOptions('k14f.m');
u2_chk('class', @() class(o));
u2_chk('props', @() strjoin(properties(o)', ' '));
u2_chk('name', @() o.ArchiveName);
u2_chk('getname', @() o.getName);
u2_chk('outdir', @() o.OutputDir);
u2_chk('sig', @() class(o.FunctionSignatures));
u2_chk('sig_size', @() size(o.FunctionSignatures));
u2_chk('routes', @() class(o.RoutesFile));
u2_chk('files', @() rel(o.FunctionFiles, src));
u2_chk('verbose', @() char(o.Verbose));
u2_chk('additional', @() class(o.AdditionalFiles));
u2_chk('support', @() strjoin(o.SupportPackages', ' '));
u2_chk('two_name', @() compiler.build.ProductionServerArchiveOptions({'k14g.m', 'k14f.m'}).ArchiveName);
u2_chk('given_name', @() compiler.build.ProductionServerArchiveOptions('k14f.m', 'ArchiveName', 'k14named').OutputDir);
u2_chk('given_getname', @() compiler.build.ProductionServerArchiveOptions('k14f.m', 'ArchiveName', 'k14named').getName);
u2_chk('nv_prefix', @() compiler.build.ProductionServerArchiveOptions('k14f.m', 'ArchiveN', 'k14pre').ArchiveName);
u2_chk('nv_case', @() compiler.build.ProductionServerArchiveOptions('k14f.m', 'archivename', 'k14case').ArchiveName);
u2_chk('nv_ambiguous', @() err(@() compiler.build.ProductionServerArchiveOptions('k14f.m', 'A', 'x')));
u2_chk('script_file', @() rel(compiler.build.ProductionServerArchiveOptions('k14s.m').FunctionFiles, src));
u2_chk('display', @() strrep(shown(o), src, '<src>'));

% The constructor's refusals.
u2_chk('none', @() err(@() compiler.build.ProductionServerArchiveOptions()));
u2_chk('missing', @() err(@() compiler.build.ProductionServerArchiveOptions('k14nosuch.m')));
u2_chk('not_code', @() err(@() compiler.build.ProductionServerArchiveOptions('k14notes.txt')));
u2_chk('json_file', @() err(@() compiler.build.ProductionServerArchiveOptions('sig.json')));
u2_chk('folder', @() strrep(err(@() compiler.build.ProductionServerArchiveOptions({src})), src, '<src>'));
u2_chk('number', @() err(@() compiler.build.ProductionServerArchiveOptions(5)));
u2_chk('empty', @() err(@() compiler.build.ProductionServerArchiveOptions({})));
u2_chk('bogus_nv', @() err(@() compiler.build.ProductionServerArchiveOptions('k14f.m', 'Bogus', 1)));
u2_chk('odd_nv', @() err(@() compiler.build.ProductionServerArchiveOptions('k14f.m', 'ArchiveName')));
u2_chk('embed_nv', @() err(@() compiler.build.ProductionServerArchiveOptions('k14f.m', 'EmbedArchive', 'on')));
u2_chk('too_many', @() err(@() compiler.build.ProductionServerArchiveOptions('k14f.m', 'Verbose', 'on', 'Verbose', 'on', 'Verbose', 'on', ...
    'Verbose', 'on', 'Verbose', 'on', 'Verbose', 'on', 'Verbose', 'on', 'Verbose', 'on', 'Verbose', 'on', 'Verbose', 'on', 'Verbose', 'on', 'Verbose', 'on')));

% ArchiveName.
names = {'k14ok', '1abc', 'a-b', 'a.b', 'K14', 'a_b', 'abc'};
for k = 1:numel(names)
    u2_chk(sprintf('name_%d', k), @() setAndRead(o, 'ArchiveName', names{k}));
end
u2_chk('name_space', @() err(@() setAndRead(o, 'ArchiveName', 'a b')));
u2_chk('name_tab', @() err(@() setAndRead(o, 'ArchiveName', sprintf('a\tb'))));
u2_chk('name_empty', @() err(@() setAndRead(o, 'ArchiveName', '')));
u2_chk('name_slash', @() err(@() setAndRead(o, 'ArchiveName', 'a/b')));
u2_chk('name_backslash', @() err(@() setAndRead(o, 'ArchiveName', 'a\b')));
u2_chk('name_colon', @() err(@() setAndRead(o, 'ArchiveName', 'a:b')));
u2_chk('name_star', @() err(@() setAndRead(o, 'ArchiveName', 'a*b')));
u2_chk('name_string', @() setAndRead(o, 'ArchiveName', "k14str"));
u2_chk('name_number', @() err(@() setAndRead(o, 'ArchiveName', 3)));
u2_chk('name_cell', @() err(@() setAndRead(o, 'ArchiveName', {'k14'})));
u2_chk('name_matrix', @() err(@() setAndRead(o, 'ArchiveName', ['ab'; 'cd'])));
u2_chk('name_strings', @() err(@() setAndRead(o, 'ArchiveName', ["a" "b"])));
u2_chk('name_long', @() setAndRead(o, 'ArchiveName', repmat('a', 1, 70)));

% FunctionSignatures: a file that validates, or the message of one that does not.
u2_chk('sig_ok', @() rel(setAndRead(o, 'FunctionSignatures', 'sig.json'), src));
u2_chk('sig_abs', @() rel(setAndRead(o, 'FunctionSignatures', fullfile(src, 'sig.json')), src));
u2_chk('sig_string', @() rel(setAndRead(o, 'FunctionSignatures', "sig.json"), src));
u2_chk('sig_missing', @() strrep(err(@() setAndRead(o, 'FunctionSignatures', 'nosuch.json')), src, '<src>'));
u2_chk('sig_reset', @() class(setAndRead(o, 'FunctionSignatures', '')));
u2_chk('sig_reset_string', @() class(setAndRead(o, 'FunctionSignatures', "")));
u2_chk('sig_reset_empty', @() class(setAndRead(o, 'FunctionSignatures', [])));
u2_chk('sig_number', @() err(@() setAndRead(o, 'FunctionSignatures', 5)));
u2_chk('sig_cell', @() err(@() setAndRead(o, 'FunctionSignatures', {'sig.json'})));
u2_chk('sig_strings', @() err(@() setAndRead(o, 'FunctionSignatures', ["a" "b"])));
u2_chk('sig_matrix', @() err(@() setAndRead(o, 'FunctionSignatures', ['ab'; 'cd'])));
u2_chk('sig_folder', @() strrep(err(@() setAndRead(o, 'FunctionSignatures', src)), src, '<src>'));
bad = {'notjson', 'noschema', 'oldschema', 'newschema', 'badprop', 'badtype', 'badsize', 'wrongcount', 'nosuchfn', 'extrafn', ...
    'typedef', 'badtypedef', 'array', 'empty', 'schemaonly', 'badname', 'noname', 'nooutputs', 'inputs_notarray', 'size_ok', 'types'};
for k = 1:numel(bad)
    u2_chk(['sig_' bad{k}], @() strrep(sigResult(o, ['sig_' bad{k} '.json']), src, '<src>'));
end
u2_chk('sig_routes_file', @() strrep(sigResult(o, 'routes.json'), src, '<src>'));
o2 = compiler.build.ProductionServerArchiveOptions({'k14f.m', 'k14g.m'});
u2_chk('sig_two_extrafn', @() strrep(sigResult(o2, 'sig_extrafn.json'), src, '<src>'));
u2_chk('sig_two_one', @() strrep(sigResult(o2, 'sig.json'), src, '<src>'));
u2_chk('sig_nv', @() rel(compiler.build.ProductionServerArchiveOptions('k14f.m', 'FunctionSignatures', 'sig.json').FunctionSignatures, src));
u2_chk('sig_nv_bad', @() strrep(err(@() compiler.build.ProductionServerArchiveOptions('k14f.m', 'FunctionSignatures', 'sig_notjson.json')), src, '<src>'));

% RoutesFile: a file that is there; nothing is read.
u2_chk('routes_ok', @() rel(setAndRead(o, 'RoutesFile', 'routes.json'), src));
u2_chk('routes_abs', @() rel(setAndRead(o, 'RoutesFile', fullfile(src, 'routes.json')), src));
u2_chk('routes_bad_content', @() rel(setAndRead(o, 'RoutesFile', 'routes_notjson.json'), src));
u2_chk('routes_sig', @() rel(setAndRead(o, 'RoutesFile', 'sig.json'), src));
u2_chk('routes_missing', @() strrep(err(@() setAndRead(o, 'RoutesFile', 'nosuch.json')), src, '<src>'));
u2_chk('routes_reset', @() class(setAndRead(o, 'RoutesFile', '')));
u2_chk('routes_reset_empty', @() class(setAndRead(o, 'RoutesFile', [])));
u2_chk('routes_number', @() err(@() setAndRead(o, 'RoutesFile', 5)));
u2_chk('routes_cell', @() err(@() setAndRead(o, 'RoutesFile', {'routes.json'})));
u2_chk('routes_folder', @() strrep(err(@() setAndRead(o, 'RoutesFile', src)), src, '<src>'));
u2_chk('routes_nv', @() rel(compiler.build.ProductionServerArchiveOptions('k14f.m', 'RoutesFile', 'routes.json').RoutesFile, src));

% The shared options.
u2_chk('verbose_on', @() char(setAndRead(o, 'Verbose', 'on')));
u2_chk('verbose_bad', @() err(@() setAndRead(o, 'Verbose', 'maybe')));
u2_chk('outdir_rel', @() strrep(setAndRead(o, 'OutputDir', 'out'), src, '<src>'));
u2_chk('outdir_file', @() strrep(err(@() setAndRead(o, 'OutputDir', fullfile(src, 'k14f.m'))), src, '<src>'));
u2_chk('additional_missing', @() err(@() setAndRead(o, 'AdditionalFiles', 'nosuch.txt')));
u2_chk('additional_ok', @() rel(setAndRead(o, 'AdditionalFiles', 'k14notes.txt'), src));
u2_chk('files_set', @() rel(setAndRead(o, 'FunctionFiles', {'k14g.m'}), src));
u2_chk('files_set_name', @() nameAfterFiles(o));

% The build function's own refusals (before anything is written).
u2_chk('build_none', @() err(@() compiler.build.productionServerArchive()));
u2_chk('build_opts_and_nv', @() err(@() compiler.build.productionServerArchive(o, 'Verbose', 'on')));
u2_chk('build_bad_nv', @() err(@() compiler.build.productionServerArchive('k14f.m', 'Bogus', 1)));
u2_chk('build_wrong_opts', @() err(@() compiler.build.productionServerArchive(compiler.build.PythonPackageOptions('k14f.m'))));
u2_chk('build_missing', @() err(@() compiler.build.productionServerArchive('nosuch.m')));
u2_chk('build_bad_sig', @() strrep(err(@() compiler.build.productionServerArchive('k14f.m', 'FunctionSignatures', 'sig_notjson.json')), src, '<src>'));
u2_chk('build_two_out', @() err(@() twoOut(@() compiler.build.productionServerArchive('k14f.m'))));

% mcc -W CTF's refusals (before anything is written).
u2_chk('mcc_nou', @() mccErr({'-W', 'CTF:k14nu', 'k14f.m'}));
u2_chk('mcc_noname', @() mccErr({'-W', 'CTF:', '-U', 'k14f.m'}));
u2_chk('mcc_nocolon', @() mccErr({'-W', 'CTF', '-U', 'k14f.m'}));
u2_chk('mcc_disc_missing', @() mccErr({'-W', 'CTF:k14dm,DISCOVERY:nosuch.json', '-U', 'k14f.m'}));
u2_chk('mcc_disc_notjson', @() strrep(mccErr({'-W', 'CTF:k14dj,DISCOVERY:sig_notjson.json', '-U', 'k14f.m'}), src, '<src>'));
u2_chk('mcc_routes_missing', @() mccErr({'-W', 'CTF:k14rm,ROUTES:nosuch.json', '-U', 'k14f.m'}));
u2_chk('mcc_routes_twice', @() mccErr({'-W', 'CTF:k14r2,ROUTES:routes.json,ROUTES:routes.json', '-U', 'k14f.m'}));
u2_chk('mcc_o', @() mccErr({'-W', 'CTF:k14on', '-U', '-o', 'k14other', 'k14f.m'}));
u2_chk('mcc_ofirst', @() mccErr({'-o', 'k14of', '-W', 'CTF:k14of2', '-U', 'k14f.m'}));
u2_chk('mcc_space', @() mccErr({'-W', 'CTF:a b', '-U', 'k14f.m'}));
u2_chk('mcc_nofiles', @() mccErr({'-W', 'CTF:k14nf', '-U'}));
u2_chk('mcc_missing_file', @() mccErr({'-W', 'CTF:k14mf', '-U', 'nosuch.m'}));

cd(old);
rmdir(root, 's');

% --- helpers --------------------------------------------------------------------------------------
function s = err(f)
try
    f();
    s = 'no error';
catch e
    s = [e.identifier ' / ' strrep(strrep(e.message, newline, ' | '), sprintf('\r'), '')];
end
end

function s = mccErr(args)
% mcc's refusal, with the text it printed first; nothing it would write is kept.
d = tempname;
mkdir(d);
try
    txt = evalc('mcc(args{:}, ''-d'', d)');
    s = ['no error: ' strtrim(strrep(txt, newline, ' | '))];
catch e
    s = [e.identifier ' / ' strrep(strrep(e.message, newline, ' | '), sprintf('\r'), '')];
end
rmdir(d, 's');
end

function v = setAndRead(o, prop, value)
o.(prop) = value;
v = o.(prop);
end

function s = sigResult(o, file)
% What setting FunctionSignatures to file answers: the path kept, a warning, or the refusal.
lastwarn('', '');
try
    o.FunctionSignatures = file;
    [msg, id] = lastwarn;
    s = ['ok ' o.FunctionSignatures];
    if ~isempty(id) || ~isempty(msg)
        s = [s ' / warn ' id ' / ' strrep(msg, newline, ' | ')];
    end
catch e
    s = [e.identifier ' / ' strrep(strrep(e.message, newline, ' | '), sprintf('\r'), '')];
end
end

function s = nameAfterFiles(o)
o.FunctionFiles = {'k14g.m'};
s = [o.ArchiveName ' ' o.OutputDir];
end

function twoOut(f)
[a, b] = f(); %#ok<ASGLU>
end

function s = shown(o)
txt = evalc('disp(o)');
txt = regexprep(txt, '<a [^>]*>|</a>', '');
lines = strtrim(splitlines(txt));
lines = lines(~cellfun(@isempty, lines));
s = strjoin(lines', ' / ');
end

function s = rel(files, folder)
files = cellfun(@(f) strrep(f, [folder filesep], ''), cellstr(files), 'UniformOutput', false);
s = strjoin(files(:)', ' ');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

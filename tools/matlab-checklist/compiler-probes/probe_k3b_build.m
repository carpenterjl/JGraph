function probe_k3b_build(work)
%PROBE_K3B_BUILD What mcc's protection switches put in the archive (compiler and SDK plan, stage
%K3b): -s, -j, -s -j, -k with no argument, -k with a key file, and -J with a secrets manifest, and
%their refusals. Each build is mcc -m -C into its own folder under WORK, so the archive is a .ctf
%beside the program; record-k3b.ps1 runs the programs afterwards. Run under matlab -batch
%-noFigureWindows; nothing built is run here.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'k3bsrc');
mkdir(src);
copyfile(fullfile(here, 'src', 'k0_quiet.m'), src);
copyfile(fullfile(here, 'src', 'k0_show.m'), src);
copyfile(fullfile(here, 'src', 'k0_flat.m'), src);
put(src, 'k3b_main.m', {
    'function k3b_main(varargin)'
    'k0_quiet;'
    'fprintf(''which.helper\t%s\n'', strrep(which(''k3b_helper''), ctfroot, ''<CTF>''));'
    'fprintf(''exist.helper\t%d\n'', exist(''k3b_helper''));'
    'fprintf(''call.helper\t%s\n'', k0_show(k3b_helper(3)));'
    'fprintf(''mfilename\t%s\n'', strrep(mfilename(''fullpath''), ctfroot, ''<CTF>''));'
    'try'
    '    t = fileread(which(''k3b_helper''));'
    '    fprintf(''fileread.helper\t%d bytes, starts %s\n'', numel(t), k0_flat(t(1:min(12, end))));'
    'catch e'
    '    fprintf(''fileread.helper\tERR %s | %s\n'', e.identifier, k0_flat(e.message));'
    'end'
    'try'
    '    fprintf(''fileread.data\t%s\n'', k0_flat(fileread(''k3b_data.txt'')));'
    'catch e'
    '    fprintf(''fileread.data\tERR %s | %s\n'', e.identifier, k0_flat(e.message));'
    'end'
    'try'
    '    s = getSecret(''K3B_SECRET'');'
    '    fprintf(''getSecret\t%s %s\n'', class(s), s);'
    'catch e'
    '    fprintf(''getSecret\tERR %s | %s\n'', e.identifier, k0_flat(e.message));'
    'end'
    'try'
    '    fprintf(''isSecret\t%d\n'', isSecret(''K3B_SECRET''));'
    'catch e'
    '    fprintf(''isSecret\tERR %s | %s\n'', e.identifier, k0_flat(e.message));'
    'end'
    'try'
    '    s = getSecret(''K3B_OTHER'');'
    '    fprintf(''getSecret.other\t%s %s\n'', class(s), s);'
    'catch e'
    '    fprintf(''getSecret.other\tERR %s | %s\n'', e.identifier, k0_flat(e.message));'
    'end'
    'try'
    '    listSecrets'
    'catch e'
    '    fprintf(''listSecrets\tERR %s | %s\n'', e.identifier, k0_flat(e.message));'
    'end'
    'try'
    '    k3b_helper(-1);'
    'catch e'
    '    fprintf(''error.helper\t%s | %s | %s line %d\n'', e.identifier, e.message, e.stack(1).name, e.stack(1).line);'
    'end'
    'd = dir(fullfile(ctfroot, ''**'', ''*''));'
    'd = d(~[d.isdir]);'
    'for i = 1:numel(d)'
    '    f = fullfile(d(i).folder, d(i).name);'
    '    fid = fopen(f, ''r''); b = fread(fid, 8, ''*uint8'')''; fclose(fid);'
    '    p = char(b); p(b < 32 | b > 126) = ''.'';'
    '    fprintf(''file\t%s\t%d\t%s\n'', strrep(f, ctfroot, ''<CTF>''), d(i).bytes, p);'
    'end'
    'end'
    });
put(src, 'k3b_helper.m', {
    'function r = k3b_helper(x)'
    '%K3B_HELPER Help text of the helper.'
    'if x < 0'
    '    error(''k3b:neg'', ''negative input'');'
    'end'
    'r = x * 2;'
    'end'
    });
put(src, 'k3b_data.txt', {'data line'});
key = fullfile(work, 'k3b.key');
put(work, 'k3b.key', {repmat('0123456789abcdef', 1, 4)});
put(work, 'secrets.json', {'{', '  "Embedded": {', '    "secret": ["K3B_SECRET"]', '  }', '}'});
put(work, 'secrets_none.json', {'{ "Other": { "secret": ["K3B_SECRET"] } }'});
put(work, 'secrets_empty.json', {'{ "Embedded": { "secret": [] } }'});
put(work, 'secrets_nosecret.json', {'{ "Embedded": { "description": "x" } }'});
put(work, 'secrets_bad.json', {'{ "Embedded": '});
cd(src);

builds = {
    'plain',      {}
    's',          {'-s'}
    'j',          {'-j'}
    'sj',         {'-s', '-j'}
    'k',          {'-k'}
    'kfile',      {'-k', ['file=' key ';loader=' fullfile(work, 'k3b_loader.mexw64')]}
    'konly',      {'-k', key}
    'kmissing',   {'-k', ['file=' fullfile(work, 'nosuch.key') ';loader=x.mexw64']}
    'Jok',        {'-J', fullfile(work, 'secrets.json')}
    'Jnone',      {'-J', fullfile(work, 'secrets_none.json')}
    'Jempty',     {'-J', fullfile(work, 'secrets_empty.json')}
    'Jnosecret',  {'-J', fullfile(work, 'secrets_nosecret.json')}
    'Jbad',       {'-J', fullfile(work, 'secrets_bad.json')}
    'Jmissing',   {'-J', fullfile(work, 'nosuch.json')}
    };
for i = 1:size(builds, 1)
    name = builds{i, 1};
    d = fullfile(work, ['b_' name]);
    mkdir(d);
    args = [{'-m', 'k3b_main.m', '-C', '-d', d, '-a', fullfile(src, 'k3b_data.txt')}, builds{i, 2}];
    fprintf('BUILD\t%s\tmcc %s\n', name, strjoin(strrep(args, work, '<W>'), ' '));
    try
        log = evalc('mcc(args{:})');
        fprintf('  ok\n');
    catch e
        log = '';
        fprintf('  ERR %s | %s\n', e.identifier, k0_flat(strrep(e.message, work, '<W>')));
    end
    for l = splitlines(strrep(strtrim(log), work, '<W>'))'
        if strlength(l{1}) > 0, fprintf('  log\t%s\n', l{1}); end
    end
    files = dir(d);
    files = files(~[files.isdir]);
    fprintf('  outputs\t%s\n', strjoin(sort({files.name}), ' '));
    ctf = fullfile(d, 'k3b_main.ctf');
    if isfile(ctf)
        listArchive(ctf, fullfile(work, ['x_' name]), work);
    end
    keys = dir(fullfile(d, '*.key'));
    for k = 1:numel(keys)
        t = fileread(fullfile(d, keys(k).name));
        fprintf('  keyfile\t%s\t%d bytes\t%s\n', keys(k).name, numel(t), k0_flat(t));
    end
end

% The compiler.build form of ObfuscateArchive and the other properties' validation.
o = compiler.build.StandaloneApplicationOptions(fullfile(src, 'k3b_main.m'));
k0_try('opts.ObfuscateArchive.default', @() o.ObfuscateArchive);
k0_try('opts.set.Obfuscate.on', @() setp(o, 'ObfuscateArchive', 'on'));
k0_try('opts.set.Obfuscate.true', @() setp(o, 'ObfuscateArchive', true));
k0_try('opts.set.Obfuscate.word', @() setp(o, 'ObfuscateArchive', 'maybe'));
k0_try('opts.set.Secrets.missing', @() setp(o, 'SecretsManifest', fullfile(work, 'nosuch.json')));
k0_try('opts.set.Secrets.txt', @() setp(o, 'SecretsManifest', fullfile(src, 'k3b_data.txt')));
k0_try('opts.set.Secrets.ok', @() setp(o, 'SecretsManifest', fullfile(work, 'secrets.json')));
k0_try('opts.set.Secrets.number', @() setp(o, 'SecretsManifest', 5));
k0_try('opts.set.Key.struct', @() setp(o, 'ExternalEncryptionKey', struct('EncryptionKeyFile', key, 'RuntimeKeyLoaderFile', 'x.mexw64')));
k0_try('opts.set.Key.onefield', @() setp(o, 'ExternalEncryptionKey', struct('EncryptionKeyFile', key)));
k0_try('opts.set.Key.extra', @() setp(o, 'ExternalEncryptionKey', struct('EncryptionKeyFile', key, 'RuntimeKeyLoaderFile', '', 'X', 1)));
k0_try('opts.set.Key.text', @() setp(o, 'ExternalEncryptionKey', key));
k0_try('opts.set.Key.missingfile', @() setp(o, 'ExternalEncryptionKey', struct('EncryptionKeyFile', fullfile(work, 'nosuch.key'), 'RuntimeKeyLoaderFile', '')));
k0_try('opts.set.Key.empty', @() setp(o, 'ExternalEncryptionKey', struct([])));
o2 = compiler.build.StandaloneApplicationOptions(fullfile(src, 'k3b_main.m'), 'OutputDir', fullfile(work, 'cb_obf'), 'ObfuscateArchive', 'on', 'Verbose', 'on');
try
    log = evalc('r = compiler.build.standaloneApplication(o2);');
    for l = splitlines(strrep(strtrim(log), work, '<W>'))'
        if contains(l{1}, 'mcc') || contains(l{1}, '-s') || contains(l{1}, 'Compiler'), fprintf('  cblog\t%s\n', l{1}); end
    end
    fprintf('cb.obf\tok %s\n', strjoin(strrep(r.Files, work, '<W>'), ' '));
catch e
    fprintf('cb.obf\tERR %s | %s\n', e.identifier, k0_flat(strrep(e.message, work, '<W>')));
end
cd(here);
end

function v = setp(o, name, value)
o.(name) = value;
v = o.(name);
end

function listArchive(ctf, into, work)
mkdir(into);
names = unzip(ctf, into);
for i = 1:numel(names)
    f = names{i};
    if isfolder(f), continue; end
    fid = fopen(f, 'r');
    b = fread(fid, 12, '*uint8')';
    fclose(fid);
    p = char(b);
    p(b < 32 | b > 126) = '.';
    d = dir(f);
    rel = strrep(strrep(f, into, ''), '\', '/');
    fprintf('  entry\t%s\t%d\t%s\n', strrep(rel, strrep(work, '\', '/'), '<W>'), d.bytes, p);
end
m = fullfile(into, '.META', 'manifest.xml');
if isfile(m)
    for l = splitlines(fileread(m))'
        t = strtrim(l{1});
        if strlength(t) > 0, fprintf('  manifest\t%s\n', strrep(t, work, '<W>')); end
    end
end
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

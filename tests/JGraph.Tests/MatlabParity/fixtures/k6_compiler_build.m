% record: -noFigureWindows
% Compiler and SDK plan, stage K6: standalone programs - compiler.build.StandaloneApplicationOptions,
% its defaults, derived names and refusals; mcc's refusals from a session; and what a build writes
% and answers (the files, the readme's sections, compiler.build.Results, the verbose command). The
% programs are built and never run here.
beep('off');
root = tempname;
src = fullfile(root, 'src');
mkdir(src);
put(src, 'k6f.m', {'function k6f(varargin)', 'fprintf(''k6f %d\n'', nargin);', 'end'});
put(src, '9k.m', {'function r = x9k', 'r = 1;', 'end'});
put(src, 'k6-dash.m', {'function r = k6dash', 'r = 2;', 'end'});
put(src, 'k6_help.txt', {'k6 help'});
[x, y] = meshgrid(linspace(0, 1, 64));
imwrite(uint8(255 * cat(3, x, y, 1 - x)), fullfile(src, 'k6_icon.png'));
old = cd(src);

% The options: their defaults and the names derived from the app file.
o = compiler.build.StandaloneApplicationOptions('k6f.m');
u2_chk('class', @() class(o));
u2_chk('name', @() o.ExecutableName);
u2_chk('outdir', @() o.OutputDir);
u2_chk('version', @() o.ExecutableVersion);
u2_chk('icon', @() strrep(o.ExecutableIcon, matlabroot, '<M>'));
u2_chk('splash', @() strrep(o.ExecutableSplashScreen, matlabroot, '<M>'));
u2_chk('appfile', @() strrep(o.AppFile, root, '<root>'));
u2_chk('help', @() o.CustomHelpTextFile);
u2_chk('embed', @() char(o.EmbedArchive));
u2_chk('numeric', @() char(o.TreatInputsAsNumeric));
u2_chk('additional', @() size(o.AdditionalFiles));
u2_chk('detect', @() char(o.AutoDetectDataFiles));
u2_chk('key', @() size(o.ExternalEncryptionKey));
u2_chk('key_fields', @() strjoin(fieldnames(o.ExternalEncryptionKey)', ' '));
u2_chk('obfuscate', @() char(o.ObfuscateArchive));
u2_chk('secrets', @() o.SecretsManifest);
u2_chk('packages', @() strjoin(o.SupportPackages, ' '));
u2_chk('verbose', @() char(o.Verbose));
u2_chk('props', @() strjoin(properties(o)', ' '));
u2_chk('digit_name', @() exename('9k.m'));
u2_chk('digit_outdir', @() outdir('9k.m'));
u2_chk('dash_name', @() exename('k6-dash.m'));
o.ExecutableName = 'k6 two words';
u2_chk('spaced_outdir', @() o.OutputDir);
o.ExecutableVersion = '70000';
u2_chk('version_unchecked', @() o.ExecutableVersion);
o.OutputDir = '.\built';
u2_chk('outdir_dot', @() o.OutputDir);
o.OutputDir = 'built';
u2_chk('outdir_abs', @() strrep(o.OutputDir, root, '<root>'));
o.TreatInputsAsNumeric = true;
u2_chk('numeric_true', @() char(o.TreatInputsAsNumeric));
o.EmbedArchive = 0;
u2_chk('embed_zero', @() char(o.EmbedArchive));
o.SupportPackages = {};
u2_chk('packages_none', @() strjoin(o.SupportPackages, ' '));
o.AdditionalFiles = {'k6_help.txt', 'k6f.m'};
u2_chk('additional_two', @() strjoin(strrep(o.AdditionalFiles', root, '<root>'), ' | '));
u2_chk('additional_size', @() size(o.AdditionalFiles));
o.CustomHelpTextFile = "";
u2_chk('help_empty', @() class(o.CustomHelpTextFile));
nv = compiler.build.StandaloneApplicationOptions('k6f.m', 'ExecutableName', 'named', 'verb', 'on');
u2_chk('nv_name', @() nv.ExecutableName);
u2_chk('nv_partial', @() char(nv.Verbose));

% The refusals, set by set and from the constructor.
u2_chk('set_name_slash', @() assign('ExecutableName', 'a/b'));
u2_chk('set_name_empty', @() assign('ExecutableName', ''));
u2_chk('set_name_number', @() assign('ExecutableName', 5));
u2_chk('set_name_tab', @() assign('ExecutableName', sprintf('a\tb')));
u2_chk('set_name_bar', @() assign('ExecutableName', 'a|b'));
u2_chk('set_outdir_bar', @() assign('OutputDir', 'a|b'));
u2_chk('set_version_number', @() assign('ExecutableVersion', 2));
u2_chk('set_numeric_word', @() assign('TreatInputsAsNumeric', 'maybe'));
u2_chk('set_detect_word', @() assign('AutoDetectDataFiles', 'maybe'));
u2_chk('set_verbose_word', @() assign('Verbose', 'loud'));
u2_chk('set_additional_missing', @() assign('AdditionalFiles', 'k0_nosuch_data.mat'));
u2_chk('set_additional_number', @() assign('AdditionalFiles', 5));
u2_chk('set_key_fields', @() assign('ExternalEncryptionKey', struct('EncryptionKeyFile', 'x')));
u2_chk('set_icon_missing', @() assign('ExecutableIcon', 'k0_nosuch.png'));
u2_chk('set_splash_text', @() strrep(errtext(@() assign('ExecutableSplashScreen', 'k6_help.txt')), root, '<root>'));
u2_chk('set_help_missing', @() assign('CustomHelpTextFile', 'k0_nosuch.txt'));
u2_chk('set_secrets_missing', @() assign('SecretsManifest', 'k0_nosuch.json'));
u2_chk('ctor_missing', @() compiler.build.StandaloneApplicationOptions('k0_nosuch.m'));
u2_chk('ctor_text', @() compiler.build.StandaloneApplicationOptions('k6_help.txt'));
u2_chk('ctor_badname', @() compiler.build.StandaloneApplicationOptions('k6f.m', 'NoSuchOption', 1));
u2_chk('ctor_none', @() compiler.build.StandaloneApplicationOptions());
u2_chk('build_none', @() compiler.build.standaloneApplication());
u2_chk('build_opts_and_nv', @() compiler.build.standaloneApplication(o, 'Verbose', 'on'));
u2_chk('build_missing', @() compiler.build.standaloneApplication('k0_nosuch.m'));
u2_chk('build_two_out', @() twoout());

% mcc's refusals, which come before anything is built.
u2_chk('mcc_missing', @() mccrun({'-m', 'k0_nosuchfile.m'}));
u2_chk('mcc_nofile', @() mccrun({'-m'}));
u2_chk('mcc_badW', @() mccrun({'-W', 'bogus:x', 'k6f.m'}));
u2_chk('mcc_mismatch', @() mccrun({'-W', 'main:k6f', '-T', 'link:lib', 'k6f.m'}));
u2_chk('mcc_badT', @() mccrun({'-T', 'link:nothing', 'k6f.m'}));
u2_chk('mcc_unknown', @() mccrun({'-Q', 'k6f.m'}));
u2_chk('mcc_nowrapper', @() mccrun({'-t', 'k6f.m'}));
u2_chk('mcc_version', @() mccrun({'-W', 'main:k6f,version=1.2.3.4.5', '-T', 'link:exe', 'k6f.m'}));
u2_chk('mcc_version_big', @() mccrun({'-W', 'main:k6f,version=70000', '-T', 'link:exe', 'k6f.m'}));
u2_chk('mcc_out_spaced', @() mccrun({'-m', 'k6f.m', '-o', 'k6 spaced'}));
u2_chk('mcc_out_ext', @() mccrun({'-m', 'k6f.m', '-o', 'k6_ext.exe'}));
u2_chk('mcc_help_missing', @() strrep(errtext(@() mccrun({'-m', 'k6f.m', '-h', fullfile(src, 'k6_nosuch.txt')})), root, '<root>'));
u2_chk('mcc_icon_png', @() strrep(errtext(@() mccrun({'-m', 'k6f.m', '-r', fullfile(src, 'k6_icon.png')})), root, '<root>'));
u2_chk('mcc_icon_missing', @() strrep(errtext(@() mccrun({'-m', 'k6f.m', '-r', fullfile(src, 'k6_nosuch.ico')})), root, '<root>'));
u2_chk('mcc_x', @() mccrun({'-x', 'k6f.m'}));

% Builds: what each writes and answers.
d1 = fullfile(root, 'out1');
mccrun({'-m', 'k6f.m', '-d', d1});
u2_chk('m_files', @() names(d1));
u2_chk('m_readme_title', @() firstline(fullfile(d1, 'readme.txt')));
u2_chk('m_readme_sections', @() strjoin(regexp(fileread(fullfile(d1, 'readme.txt')), '(?m)^\d+\. [^\r\n]*', 'match'), ' | '));
u2_chk('m_readme_crlf', @() contains(fileread(fullfile(d1, 'readme.txt')), sprintf('\r\n')));
u2_chk('m_products', @() fileread(fullfile(d1, 'requiredMCRProducts.txt')));
d2 = fullfile(root, 'out2');
mccrun({'-m', 'k6f.m', '-C', '-o', 'k6sep', '-d', d2});
u2_chk('c_files', @() names(d2));
chkdiv('c_readme_list', @() listed(fullfile(d2, 'readme.txt')), 'ADR0262');
d3 = fullfile(root, 'out3');
mccrun({'-m', 'k6f.m', '-d', fullfile(d3, 'made', 'here')});
u2_chk('d_made', @() names(fullfile(d3, 'made', 'here')));

r = compiler.build.standaloneApplication('k6f.m', 'OutputDir', fullfile(root, 'api'), 'ExecutableName', 'k6api', ...
    'ExecutableIcon', fullfile(src, 'k6_icon.png'), 'ExecutableVersion', '3.4', 'CustomHelpTextFile', fullfile(src, 'k6_help.txt'));
u2_chk('r_type', @() r.BuildType);
u2_chk('r_files', @() strjoin(strrep(r.Files', root, '<root>'), ' | '));
u2_chk('r_files_size', @() size(r.Files));
u2_chk('r_options', @() class(r.Options));
u2_chk('r_packages', @() size(r.IncludedSupportPackages));
u2_chk('r_deps', @() class(r.RuntimeDependencies));
u2_chk('r_required_size', @() size(r.RuntimeDependencies.Required));
u2_chk('r_required_id', @() r.RuntimeDependencies.Required.ID);
chkdiv('r_required_name', @() r.RuntimeDependencies.Required.DisplayName{1}, 'ADR0257');
u2_chk('r_optional', @() size(r.RuntimeDependencies.Optional));
u2_chk('r_id', @() class(r.BuildID));
u2_chk('r_duration', @() class(r.BuildDuration));
u2_chk('r_props', @() strjoin(properties(r)', ' '));
u2_chk('r_display', @() display_lines(r));
u2_chk('api_files', @() names(fullfile(root, 'api')));

v = compiler.build.StandaloneApplicationOptions('k6f.m', 'OutputDir', fullfile(root, 'verbose'), 'Verbose', 'on', 'TreatInputsAsNumeric', 'on');
said = splitlines(strtrim(evalc('compiler.build.standaloneApplication(v);')));
said = regexprep(strrep(said, root, '<root>'), '\S*\\tp[^\\]*\\icon\.ico', '<ICON>');
u2_chk('verbose_command', @() said{1});
chkdiv('verbose_compiler', @() said{2}, 'ADR0262');
u2_chk('verbose_rest', @() strjoin(said(3:end)', ' / '));

w = compiler.build.standaloneWindowsApplication('k6f.m', 'OutputDir', fullfile(root, 'win'));
u2_chk('w_type', @() w.BuildType);
u2_chk('w_files', @() strjoin(strrep(w.Files', root, '<root>'), ' | '));
info = imfinfo(fullfile(root, 'win', 'splash.png'));
u2_chk('w_splash', @() [info.Width info.Height]);
cd(old);

function v = exename(file)
v = compiler.build.StandaloneApplicationOptions(file).ExecutableName;
end

function v = outdir(file)
v = compiler.build.StandaloneApplicationOptions(file).OutputDir;
end

function r = assign(name, value)
o = compiler.build.StandaloneApplicationOptions('k6f.m');
o.(name) = value;
r = 'ok';
end

function r = twoout()
[a, b] = compiler.build.standaloneApplication('k6f.m'); %#ok<ASGLU>
r = 'ok';
end

function r = mccrun(args)
evalc('mcc(args{:})');
r = 'ok';
end

function s = errtext(f)
try
    f();
    s = 'no error';
catch e
    s = [e.identifier ' / ' strrep(e.message, newline, ' / ')];
end
end

function s = names(folder)
d = dir(folder);
d = d(~[d.isdir]);
s = strjoin(sort({d.name}), ' ');
end

function s = firstline(file)
t = splitlines(fileread(file));
s = t{1};
end

function s = listed(file)
t = splitlines(strrep(fileread(file), sprintf('\r'), ''));
s = strjoin(t(startsWith(t, '-'))', ' | ');
end

function s = display_lines(v)
t = regexprep(evalc('disp(v)'), '<a [^>]*>|</a>', '');
t = strtrim(splitlines(t));
s = strjoin(t(strlength(t) > 0)', ' / ');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function chkdiv(name, fn, adr)
% A u2_chk line whose rule is a recorded divergence: the stamp adds what JGraph prints.
fprintf('CHK|%s|%s|div=%s\n', name, u2_text(fn()), adr);
end

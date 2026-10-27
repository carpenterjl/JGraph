function probe_k21_legacy(work)
%PROBE_K21_LEGACY Asks R2025b what it does with the legacy deployment projects of R2024b and before
%(compiler and SDK plan, stage K21): the <deployment-project plugin="plugin.ezdeploy"> files the
%Application Compiler, Library Compiler, Production Server Compiler and Web App Compiler wrote. The
%probe writes a sample of every sub-target (and the broken ones) in a folder of its own, then
%records what compiler.internal.readPRJStruct reads, what createDeploymentScript writes for each
%and refuses, what deploytool -build and -package print and leave behind, what the upgrade into a
%MATLAB project (matlab.internal.deployment.createProjectModelFromPRJ, the road deploytool's
%one-argument form and open take) makes of each, and the catalog texts. Run under matlab -batch
%-noFigureWindows. One key<TAB>value line per question, what a call printed as indented lines
%under <key>.printed, a script's text under <key>.text.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
old = cd(work);
cleanup = onCleanup(@() cd(old)); %#ok<NASGU>

%% The sources every sample gets a copy of.
writeText(fullfile(src, 'k21_hello.m'), sprintf(['function k21_hello(name)\n%%K21_HELLO Greets.\n%%   k21_hello(name) prints a greeting.\n' ...
    'if nargin < 1, name = ''world''; end\nfprintf(''hello %%s\\n'', name);\nend\n']));
writeText(fullfile(src, 'k21_add.m'), sprintf('function c = k21_add(a, b)\n%%K21_ADD Adds.\nc = a + b;\nend\n'));
writeText(fullfile(src, 'k21_mul.m'), sprintf('function c = k21_mul(a, b)\n%%K21_MUL Multiplies.\nc = a * b;\nend\n'));
writeText(fullfile(src, 'k21_sample.m'), sprintf('%% A sample script.\nc = k21_add(1, 2);\ndisp(c)\n'));
writeText(fullfile(src, 'k21_note.txt'), sprintf('a note\n'));
writeText(fullfile(src, 'k21_notes.txt'), sprintf('install notes line 1\nline 2\n'));
writeText(fullfile(src, 'k21_b.txt'), sprintf('b\n'));
writeText(fullfile(src, 'k21_a.txt'), sprintf('a\n'));
x = 1:3; save(fullfile(src, 'k21_data.mat'), 'x'); %#ok<NASGU>
imwrite(uint8(zeros(32, 32, 3)), fullfile(src, 'k21_icon.png'));
imwrite(uint8(zeros(400, 400, 3)), fullfile(src, 'k21_splash.png'));
imwrite(uint8(zeros(112, 290, 3)), fullfile(src, 'k21_logo.png'));
writeIco(fullfile(src, 'k21_icon.ico'));
copyfile(fullfile(here, '..', 'ui-probes', 'u7b', 'rt', 'U7bApp.mlapp'), fullfile(src, 'k21_app.mlapp'));

P = containers.Map('KeyType', 'char', 'ValueType', 'any');
    function file = sample(name, target, targetName, params, filesets, varargin)
        folder = fullfile(work, 'p', name);
        mkdir(folder);
        copyfile(fullfile(src, '*'), folder);
        file = fullfile(folder, [name '.prj']);
        writePrj(file, name, target, targetName, params, filesets, varargin{:});
        P(name) = file;
    end

plainSets = struct('fileset_main', {{'${PROJECT_ROOT}\k21_hello.m'}}, 'fileset_resources', {{}}, 'fileset_package', {{}}, 'fileset_depfun', {{}});
std = standaloneParams('k21std');
std('param.authnamewatermark') = 'Ada Lovelace';
std('param.email') = 'ada@example.com';
std('param.company') = 'Analytical Engines';
std('param.summary') = 'A greeting';
std('param.description') = sprintf('Prints a greeting.\nTwo lines.');
std('param.version') = '2.3.1';
std('param.icon') = '${PROJECT_ROOT}\k21_icon.ico';
std('param.screenshot') = '${PROJECT_ROOT}\k21_splash.png';
std('param.logo') = '${PROJECT_ROOT}\k21_logo.png';
std('param.install.notes') = sprintf('Notes line 1\nNotes line 2');
std('param.installpath.string') = '\Analytical Engines\k21std\';
std('param.windows.command.prompt') = 'false';
std('param.create.log') = 'true';
std('param.log.file') = 'k21std.log';
std('param.native.matlab') = 'true';
std('param.checkbox') = 'true';
std('param.example') = 'k21std world';
std('param.help.text') = sprintf('Usage: k21std name\nPrints hello.');
std('param.user.defined.mcr.options') = '-C -v';
sample('k21std', 'target.ezdeploy.standalone', 'Application Compiler', std, struct( ...
    'fileset_main', {{'${PROJECT_ROOT}\k21_hello.m'}}, 'fileset_resources', {{'${PROJECT_ROOT}\k21_note.txt', '${PROJECT_ROOT}\k21_b.txt', '${PROJECT_ROOT}\k21_a.txt', '${PROJECT_ROOT}\k21_data.mat'}}, ...
    'fileset_package', {{'${PROJECT_ROOT}\k21_notes.txt', '${PROJECT_ROOT}\k21_a.txt'}}, 'fileset_depfun', {{'${PROJECT_ROOT}\k21_add.m'}}), '', 'plugin.ezdeploy', {'${PROJECT_ROOT}\k21_icon.png', '${PROJECT_ROOT}\k21_splash.png'});
sample('k21plain', 'target.ezdeploy.standalone', 'Application Compiler', standaloneParams('k21plain'), plainSets);
pngicon = standaloneParams('k21pngicon'); pngicon('param.icon') = '${PROJECT_ROOT}\k21_icon.png'; pngicon('param.icons') = '${PROJECT_ROOT}\k21_icon.png';
sample('k21pngicon', 'target.ezdeploy.standalone', 'Application Compiler', pngicon, plainSets);
icons = standaloneParams('k21icons'); icons('param.icon') = '${PROJECT_ROOT}\k21_icon.ico';
sample('k21icons', 'target.ezdeploy.standalone', 'Application Compiler', icons, plainSets, '', 'plugin.ezdeploy', {'${PROJECT_ROOT}\k21_icon.png', '${PROJECT_ROOT}\k21_splash.png'});
mcr = standaloneParams('k21mcr'); mcr('param.web.mcr') = 'false'; mcr('param.package.mcr') = 'true';
sample('k21mcr', 'target.ezdeploy.standalone', 'Application Compiler', mcr, plainSets);
nomcr = standaloneParams('k21nomcr'); nomcr('param.web.mcr') = 'false'; nomcr('param.no.mcr') = 'true';
sample('k21nomcr', 'target.ezdeploy.standalone', 'Application Compiler', nomcr, plainSets);
all3 = standaloneParams('k21all3'); all3('param.package.mcr') = 'true'; all3('param.no.mcr') = 'true';
sample('k21all3', 'target.ezdeploy.standalone', 'Application Compiler', all3, plainSets);
none3 = standaloneParams('k21none3'); none3('param.web.mcr') = 'false';
sample('k21none3', 'target.ezdeploy.standalone', 'Application Compiler', none3, plainSets);
appdata = standaloneParams('k21appdata'); appdata('param.installpath.combo') = 'option.installpath.appdata'; appdata('param.installpath.string') = 'Sub\Folder';
sample('k21appdata', 'target.ezdeploy.standalone', 'Application Compiler', appdata, plainSets);
sample('k21noname', 'target.ezdeploy.standalone', 'Application Compiler', standaloneParams(''), plainSets);
sample('k21nomain', 'target.ezdeploy.standalone', 'Application Compiler', standaloneParams('k21nomain'), struct('fileset_main', {{}}, 'fileset_resources', {{}}, 'fileset_package', {{}}, 'fileset_depfun', {{}}));
sample('k21appmain', 'target.ezdeploy.standalone', 'Application Compiler', standaloneParams('k21appmain'), struct('fileset_main', {{'${PROJECT_ROOT}\k21_app.mlapp'}}, 'fileset_resources', {{}}, 'fileset_package', {{}}, 'fileset_depfun', {{}}));
sample('k21badtarget', 'target.ezdeploy.nothing', 'Nothing Compiler', standaloneParams('k21badtarget'), plainSets);
badsub = standaloneParams('k21badsub'); badsub('param.target.type') = 'subtarget.nothing';
sample('k21badsub', 'target.ezdeploy.standalone', 'Application Compiler', badsub, plainSets);
nosub = standaloneParams('k21nosub'); nosub('param.target.type') = '';
sample('k21nosub', 'target.ezdeploy.standalone', 'Application Compiler', nosub, plainSets);
sample('k21abs', 'target.ezdeploy.standalone', 'Application Compiler', standaloneParams('k21abs'), struct('fileset_main', {{fullfile(work, 'p', 'k21abs', 'k21_hello.m')}}, ...
    'fileset_resources', {{'${MATLAB_ROOT}\toolbox\compiler\Contents.m', 'C:/forward/slash.txt'}}, 'fileset_package', {{}}, 'fileset_depfun', {{}}));
sample('k21moved', 'target.ezdeploy.standalone', 'Application Compiler', standaloneParams('k21moved'), plainSets, 'C:\somewhere\else');
sample('k21missingmain', 'target.ezdeploy.standalone', 'Application Compiler', standaloneParams('k21missingmain'), struct('fileset_main', {{'${PROJECT_ROOT}\k21_nothere.m'}}, ...
    'fileset_resources', {{'${PROJECT_ROOT}\k21_gone.txt'}}, 'fileset_package', {{}}, 'fileset_depfun', {{}}));
sample('k21mainsub', 'target.ezdeploy.standalone', 'Application Compiler', standaloneParams('k21mainsub'), struct('fileset_main', {{'${PROJECT_ROOT}\sub\k21_hello.m'}}, ...
    'fileset_resources', {{}}, 'fileset_package', {{}}, 'fileset_depfun', {{}}));
mkdir(fullfile(work, 'p', 'k21mainsub', 'sub')); copyfile(fullfile(src, 'k21_hello.m'), fullfile(work, 'p', 'k21mainsub', 'sub'));
helpoff = standaloneParams('k21helpoff'); helpoff('param.help.text') = 'Some help'; helpoff('param.example') = 'k21helpoff 1';
sample('k21helpoff', 'target.ezdeploy.standalone', 'Application Compiler', helpoff, plainSets);
helpempty = standaloneParams('k21helpempty'); helpempty('param.checkbox') = 'true';
sample('k21helpempty', 'target.ezdeploy.standalone', 'Application Compiler', helpempty, plainSets);
consoleC = standaloneParams('k21console'); consoleC('param.windows.command.prompt') = 'false'; consoleC('param.user.defined.mcr.options') = '-C';
sample('k21console', 'target.ezdeploy.standalone', 'Application Compiler', consoleC, plainSets);

libSets = struct('fileset_exports', {{'${PROJECT_ROOT}\k21_add.m', '${PROJECT_ROOT}\k21_mul.m'}}, ...
    'fileset_classes', {{'Class1', {'${PROJECT_ROOT}\k21_add.m', '${PROJECT_ROOT}\k21_mul.m'}}}, ...
    'fileset_resources', {{'${PROJECT_ROOT}\k21_note.txt'}}, 'fileset_depfun', {{}}, 'fileset_package', {{'${PROJECT_ROOT}\k21_notes.txt'}}, ...
    'fileset_examples', {{'${PROJECT_ROOT}\k21_sample.m'}}, 'fileset_documentation', {{'${PROJECT_ROOT}\k21_b.txt'}});
subs = {'c', 'subtarget.library.c'; 'cpp', 'subtarget.library.cpp'; 'net', 'subtarget.net.component'; 'java', 'subtarget.java.package'; ...
    'python', 'subtarget.python.module'; 'com', 'subtarget.com.component'; 'excel', 'subtarget.ex.addin'};
for k = 1:size(subs, 1)
    name = ['k21' subs{k, 1}];
    lib = libraryParams(name, subs{k, 2});
    lib('param.authnamewatermark') = 'Ada Lovelace';
    lib('param.email') = 'ada@example.com';
    lib('param.company') = 'Analytical Engines';
    lib('param.summary') = 'A library';
    lib('param.description') = 'Adds and multiplies.';
    lib('param.version') = '1.2';
    lib('param.install.notes') = 'Library notes';
    lib('param.target.install.notes') = 'Target notes';
    lib('param.logo') = '${PROJECT_ROOT}\k21_logo.png';
    lib('param.screenshot') = '${PROJECT_ROOT}\k21_splash.png';
    lib('param.icon') = '${PROJECT_ROOT}\k21_icon.ico';
    if strcmp(subs{k, 1}, 'net'), lib('param.namespace') = 'K21Net'; end
    if strcmp(subs{k, 1}, 'java'), lib('param.namespace') = 'k21java'; end
    if strcmp(subs{k, 1}, 'python'), lib('param.namespace') = 'k21py'; end
    sample(name, 'target.ezdeploy.library', 'Library Compiler', lib, libSets);
end
one = struct('fileset_exports', {{'${PROJECT_ROOT}\k21_add.m'}}, 'fileset_classes', {{'Class1', {'${PROJECT_ROOT}\k21_add.m'}}}, ...
    'fileset_resources', {{}}, 'fileset_depfun', {{}}, 'fileset_package', {{}}, 'fileset_examples', {{}}, 'fileset_documentation', {{}});
cppl = libraryParams('k21cpplegacy', 'subtarget.library.cpp'); cppl('param.cpp.api') = 'option.cpp.legacy';
sample('k21cpplegacy', 'target.ezdeploy.library', 'Library Compiler', cppl, one);
cppg = libraryParams('k21cppgeneric', 'subtarget.library.cpp'); cppg('param.cpp.api') = 'option.cpp.generic';
sample('k21cppgeneric', 'target.ezdeploy.library', 'Library Compiler', cppg, one);
tsa = libraryParams('k21tsa', 'subtarget.net.component'); tsa('param.net.tsa.enable') = 'true'; tsa('param.net.tsa.assembly') = '${PROJECT_ROOT}\Contract.dll'; tsa('param.net.tsa.superclass') = 'Class1'; tsa('param.net.saved.interface') = 'Contract.ICompute';
sample('k21tsa', 'target.ezdeploy.library', 'Library Compiler', tsa, one);
net5 = libraryParams('k21net5', 'subtarget.net.component'); net5('param.assembly.net.version') = 'option.net.version.five'; net5('param.net.enable.remoting') = 'true'; net5('param.assembly.type') = 'true'; net5('param.encryption.key.file') = '${PROJECT_ROOT}\key.snk';
sample('k21net5', 'target.ezdeploy.library', 'Library Compiler', net5, one);
netpkg = libraryParams('k21netpkg', 'subtarget.net.component'); netpkg('param.namespace') = 'Outer.Inner';
netpkgSets = one; netpkgSets.fileset_classes = {'First', {'${PROJECT_ROOT}\k21_add.m'}, 'Second', {'${PROJECT_ROOT}\k21_mul.m'}}; netpkgSets.fileset_exports = {'${PROJECT_ROOT}\k21_add.m', '${PROJECT_ROOT}\k21_mul.m'};
sample('k21netpkg', 'target.ezdeploy.library', 'Library Compiler', netpkg, netpkgSets);
sample('k21noexports', 'target.ezdeploy.library', 'Library Compiler', libraryParams('k21noexports', 'subtarget.library.c'), ...
    struct('fileset_exports', {{}}, 'fileset_classes', {{}}, 'fileset_resources', {{}}, 'fileset_depfun', {{}}, 'fileset_package', {{}}, 'fileset_examples', {{}}, 'fileset_documentation', {{}}));
noclassSets = struct('fileset_exports', {{'${PROJECT_ROOT}\k21_add.m'}}, 'fileset_classes', {{}}, 'fileset_resources', {{}}, 'fileset_depfun', {{}}, 'fileset_package', {{}}, 'fileset_examples', {{}}, 'fileset_documentation', {{}});
sample('k21noclass', 'target.ezdeploy.library', 'Library Compiler', libraryParams('k21noclass', 'subtarget.net.component'), noclassSets);
sample('k21excelnoclass', 'target.ezdeploy.library', 'Library Compiler', libraryParams('k21excelnoclass', 'subtarget.ex.addin'), noclassSets);
sample('k21pynoclass', 'target.ezdeploy.library', 'Library Compiler', libraryParams('k21pynoclass', 'subtarget.python.module'), noclassSets);
sample('k21twoclass', 'target.ezdeploy.library', 'Library Compiler', libraryParams('k21twoclass', 'subtarget.python.module'), ...
    struct('fileset_exports', {{'${PROJECT_ROOT}\k21_add.m', '${PROJECT_ROOT}\k21_mul.m'}}, 'fileset_classes', {{'Adder', {'${PROJECT_ROOT}\k21_add.m'}, 'Multiplier', {'${PROJECT_ROOT}\k21_mul.m'}}}, ...
    'fileset_resources', {{}}, 'fileset_depfun', {{}}, 'fileset_package', {{}}, 'fileset_examples', {{}}, 'fileset_documentation', {{}}));
cC = libraryParams('k21cC', 'subtarget.library.c'); cC('param.user.defined.mcr.options') = '-C';
sample('k21cC', 'target.ezdeploy.library', 'Library Compiler', cC, one);
cdepSets = one; cdepSets.fileset_depfun = {'${PROJECT_ROOT}\k21_mul.m'}; cdepSets.fileset_package = {'${PROJECT_ROOT}\k21_notes.txt'};
sample('k21cdep', 'target.ezdeploy.library', 'Library Compiler', libraryParams('k21cdep', 'subtarget.library.c'), cdepSets);
libsub = libraryParams('k21libbadsub', 'subtarget.nothing');
sample('k21libbadsub', 'target.ezdeploy.library', 'Library Compiler', libsub, one);

mpsSets = struct('fileset_exports', {{'${PROJECT_ROOT}\k21_add.m', '${PROJECT_ROOT}\k21_mul.m'}}, 'fileset_classes', {{}}, 'fileset_resources', {{'${PROJECT_ROOT}\k21_note.txt'}}, ...
    'fileset_depfun', {{}}, 'fileset_package', {{}}, 'fileset_package_server', {{'${PROJECT_ROOT}\k21_a.txt'}}, 'fileset_package_client', {{'${PROJECT_ROOT}\k21_b.txt'}});
sample('k21mps', 'target.ezdeploy.mps.archive', 'Production Server Compiler', mpsParams('k21mps', 'subtarget.mads'), mpsSets);
mpsxSets = mpsSets; mpsxSets.fileset_classes = {'Class1', {'${PROJECT_ROOT}\k21_add.m'}};
sample('k21mpsexcel', 'target.ezdeploy.mps.archive', 'Production Server Compiler', mpsParams('k21mpsexcel', 'subtarget.mps.excel'), mpsxSets);
sample('k21web', 'target.webdeploy', 'Web App Compiler', webParams('k21web'), struct('fileset_web_main', {{'${PROJECT_ROOT}\k21_app.mlapp'}}, 'fileset_resources', {{'${PROJECT_ROOT}\k21_note.txt'}}, 'fileset_depfun', {{}}), '', 'plugin.webdeploy');
webout = webParams('k21webout'); webout('param.output') = '${PROJECT_ROOT}\k21webout\out'; webout('param.checkbox') = 'true';
sample('k21webout', 'target.webdeploy', 'Web App Compiler', webout, struct('fileset_web_main', {{'${PROJECT_ROOT}\k21_app.mlapp'}}, 'fileset_resources', {{}}, 'fileset_depfun', {{}}), '', 'plugin.webdeploy');
hadoop = standaloneParams('k21hadoop'); hadoop('param.target.type') = 'subtarget.hadoop';
sample('k21hadoop', 'target.ezdeploy.map.reduce', 'Hadoop Compiler', hadoop, struct('fileset_mapper', {{'${PROJECT_ROOT}\k21_add.m'}}, 'fileset_reducer', {{'${PROJECT_ROOT}\k21_mul.m'}}, 'fileset_datastore', {{}}, 'fileset_resources', {{}}, 'fileset_depfun', {{}}));
% Files that are not legacy projects.
writeText(fullfile(work, 'p', 'k21text.prj'), sprintf('not a project\n'));
writeText(fullfile(work, 'p', 'k21stub.prj'), sprintf(['<?xml version="1.0" encoding="UTF-8"?>\n' ...
    '<deployment-project plugin="plugin.ezdeploy" plugin-version="1.0">\n' ...
    '  <configuration build-checksum="1" file="k21stub.prj" location="." name="k21stub" target="target.ezdeploy.standalone" target-name="Application Compiler">\n' ...
    '    <param.appname>k21stub</param.appname>\n' ...
    '    <fileset.main><file>${PROJECT_ROOT}\\k21_hello.m</file></fileset.main>\n' ...
    '  </configuration>\n' ...
    '</deployment-project>\n']));
writeText(fullfile(work, 'p', 'k21nocfg.prj'), sprintf('<?xml version="1.0" encoding="UTF-8"?>\n<deployment-project plugin="plugin.ezdeploy" plugin-version="1.0">\n  <nothing />\n</deployment-project>\n'));

%% The names.
names = {'createDeploymentScript', 'compiler.internal.readPRJStruct', 'compiler.internal.build.legacyProject', 'compiler.internal.package.legacyProject', ...
    'matlab.internal.deployment.createProjectModelFromPRJ', 'matlab.internal.deployment.readPRJStruct', 'compiler.internal.ui.getStandaloneDeploymentScript', ...
    'compiler.internal.ui.getCSharedLibraryDeploymentScript', 'compiler.internal.ui.getPSADeploymentScript'};
for k = 1:numel(names)
    n = names{k};
    k0_try(['exist.' n], @() exist(n)); %#ok<EXIST>
    k0_try(['nargin.' n], @() nargin(n));
    k0_try(['nargout.' n], @() nargout(n));
end

%% readPRJStruct.
for n = {'k21std', 'k21plain', 'k21abs', 'k21moved', 'k21c', 'k21net', 'k21twoclass', 'k21mps', 'k21web', 'k21icons', 'k21nomain', 'k21noclass', 'k21hadoop', 'k21badsub'}
    k0_try(['prj.' n{1}], @() describeStruct(compiler.internal.readPRJStruct(P(n{1})), ''));
end
k0_try('prj.stub', @() describeStruct(compiler.internal.readPRJStruct(fullfile(work, 'p', 'k21stub.prj')), ''));
k0_try('prj.text', @() describeStruct(compiler.internal.readPRJStruct(fullfile(work, 'p', 'k21text.prj')), ''));
k0_try('prj.nocfg', @() describeStruct(compiler.internal.readPRJStruct(fullfile(work, 'p', 'k21nocfg.prj')), ''));
k0_try('prj.missing', @() describeStruct(compiler.internal.readPRJStruct(fullfile(work, 'p', 'k21none.prj')), ''));
k0_try('prj.noext', @() describeStruct(compiler.internal.readPRJStruct(fullfile(work, 'p', 'k21plain', 'k21plain')), ''));
k0_try('prj.std.deployment', @() describeStruct(matlab.internal.deployment.readPRJStruct(P('k21std')), ''));
cd(fullfile(work, 'p', 'k21plain'));
k0_try('prj.cwd.relative', @() describeStruct(compiler.internal.readPRJStruct('k21plain.prj'), ''));
k0_try('prj.cwd.noext', @() describeStruct(compiler.internal.readPRJStruct('k21plain'), ''));
cd(work);
k0_try('prj.number', @() describeStruct(compiler.internal.readPRJStruct(3), ''));

%% createDeploymentScript for each, and the output file's rules.
scripts = fullfile(work, 'scripts');
mkdir(scripts);
names = sort(keys(P));
for k = 1:numel(names)
    out = fullfile(scripts, ['deploy_' names{k} '.m']);
    key = ['script.' names{k}];
    call(key, @() createDeploymentScript(P(names{k}), out));
    if isfile(out)
        fprintf('%s.text\n', key);
        printLines(fileread(out));
    end
end
call('script.stub', @() createDeploymentScript(fullfile(work, 'p', 'k21stub.prj'), fullfile(scripts, 'deploy_stub.m')));
call('script.text', @() createDeploymentScript(fullfile(work, 'p', 'k21text.prj'), fullfile(scripts, 'deploy_text.m')));
call('script.nocfg', @() createDeploymentScript(fullfile(work, 'p', 'k21nocfg.prj'), fullfile(scripts, 'deploy_nocfg.m')));
call('script.missing', @() createDeploymentScript(fullfile(work, 'p', 'k21none.prj'), fullfile(scripts, 'deploy_missing.m')));
call('script.mfile', @() createDeploymentScript(fullfile(src, 'k21_hello.m'), fullfile(scripts, 'deploy_mfile.m')));
call('script.exists', @() createDeploymentScript(P('k21plain'), fullfile(scripts, 'deploy_k21plain.m')));
call('script.noext', @() createDeploymentScript(P('k21plain'), fullfile(scripts, 'deploy_noext')));
k0_try('script.noext.file', @() isfile(fullfile(scripts, 'deploy_noext.m')));
call('script.txt', @() createDeploymentScript(P('k21plain'), fullfile(scripts, 'deploy_plain2.txt')));
call('script.badname', @() createDeploymentScript(P('k21plain'), fullfile(scripts, '1bad.m')));
call('script.dashname', @() createDeploymentScript(P('k21plain'), fullfile(scripts, 'my-script.m')));
call('script.badname.txt', @() createDeploymentScript(P('k21plain'), fullfile(scripts, '1bad.txt')));
call('script.missing.txt', @() createDeploymentScript(fullfile(work, 'p', 'k21none.prj'), fullfile(scripts, 'deploy_m.txt')));
call('script.text.txt', @() createDeploymentScript(fullfile(work, 'p', 'k21text.prj'), fullfile(scripts, 'deploy_t.txt')));
call('script.nomain.txt', @() createDeploymentScript(P('k21nomain'), fullfile(scripts, 'deploy_nm.txt')));
call('script.nomain.exists', @() createDeploymentScript(P('k21nomain'), fullfile(scripts, 'deploy_k21plain.m')));
call('script.subfolder', @() createDeploymentScript(P('k21plain'), fullfile(scripts, 'sub', 'deploy_sub.m')));
call('script.badfolder', @() createDeploymentScript(P('k21plain'), 'Q:\nowhere\deploy_x.m'));
call('script.relative', @() createDeploymentScript(P('k21plain'), 'deploy_rel.m'));
k0_try('script.relative.file', @() isfile(fullfile(work, 'deploy_rel.m')));
call('script.string', @() createDeploymentScript(string(P('k21plain')), string(fullfile(scripts, 'deploy_string.m'))));
call('script.cell', @() createDeploymentScript({P('k21plain')}, fullfile(scripts, 'deploy_cell.m')));
call('script.number', @() createDeploymentScript(1, fullfile(scripts, 'deploy_number.m')));
call('script.outnumber', @() createDeploymentScript(P('k21plain'), 2));
call('script.outcell', @() createDeploymentScript(P('k21plain'), {fullfile(scripts, 'deploy_outcell.m')}));
call('script.outempty', @() createDeploymentScript(P('k21plain'), ''));
call('script.upper', @() createDeploymentScript(P('k21plain'), fullfile(scripts, 'deploy_upper.M')));
call('script.one', @() createDeploymentScript(P('k21plain')));
call('script.three', @() createDeploymentScript(P('k21plain'), fullfile(scripts, 'deploy_three.m'), 'x'));
cd(fullfile(work, 'p', 'k21plain'));
call('script.cwd.relative', @() createDeploymentScript('k21plain.prj', fullfile(scripts, 'deploy_cwd.m')));
k0_try('script.cwd.relative.file', @() isfile(fullfile(scripts, 'deploy_cwd.m')));
cd(work);
call('script.notcwd.relative', @() createDeploymentScript('k21plain.prj', fullfile(scripts, 'deploy_notcwd.m')));
addpath(fullfile(work, 'p', 'k21plain'));
call('script.onpath.relative', @() createDeploymentScript('k21plain.prj', fullfile(scripts, 'deploy_onpath.m')));
rmpath(fullfile(work, 'p', 'k21plain'));

%% deploytool -build and -package: what they print and leave behind.
for n = {'k21plain', 'k21std', 'k21console', 'k21c', 'k21cC', 'k21cppgeneric', 'k21python', 'k21net', 'k21tsa', 'k21mps', 'k21excel', 'k21web'}
    call(['build.' n{1}], @() deploytool('-build', P(n{1})));
    k0_try(['build.' n{1} '.files'], @() relFiles(fullfile(work, 'p', n{1}, n{1})));
end
call('build.nomain', @() deploytool('-build', P('k21nomain')));
call('build.noexports', @() deploytool('-build', P('k21noexports')));
call('build.noclass', @() deploytool('-build', P('k21noclass')));
call('build.hadoop', @() deploytool('-build', P('k21hadoop')));
call('build.badsub', @() deploytool('-build', P('k21badsub')));
call('build.missing', @() deploytool('-build', fullfile(work, 'p', 'k21none.prj')));
call('build.stub', @() deploytool('-build', fullfile(work, 'p', 'k21stub.prj')));
call('build.applicationCompiler', @() applicationCompiler('-build', P('k21plain')));
call('build.libraryCompiler', @() libraryCompiler('/build', P('k21cC')));
call('build.productionServerCompiler', @() productionServerCompiler('-build', P('k21mps')));
k0_try('build.legacy.result', @() describeObject(compiler.internal.build.legacyProject(P('k21plain'))));
for n = {'k21plain', 'k21cdep', 'k21python', 'k21mps', 'k21web', 'k21nomcr', 'k21mcr'}
    call(['package.' n{1}], @() deploytool('-package', P(n{1})));
    k0_try(['package.' n{1} '.files'], @() relFiles(fullfile(work, 'p', n{1}, n{1})));
end
call('package.c', @() deploytool('-package', P('k21c')));
call('package.nomain', @() deploytool('-package', P('k21nomain')));
call('package.missing', @() deploytool('-package', fullfile(work, 'p', 'k21none.prj')));
call('package.libraryCompiler', @() libraryCompiler('/package', P('k21cdep')));
k0_try('package.legacy.nargout', @() nargout('compiler.internal.package.legacyProject'));

%% The upgrade into a MATLAB project: what createProjectModelFromPRJ makes of each.
ups = {'k21std', 'k21plain', 'k21icons', 'k21console', 'k21c', 'k21cC', 'k21cpp', 'k21cpplegacy', 'k21net', 'k21tsa', 'k21net5', 'k21netpkg', 'k21python', 'k21twoclass', ...
    'k21excel', 'k21mps', 'k21mpsexcel', 'k21web', 'k21java', 'k21com', 'k21nomain', 'k21noexports', 'k21noclass', 'k21hadoop', 'k21badsub', 'k21libbadsub', 'k21moved', 'k21missingmain', 'k21mainsub', 'k21appmain'};
wrappers = {'StandaloneApp', 'getStandaloneDeploymentScript', true; 'CLibrary', 'getCSharedLibraryDeploymentScript', true; 'CppLibrary', 'getCppSharedLibraryDeploymentScript', true; ...
    'DotNETLibrary', 'getDotNETDeploymentScript', true; 'PythonLibrary', 'getPythonDeploymentScript', true; 'ExcelAddIn', 'getExcelDeploymentScript', true; ...
    'PSA', 'getPSADeploymentScript', false; 'WebApp', 'getWebAppDeploymentScript', false; 'JavaLibrary', 'getJavaDeploymentScript', true; 'COMLibrary', 'getCOMLibraryDeploymentScript', true};
for k = 1:numel(ups)
    name = ups{k};
    key = ['upgrade.' name];
    folder = fullfile(work, 'p', name);
    before = relFiles(folder);
    config = [];
    try
        [config, fw, ufw] = matlab.internal.deployment.createProjectModelFromPRJ(P(name));
        fprintf('%s\tclass %s\n', key, class(config));
        k0_try([key '.fileWarnings'], @() fw);
        k0_try([key '.unsupported'], @() ufw);
        if ~isempty(config)
            k0_try([key '.config'], @() describeObject(config));
        end
    catch ME
        fprintf('%s\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
    k0_warned(key);
    after = relFiles(folder);
    added = setdiff(after, before);
    added = added(~contains(added, 'resources'));
    fprintf('%s.added\t%s\n', key, strjoin(strrep(added, filesep, '/'), ', '));
    fprintf('%s.defs\t%d\n', key, sum(contains(relFiles(folder), ['resources' filesep 'project'])));
    try
        p = currentProject;
        fprintf('%s.project\tName=%s Root=%s\n', key, p.Name, rel(p.RootFolder, work));
        fprintf('%s.project.files\t%s\n', key, strjoin(sort(arrayfun(@(f) strrep(rel(f.Path, folder), filesep, '/'), p.Files, 'UniformOutput', false)), ', '));
        cats = p.Categories;
        for c = 1:numel(cats)
            if strcmp(cats(c).Name, 'Classification'), continue; end
            fprintf('%s.project.category\t%s [%s]\n', key, cats(c).Name, strjoin(arrayfun(@(l) char(l.Name), cats(c).LabelDefinitions, 'UniformOutput', false), ','));
        end
        for f = 1:numel(p.Files)
            labels = p.Files(f).Labels;
            if isempty(labels), continue; end
            fprintf('%s.project.labels.%s\t%s\n', key, strrep(rel(p.Files(f).Path, folder), filesep, '/'), strjoin(sort(arrayfun(@(l) sprintf('%s:%s', char(l.CategoryName), char(l.Name)), labels, 'UniformOutput', false)), ','));
        end
        if ~isempty(config)
            cls = class(config);
            for w = 1:size(wrappers, 1)
                if contains(cls, ['.' wrappers{w, 1} 'ConfigurationOptions'])
                    fn = str2func(['compiler.internal.ui.' wrappers{w, 2}]);
                    if wrappers{w, 3}
                        k0_try([key '.task.script'], @() 1);
                        try, printLines(fn(p.RootFolder, config.ProfileDisplayName, true)); catch ME, fprintf('  |ERR %s | %s\n', ME.identifier, k0_flat(ME.message)); end
                    else
                        k0_try([key '.task.script'], @() 1);
                        try, printLines(fn(p.RootFolder, config.ProfileDisplayName)); catch ME, fprintf('  |ERR %s | %s\n', ME.identifier, k0_flat(ME.message)); end
                    end
                end
            end
        end
        close(p);
    catch ME
        fprintf('%s.project\tERR %s | %s\n', key, ME.identifier, k0_flat(ME.message));
    end
end
k0_try('upgrade.again.std', @() class(matlab.internal.deployment.createProjectModelFromPRJ(P('k21std'))));
k0_try('upgrade.again.std.project', @() currentProject().Name);
try, close(currentProject); catch, end
k0_try('upgrade.bak', @() class(matlab.internal.deployment.createProjectModelFromPRJ([P('k21std') '.bak'])));
k0_try('upgrade.bak.project', @() currentProject().Name);
k0_try('upgrade.bak.tasks', @() strjoin(arrayfun(@(c) char(c.Name), currentProject().Categories, 'UniformOutput', false), ', '));
try, close(currentProject); catch, end
k0_try('upgrade.missing', @() class(matlab.internal.deployment.createProjectModelFromPRJ(fullfile(work, 'p', 'k21none.prj'))));
k0_try('upgrade.text', @() class(matlab.internal.deployment.createProjectModelFromPRJ(fullfile(work, 'p', 'k21text.prj'))));
k0_try('upgrade.stub', @() class(matlab.internal.deployment.createProjectModelFromPRJ(fullfile(work, 'p', 'k21stub.prj'))));
try, close(currentProject); catch, end
call('script.upgraded', @() createDeploymentScript(P('k21plain'), fullfile(scripts, 'deploy_upgraded.m')));
call('build.upgraded', @() deploytool('-build', P('k21plain')));

%% deploytool's one-argument form, the legacy apps' and open, each on a fresh copy of a project.
copies = {'deploytool', 'k21plain', @(f) deploytool(f); 'applicationCompiler', 'k21plain', @(f) applicationCompiler(f); 'libraryCompiler', 'k21c', @(f) libraryCompiler(f); ...
    'productionServerCompiler', 'k21mps', @(f) productionServerCompiler(f); 'open', 'k21plain', @(f) open(f); 'standaloneApplicationCompiler', 'k21plain', @(f) standaloneApplicationCompiler(f); ...
    'webAppCompiler', 'k21web', @(f) webAppCompiler(f)};
for k = 1:size(copies, 1)
    name = copies{k, 2};
    folder = fullfile(work, 'o', copies{k, 1});
    mkdir(folder);
    copyfile(fullfile(src, '*'), folder);
    copyfile([P(name) '.bak'], fullfile(folder, [name '.prj']));
    key = ['open.' copies{k, 1}];
    call(key, @() copies{k, 3}(fullfile(folder, [name '.prj'])));
    k0_try([key '.project'], @() currentProject().Name);
    k0_try([key '.bak'], @() isfile(fullfile(folder, [name '.prj.bak'])));
    k0_try([key '.defs'], @() sum(contains(relFiles(folder), ['resources' filesep 'project'])));
    try, close(currentProject); catch, end
end
k0_try('open.pwd', @() rel(pwd, work));
cd(work);

%% The catalogs the refusals and words come from.
texts = {'Compiler:build:compatibility:buildComplete', 'Compiler:build:compatibility:dataMarshallingWarning', 'Compiler:build:compatibility:failedToWriteScript', ...
    'Compiler:build:compatibility:hadoopNotSupported', 'Compiler:build:compatibility:incompatibleScriptExtension', 'Compiler:build:compatibility:invalidClassDefinition', ...
    'Compiler:build:compatibility:invalidOutputFileName', 'Compiler:build:compatibility:mccFlagsNotSupported', 'Compiler:build:compatibility:noExportedFunctionsSelected', ...
    'Compiler:build:compatibility:noMainFileSelected', 'Compiler:build:compatibility:notCompatibilityPRJ', 'Compiler:build:compatibility:packagingNotSupported', ...
    'Compiler:build:compatibility:projectDoesNotExist', 'Compiler:build:compatibility:scriptFileCannotExist', 'Compiler:build:compatibility:sdkNotAvailable', ...
    'Compiler:build:compatibility:typeSafeNotSupported', 'Compiler:build:compatibility:unsupportedPlatform', 'Compiler:package:compatibility:typeSafeNotSupported', ...
    'Compiler:package:compatibility:excelDataMarshallingWarning', 'Compiler:package:compatibility:packageComplete', 'Compiler:package:compatibility:incompatiblePlatform', ...
    'Compiler:deploytool:NotACompilerPRJ', 'Compiler:deploytool:OpenProjectRequiresDesktop'};
for k = 1:numel(texts)
    k0_try(['text.' texts{k}], @() getText(texts{k}));
end
end

function t = getText(key)
holes = {'{0}', '{1}', '{2}'};
for n = 0:3
    try
        t = k0_flat(getString(message(key, holes{1:n})));
        return
    catch
    end
end
t = '<no text>';
end

function writeIco(file)
% A 1x1 32-bit icon: ICONDIR, one ICONDIRENTRY, a BITMAPINFOHEADER, one pixel, one mask row.
fid = fopen(file, 'w');
fwrite(fid, uint16([0 1 1]), 'uint16');
fwrite(fid, uint8([1 1 0 0]), 'uint8');
fwrite(fid, uint16([1 32]), 'uint16');
fwrite(fid, uint32(40 + 4 + 4), 'uint32');
fwrite(fid, uint32(22), 'uint32');
fwrite(fid, uint32([40 1 2 (1 + 65536 * 32) 0 8 0 0 0 0]), 'uint32');
fwrite(fid, uint8([0 0 255 255]), 'uint8');
fwrite(fid, uint8([0 0 0 0]), 'uint8');
fclose(fid);
end

function s = describeStruct(st, prefix)
% Every field with its value, nested structs flattened, one line per leaf joined by ' ; '.
parts = {};
if isstruct(st) && numel(st) > 1
    for j = 1:numel(st)
        parts{end + 1} = describeStruct(st(j), sprintf('%s(%d)', prefix, j)); %#ok<AGROW>
    end
    s = strjoin(parts, ' ; ');
    return
end
f = fieldnames(st);
for i = 1:numel(f)
    v = st.(f{i});
    name = [prefix '.' f{i}];
    if isstruct(v)
        parts{end + 1} = describeStruct(v, name); %#ok<AGROW>
    elseif isa(v, 'missing')
        parts{end + 1} = sprintf('%s=<missing>', name); %#ok<AGROW>
    elseif isstring(v)
        if isscalar(v)
            parts{end + 1} = sprintf('%s="%s"', name, k0_flat(v)); %#ok<AGROW>
        else
            parts{end + 1} = sprintf('%s=[%s]', name, strjoin(cellstr(v(:)'), ' | ')); %#ok<AGROW>
        end
    else
        parts{end + 1} = sprintf('%s=%s', name, k0_show(v)); %#ok<AGROW>
    end
end
s = strjoin(parts, ' ; ');
end

function s = describeObject(o)
props = properties(o);
parts = {sprintf('class=%s', class(o))};
for i = 1:numel(props)
    try
        v = o.(props{i});
        if isobject(v) && ~isstring(v) && ~isenum(v)
            parts{end + 1} = sprintf('%s:<%s>', props{i}, class(v)); %#ok<AGROW>
        elseif isstruct(v)
            parts{end + 1} = sprintf('%s:{%s}', props{i}, describeStruct(v, '')); %#ok<AGROW>
        elseif isenum(v)
            parts{end + 1} = sprintf('%s:%s', props{i}, char(string(v))); %#ok<AGROW>
        elseif iscell(v)
            parts{end + 1} = sprintf('%s:{%s}', props{i}, strjoin(cellfun(@(e) k0_flat(e), v(:)', 'UniformOutput', false), ' | ')); %#ok<AGROW>
        else
            parts{end + 1} = sprintf('%s:%s', props{i}, k0_show(v)); %#ok<AGROW>
        end
    catch ME
        parts{end + 1} = sprintf('%s:ERR %s', props{i}, ME.identifier); %#ok<AGROW>
    end
end
s = strjoin(parts, ' ; ');
end

function m = standaloneParams(name)
m = containers.Map('KeyType', 'char', 'ValueType', 'any');
keys = {'param.appname', name, 'param.icon', '', 'param.icons', '', 'param.version', '1.0', 'param.authnamewatermark', '', ...
    'param.email', '', 'param.company', '', 'param.summary', '', 'param.description', '', 'param.screenshot', '', 'param.guid', '', ...
    'param.installpath.string', ['\' name '\'], 'param.installpath.combo', 'option.installpath.programfiles', 'param.logo', '', ...
    'param.install.notes', '', 'param.target.install.notes', '', 'param.intermediate', ['${PROJECT_ROOT}\' name '\for_testing'], ...
    'param.files.only', ['${PROJECT_ROOT}\' name '\for_redistribution_files_only'], 'param.output', ['${PROJECT_ROOT}\' name '\for_redistribution'], ...
    'param.logdir', ['${PROJECT_ROOT}\' name], 'param.enable.clean.build', 'false', 'param.user.defined.mcr.options', '', ...
    'param.target.type', 'subtarget.standalone', 'param.support.packages', '', 'param.web.mcr', 'true', 'param.package.mcr', 'false', ...
    'param.no.mcr', 'false', 'param.web.mcr.name', 'MyAppInstaller_web', 'param.package.mcr.name', 'MyAppInstaller_mcr', ...
    'param.no.mcr.name', 'MyAppInstaller_app', 'param.windows.command.prompt', 'true', 'param.create.log', 'false', 'param.log.file', '', ...
    'param.native.matlab', 'false', 'param.checkbox', 'false', 'param.example', '', 'param.help.text', ''};
m('order') = keys(1:2:end);
for i = 1:2:numel(keys), m(keys{i}) = keys{i + 1}; end
end

function m = libraryParams(name, sub)
m = containers.Map('KeyType', 'char', 'ValueType', 'any');
keys = {'param.appname', name, 'param.icon', '', 'param.icons', '', 'param.version', '1.0', 'param.authnamewatermark', '', ...
    'param.email', '', 'param.company', '', 'param.summary', '', 'param.description', '', 'param.screenshot', '', 'param.guid', '', ...
    'param.installpath.string', ['\' name '\'], 'param.installpath.combo', 'option.installpath.programfiles', 'param.logo', '', ...
    'param.install.notes', '', 'param.target.install.notes', '', 'param.intermediate', ['${PROJECT_ROOT}\' name '\for_testing'], ...
    'param.files.only', ['${PROJECT_ROOT}\' name '\for_redistribution_files_only'], 'param.output', ['${PROJECT_ROOT}\' name '\for_redistribution'], ...
    'param.logdir', ['${PROJECT_ROOT}\' name], 'param.enable.clean.build', 'false', 'param.user.defined.mcr.options', '', ...
    'param.target.type', sub, 'param.support.packages', '', 'param.namespace', '', 'param.classorg', '', ...
    'param.web.mcr', 'true', 'param.package.mcr', 'false', 'param.no.mcr', 'false', 'param.web.mcr.name', 'MyAppInstaller_web', ...
    'param.package.mcr.name', 'MyAppInstaller_mcr', 'param.no.mcr.name', 'MyAppInstaller_app', 'param.windows.command.prompt', 'true', ...
    'param.create.log', 'false', 'param.log.file', '', 'param.user.only.registration', 'false', 'param.assembly.net.version', 'option.net.version.four', ...
    'param.assembly.type', 'false', 'param.encryption.key.file', '', 'param.net.enable.remoting', 'false', 'param.net.tsa.enable', 'false', ...
    'param.net.tsa.assembly', '', 'param.net.tsa.interface', '', 'param.net.tsa.namespace', '', 'param.net.tsa.superclass', 'Class1', ...
    'param.net.tsa.metadata', '', 'param.net.tsa.metadata.assembly', '', 'param.net.saved.interface', '', 'param.cpp.api', 'option.cpp.all'};
m('order') = keys(1:2:end);
for i = 1:2:numel(keys), m(keys{i}) = keys{i + 1}; end
end

function m = mpsParams(name, sub)
m = containers.Map('KeyType', 'char', 'ValueType', 'any');
keys = {'param.appname', name, 'param.icon', '', 'param.icons', '', 'param.version', '1.0', ...
    'param.intermediate', ['${PROJECT_ROOT}\' name '\for_testing'], 'param.files.only', ['${PROJECT_ROOT}\' name '\for_redistribution_files_only'], ...
    'param.output', ['${PROJECT_ROOT}\' name '\for_redistribution'], 'param.logdir', ['${PROJECT_ROOT}\' name], 'param.enable.clean.build', 'false', ...
    'param.user.defined.mcr.options', '', 'param.server.ctf', ['${PROJECT_ROOT}\' name '\for_testing\server\' name '.ctf'], ...
    'param.server.readme', ['${PROJECT_ROOT}\' name '\for_testing\server\readme.txt'], 'param.target.type', sub, 'param.support.packages', '', ...
    'param.required.mcr.products', '', 'param.discovery.file', '', 'param.classorg', '', 'param.function.data', '', ...
    'param.user.only.registration', 'false', 'param.mps.server.dir', 'server', 'param.mps.client.dir', 'client', ...
    'param.time.out', '60', 'param.max.size', '750', 'param.certificate.file', '', 'param.mads.server.configuration', ''};
m('order') = keys(1:2:end);
for i = 1:2:numel(keys), m(keys{i}) = keys{i + 1}; end
end

function m = webParams(name)
m = containers.Map('KeyType', 'char', 'ValueType', 'any');
keys = {'param.appname', name, 'param.intermediate', ['${PROJECT_ROOT}\' name '\'], 'param.files.only', ['${PROJECT_ROOT}\' name '\'], ...
    'param.output', '', 'param.checkbox', 'false', 'param.logdir', ['${PROJECT_ROOT}\' name '\'], 'param.target.type', 'subtarget.web.app', ...
    'param.user.defined.mcr.options', '', 'param.support.packages', ''};
m('order') = keys(1:2:end);
for i = 1:2:numel(keys), m(keys{i}) = keys{i + 1}; end
end

function writePrj(file, name, target, targetName, params, filesets, location, plugin, iconsList)
% The legacy form as the apps wrote it: the params, the unset ones listed, the file sets, the
% deliverables, workflow, matlab and platform blocks.
if nargin < 7 || isempty(location), location = fileparts(file); end
if nargin < 8 || isempty(plugin), plugin = 'plugin.ezdeploy'; end
if nargin < 9, iconsList = {}; end
t = sprintf('<deployment-project plugin="%s" plugin-version="1.0">\n', plugin);
t = [t sprintf('  <configuration build-checksum="1" file="%s" location="%s" name="%s" target="%s" target-name="%s">\n', file, location, name, target, targetName)];
order = params('order');
unset = {};
for i = 1:numel(order)
    v = params(order{i});
    if strcmp(order{i}, 'param.icons') && ~isempty(iconsList)
        t = [t sprintf('    <param.icons>\n')]; %#ok<AGROW>
        for j = 1:numel(iconsList), t = [t sprintf('      <file>%s</file>\n', iconsList{j})]; end %#ok<AGROW>
        t = [t sprintf('    </param.icons>\n')]; %#ok<AGROW>
    elseif isempty(v)
        t = [t sprintf('    <%s />\n', order{i})]; %#ok<AGROW>
        unset{end + 1} = order{i}; %#ok<AGROW>
    else
        t = [t sprintf('    <%s>%s</%s>\n', order{i}, escape(v), order{i})]; %#ok<AGROW>
    end
end
t = [t sprintf('    <unset>\n')];
for i = 1:numel(unset), t = [t sprintf('      <%s />\n', unset{i})]; end %#ok<AGROW>
t = [t sprintf('    </unset>\n')];
f = fieldnames(filesets);
for i = 1:numel(f)
    tag = strrep(f{i}, '_', '.');
    v = filesets.(f{i});
    if strcmp(f{i}, 'fileset_classes') && ~isempty(v)
        t = [t sprintf('    <%s>\n      <entity.package name="">\n', tag)]; %#ok<AGROW>
        for c = 1:2:numel(v)
            t = [t sprintf('        <entity.class name="%s">\n', v{c})]; %#ok<AGROW>
            files = v{c + 1};
            for j = 1:numel(files), t = [t sprintf('          <file>%s</file>\n', files{j})]; end %#ok<AGROW>
            t = [t sprintf('        </entity.class>\n')]; %#ok<AGROW>
        end
        t = [t sprintf('      </entity.package>\n    </%s>\n', tag)]; %#ok<AGROW>
    elseif isempty(v)
        t = [t sprintf('    <%s />\n', tag)]; %#ok<AGROW>
    else
        t = [t sprintf('    <%s>\n', tag)]; %#ok<AGROW>
        for j = 1:numel(v), t = [t sprintf('      <file>%s</file>\n', v{j})]; end %#ok<AGROW>
        t = [t sprintf('    </%s>\n', tag)]; %#ok<AGROW>
    end
end
t = [t sprintf('    <build-deliverables />\n    <workflow />\n    <matlab>\n      <root>%s</root>\n      <toolboxes />\n    </matlab>\n', matlabroot)];
t = [t sprintf(['    <platform>\n      <unix>false</unix>\n      <mac>false</mac>\n      <windows>true</windows>\n      <win2k>false</win2k>\n' ...
    '      <winxp>false</winxp>\n      <vista>false</vista>\n      <linux>false</linux>\n      <solaris>false</solaris>\n      <osver>10.0</osver>\n' ...
    '      <os32>false</os32>\n      <os64>true</os64>\n      <arch>win64</arch>\n      <matlab>true</matlab>\n    </platform>\n'])];
t = [t sprintf('  </configuration>\n')];
t = [t sprintf('</deployment-project>')];
writeText(file, ['<?xml version="1.0" encoding="UTF-8"?>' newline t]);
end

function s = escape(s)
s = strrep(s, '&', '&amp;');
s = strrep(s, '<', '&lt;');
s = strrep(s, '>', '&gt;');
end

function r = rel(p, root)
r = char(p);
if startsWith(r, [root filesep]), r = r(numel(root) + 2:end); end
if strcmp(r, root), r = '<root>'; end
end

function c = relFiles(root)
d = dir(fullfile(root, '**', '*'));
d = d(~[d.isdir]);
c = sort(arrayfun(@(e) strrep(fullfile(e.folder, e.name), [root filesep], ''), d, 'UniformOutput', false))';
end

function call(key, f)
% One call: what it printed, ok or its refusal, then its warning.
lastwarn('', '');
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

function writeText(file, text)
fid = fopen(file, 'w');
fprintf(fid, '%s', text);
fclose(fid);
end

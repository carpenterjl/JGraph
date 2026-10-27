function probe_k19_apps(work)
%PROBE_K19_APPS Asks R2025b's compiler app commands every question a session without a desktop can
%answer (compiler and SDK plan, stage K19): the target-specific apps of R2025a and later
%(standaloneApplicationCompiler and its siblings), the legacy ones (applicationCompiler,
%libraryCompiler, productionServerCompiler, deploytool, webAppCompiler, hadoopCompiler) with their
%warnings, help and refusals, what each does with no project, a stub project, a MATLAB file and a
%missing file, and the catalog texts the apps' windows are made of. Run under matlab -batch
%-noFigureWindows; nothing here opens a window (the app gallery is published to a desktop that is
%not there). One key<TAB>value line per question, what a call printed as indented lines under
%<key>.printed.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
mkdir(src);
copyfile(fullfile(here, 'src', 'k6', 'k6_hello.m'), src);
old = cd(src);
cleanup = onCleanup(@() cd(old)); %#ok<NASGU>

% A stub of a legacy deployment project, a text file with the extension, a MATLAB file, a text file.
writeText(fullfile(src, 'k19stub.prj'), sprintf(['<?xml version="1.0" encoding="UTF-8"?>\n' ...
    '<deployment-project plugin="plugin.ezdeploy" plugin-version="1.0">\n' ...
    '  <configuration build-checksum="1" file="k19stub.prj" location="." name="k19stub" target="target.ezdeploy.standalone" target-name="Application Compiler">\n' ...
    '    <param.appname>k19stub</param.appname>\n' ...
    '    <fileset.main><file>${PROJECT_ROOT}\\k6_hello.m</file></fileset.main>\n' ...
    '  </configuration>\n' ...
    '</deployment-project>\n']));
writeText(fullfile(src, 'k19text.prj'), sprintf('not a project\n'));
writeText(fullfile(src, 'k19note.txt'), sprintf('hello\n'));

%% The names: existence, counts, where they are, and their first help line.
names = {'standaloneApplicationCompiler', 'productionServerArchiveCompiler', 'excelAddinCompiler', ...
    'dotNetAssemblyCompiler', 'pythonPackageCompiler', 'cppSharedLibraryCompiler', 'cSharedLibraryCompiler', ...
    'javaPackageCompiler', 'comComponentCompiler', 'applicationCompiler', 'libraryCompiler', ...
    'productionServerCompiler', 'deploytool', 'webAppCompiler', 'hadoopCompiler', 'createDeploymentScript', ...
    'compiler.internal.displayDeploymentUIWarning', 'compiler.internal.launchui', 'excelAddInCompiler'};
for k = 1:numel(names)
    n = names{k};
    k0_try(['exist.' n], @() exist(n));
    k0_try(['nargin.' n], @() nargin(n));
    k0_try(['nargout.' n], @() nargout(n));
    k0_try(['which.' n], @() which(n));
    k0_try(['h1.' n], @() firstLine(evalc(['help ' n])));
end

%% The target-specific apps (R2025a and later): a project file opens, else the gallery is published to.
takesFile = {'standaloneApplicationCompiler', 'productionServerArchiveCompiler'};
for k = 1:numel(takesFile)
    n = takesFile{k};
    f = str2func(n);
    call([n '.noargs'], @() f());
    call([n '.missing'], @() f('k19none.prj'));
    call([n '.missing.string'], @() f("k19none.prj"));
    call([n '.stub'], @() f('k19stub.prj'));
    call([n '.text'], @() f('k19text.prj'));
    call([n '.mfile'], @() f('k6_hello.m'));
    call([n '.txt'], @() f('k19note.txt'));
    call([n '.number'], @() f(1));
    call([n '.empty'], @() f(''));
    call([n '.two'], @() f('a', 'b'));
    call([n '.output'], @() nargout1(f));
end
noFile = {'excelAddinCompiler', 'dotNetAssemblyCompiler', 'pythonPackageCompiler', 'cppSharedLibraryCompiler', ...
    'cSharedLibraryCompiler', 'javaPackageCompiler', 'comComponentCompiler'};
for k = 1:numel(noFile)
    n = noFile{k};
    f = str2func(n);
    call([n '.noargs'], @() f());
    call([n '.one'], @() f('k19stub.prj'));
    call([n '.output'], @() nargout1(f));
end

%% The legacy apps: the warning, the license, launchui's forms.
legacy = {'applicationCompiler', 'libraryCompiler', 'productionServerCompiler', 'deploytool', 'webAppCompiler'};
for k = 1:numel(legacy)
    n = legacy{k};
    f = str2func(n);
    call([n '.noargs'], @() f());
    call([n '.number'], @() f(1));
    call([n '.strings'], @() f(["a" "b"]));
    call([n '.cell'], @() f({'a'}));
    call([n '.charmatrix'], @() f(['ab'; 'cd']));
    call([n '.empty'], @() f(''));
    call([n '.help'], @() f('-?'));
    call([n '.help.slash'], @() f('/?'));
    call([n '.help.string'], @() f("-?"));
    call([n '.missing'], @() f('k19none.prj'));
    call([n '.stub'], @() f('k19stub.prj'));
    call([n '.text'], @() f('k19text.prj'));
    call([n '.mfile'], @() f('k6_hello.m'));
    call([n '.txt'], @() f('k19note.txt'));
    call([n '.build.missing'], @() f('-build', 'k19none.prj'));
    call([n '.build.stub'], @() f('-build', 'k19stub.prj'));
    call([n '.build.slash'], @() f('/build', 'k19stub.prj'));
    call([n '.package.stub'], @() f('-package', 'k19stub.prj'));
    call([n '.package.mfile'], @() f('-package', 'k6_hello.m'));
    call([n '.build.number'], @() f('-build', 2));
    call([n '.build.upper'], @() f('-BUILD', 'k19stub.prj'));
    call([n '.bad.two'], @() f('-foo', 'k19stub.prj'));
    call([n '.three'], @() f('a', 'b', 'c'));
    call([n '.output'], @() nargout1(f));
end
call('hadoopCompiler.noargs', @() hadoopCompiler());
call('hadoopCompiler.one', @() hadoopCompiler('x'));
call('hadoopCompiler.output', @() nargout1(@hadoopCompiler));
call('excelAddInCompiler.noargs', @() excelAddInCompiler());

%% What the apps are built on.
call('publish.standalone', @() message.publish('/matlab_compiler/app_gallery', 'StandaloneDesktopApp'));
call('publish.empty', @() message.publish('/matlab_compiler/app_gallery', ''));
call('uiwarning.applicationCompiler', @() compiler.internal.displayDeploymentUIWarning('applicationCompiler'));
call('uiwarning.productionServerCompiler', @() compiler.internal.displayDeploymentUIWarning('productionServerCompiler'));
call('uiwarning.libraryCompiler', @() compiler.internal.displayDeploymentUIWarning('libraryCompiler', 'x.prj'));
call('uiwarning.deploytool', @() compiler.internal.displayDeploymentUIWarning('deploytool'));
call('uiwarning.webAppCompiler', @() compiler.internal.displayDeploymentUIWarning('webAppCompiler'));
call('uiwarning.deploytool.build', @() compiler.internal.displayDeploymentUIWarning('deploytool', '-build', 'x.prj'));
call('uiwarning.applicationCompiler.build', @() compiler.internal.displayDeploymentUIWarning('applicationCompiler', '/package', 'x.prj'));
call('uiwarning.applicationCompiler.other', @() compiler.internal.displayDeploymentUIWarning('applicationCompiler', '-foo', 'x.prj'));
call('uiwarning.unknown', @() compiler.internal.displayDeploymentUIWarning('other'));
call('uiwarning.four', @() compiler.internal.displayDeploymentUIWarning('deploytool', 'a', 'b', 'c'));
call('launchui.none', @() compiler.internal.launchui('applicationCompiler'));
call('launchui.other', @() compiler.internal.launchui('other'));
call('launchui.number', @() compiler.internal.launchui('deploytool', 3));
k0_try('license.compiler', @() license('checkout', 'Compiler'));
k0_try('license.sdk', @() license('checkout', 'MATLAB_Builder_for_Java'));
k0_try('license.test', @() license('test', 'Compiler'));
call('script.missing', @() createDeploymentScript('k19none.prj', 'k19script.m'));
call('script.stub', @() createDeploymentScript('k19stub.prj', 'k19script.m'));
call('script.text', @() createDeploymentScript('k19text.prj', 'k19script.m'));
call('script.mfile', @() createDeploymentScript('k6_hello.m', 'k19script.m'));
call('script.noargs', @() createDeploymentScript());
call('script.one', @() createDeploymentScript('k19stub.prj'));
call('script.three', @() createDeploymentScript('k19stub.prj', 'k19script.m', 'x'));
k0_try('prjstruct.stub', @() describe(compiler.internal.readPRJStruct('k19stub.prj')));
k0_try('prjstruct.text', @() describe(compiler.internal.readPRJStruct('k19text.prj')));
k0_try('prjstruct.missing', @() describe(compiler.internal.readPRJStruct('k19none.prj')));
k0_try('open.stub', @() open('k19stub.prj'));
k0_try('open.missing', @() open('k19none.prj'));
k0_try('open.text', @() open('k19text.prj'));
k0_try('open.mfile', @() open('k6_hello.m'));
k0_try('webui', @() feature('webui'));
k0_try('capability.local', @() matlab.internal.capability.Capability.isSupported(matlab.internal.capability.Capability.LocalClient));

%% The catalog texts the apps' windows are made of (resources/<catalog>/en/*.xml).
texts = {
    'deployment:desktop:Tool_standaloneApplicationCompiler_Label'
    'deployment:desktop:Tool_standaloneApplicationCompiler_Description'
    'deployment:desktop:Tool_productionServerArchiveCompiler_Label'
    'deployment:desktop:Tool_productionServerArchiveCompiler_Description'
    'deployment:desktop:Tool_excelAddinCompiler_Label'
    'deployment:desktop:Tool_dotNetAssemblyCompiler_Label'
    'deployment:desktop:Tool_pythonPackageCompiler_Label'
    'deployment:desktop:Tool_cppSharedLibraryCompiler_Label'
    'deployment:desktop:Tool_cSharedLibraryCompiler_Label'
    'deployment:desktop:Tool_javaPackageCompiler_Label'
    'deployment:desktop:Tool_comComponentCompiler_Label'
    'deployment:desktop:Tool_webAppCompiler_Label'
    'Compiler:desktop:Tool_applicationCompiler_Label'
    'Compiler:desktop:Tool_libraryCompiler_Label'
    'deployment:model:CONFIGTYPE_STANDALONEAPP'
    'deployment:model:CONFIGTYPE_STANDALONEAPP_DESC'
    'deployment:model:CONFIGTYPE_MPSARCHIVE'
    'deployment:model:CONFIGTYPE_MPSARCHIVE_DESC'
    'deployment:model:CONFIGTYPE_DOTNETASSEMBLY_DESC'
    'deployment:model:CONFIGTYPE_PYTHONPACKAGE_DESC'
    'deployment:model:CONFIGTYPE_CPPSHAREDLIB_DESC'
    'deployment:model:CONFIGTYPE_CSHAREDLIB_DESC'
    'deployment:model:CONFIGTYPE_EXCELADDIN_DESC'
    'deployment:model:TITLE_OUTPUT_FOLDER_PATH'
    'compiler_ui_common:messages:mainFile'
    'compiler_ui_common:messages:exportedFunctionsTitle'
    'compiler_ui_common:messages:build'
    'compiler_ui_common:messages:buildAndPackage'
    'compiler_ui_common:messages:createBuildScriptLabel'
    'compiler_ui_common:messages:showCode'
    'compiler_ui_common:messages:hideCode'
    'compiler_ui_common:messages:installerSettingsLabel'
    'compiler_ui_common:messages:packageTypeLabel'
    'compiler_ui_common:messages:webRuntimeDescription'
    'compiler_ui_common:messages:packageRuntimeDescription'
    'compiler_ui_common:messages:packageNoInstallerDescription'
    'compiler_ui_common:messages:outputLocationsLabel'
    'compiler_ui_common:messages:outputLocationsBuildDescription'
    'compiler_ui_common:messages:outputLocationsPackageDescription'
    'compiler_ui_common:messages:filesPackaged'
    'compiler_ui_common:messages:autoDetectDataFiles'
    'compiler_ui_common:messages:enableAutoDetectDataFiles'
    'compiler_ui_common:messages:enableObfuscation'
    'compiler_ui_common:messages:obfuscation'
    'compiler_ui_common:messages:installerDetails'
    'compiler_ui_common:messages:installerImages'
    'compiler_ui_common:messages:installRootWindows'
    'compiler_ui_common:messages:installSubfolder'
    'compiler_ui_common:messages:readyToPackage'
    'compiler_ui_common:messages:missingMain'
    'compiler_ui_common:messages:noRequiredFiles'
    'compiler_ui_common:messages:packageDialogBuildSuccess'
    'compiler_ui_common:messages:packageDialogBuildWithWarnings'
    'compiler_ui_common:messages:packageDialogBuildError'
    'compiler_ui_common:messages:packageDialogFirstPhaseLineStandalone'
    'compiler_ui_common:messages:packageDialogFirstPhaseLineMPS'
    'compiler_ui_common:messages:packageDialogFirstPhaseLineNet'
    'compiler_ui_common:messages:packageDialogFirstPhaseLinePython'
    'compiler_ui_common:messages:packageDialogFirstPhaseLineCpp'
    'compiler_ui_common:messages:packageDialogFirstPhaseLineC'
    'compiler_ui_common:messages:packageDialogFirstPhaseLineExcel'
    'compiler_ui_common:messages:packageDialogSecondPhaseCreateInstaller'
    'compiler_ui_common:messages:packageDialogOpenOutputLabel'
    'compiler_ui_common:messages:packageDialogShowLogLabel'
    'compiler_ui_common:messages:packageDialogHideLogLabel'
    'compiler_ui_common:messages:packageNotSupported'
    'compiler_ui_common:deploymentscript:buildIntro'
    'compiler_ui_common:deploymentscript:packageIntro'
    'compiler_ui_common:deploymentscript:setProjectRoot'
    'compiler_ui_common:deploymentscript:downloadRuntime'
    'compiler_ui_common:deploymentscript:assemblyVersionPreLines'
    'compiler_ui_common:deploymentscript:libraryVersionPreLines'
    'compiler_ui_standalone:messages:applicationInfo'
    'compiler_ui_standalone:messages:applicationType'
    'compiler_ui_standalone:messages:standaloneApplication'
    'compiler_ui_standalone:messages:standaloneWindowsApplication'
    'compiler_ui_standalone:messages:executableDetails'
    'compiler_ui_standalone:messages:executableNameLabel'
    'compiler_ui_standalone:messages:inputTypeLabel'
    'compiler_ui_standalone:messages:inputTreatmentChar'
    'compiler_ui_standalone:messages:inputTreatmentNumeric'
    'compiler_ui_standalone:messages:requiredFilesMessage'
    'compiler_ui_standalone:messages:standaloneImages'
    'compiler_ui_standalone:messages:initialInfo'
    'compiler_ui_standalone:messages:appHelp'
    'compilersdk_ui_mps:messages:archiveInfo'
    'compilersdk_ui_mps:messages:functionSignatureTitle'
    'compilersdk_ui_mps:messages:functionSignaturePlaceholder'
    'compilersdk_ui_mps:messages:requiredFilesLabel'
    'compilersdk_ui_mps:messages:testClient'
    'compilersdk_ui_mps:messages:cannotOpenMessage'
    'compilersdk_ui_dotnet:messages:assemblyInfoTitle'
    'compilersdk_ui_dotnet:messages:netAPISelectionLabel'
    'compilersdk_ui_dotnet:messages:netVersion'
    'compilersdk_ui_dotnet:messages:netVersionFive'
    'compilersdk_ui_dotnet:messages:filesRequiredLabel'
    'compilersdk_ui_python:messages:packageInfoTitle'
    'compilersdk_ui_python:messages:filesRequiredLabel'
    'compilersdk_ui_cppSharedLibrary:messages:apiSelectionTitle'
    'compiler_ui_excel:messages:addinInfo'
    'compiler_ui_excel:messages:addinName'
    'compiler_ui_excel:messages:filesRequiredLabel'
    'deployment_common:messages:devAndTestLabel'
    'Compiler:deploytool:JavaAPIToBeRemoved|a|b'
    'Compiler:deploytool:DeploytoolFunctionalityToBeRemoved|a'
    'Compiler:deploytool:DeploytoolBuildPackageToBeRemoved|a'
    'Compiler:deploytool:invalidFirstInputOneInput|a|b'
    'Compiler:deploytool:invalidFirstInputTwoInputs|a|b'
    'Compiler:deploytool:HadoopCompilerRemoved'
    'Compiler:deploytool:OpenProjectRequiresDesktop'
    'Compiler:deploytool:NotACompilerPRJ|a'
    'Compiler:deploytool:MatlabOnlineSupport|a'
    'Compiler:deploytool:license_checkout_error'
    };
for k = 1:numel(texts)
    parts = strsplit(texts{k}, '|');
    k0_try(['text.' parts{1}], @() k0_flat(getString(message(parts{:}))));
end
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

function nargout1(f)
x = f(); %#ok<NASGU>
end

function t = firstLine(txt)
lines = splitlines(strtrim(regexprep(txt, '<a [^>]*>|</a>', '')));
t = strtrim(lines{1});
end

function t = describe(v)
if isstruct(v)
    t = ['struct ' strjoin(fieldnames(v)', ',')];
else
    t = class(v);
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

function writeText(file, text)
fid = fopen(file, 'w');
fprintf(fid, '%s', text);
fclose(fid);
end

function src = ds1b_appsource(app)
% DS1b (designer plan): where an app the probes check comes from. Returns a struct:
%   method      createComponents as JGraph's designer writes it (rt/jgraph/<app>__code.m, left with
%               the tree by the local test AppDesignerTreeWriterTests);
%   classText   the whole class, for App Designer's serializer;
%   folder      the folder the app's images are found from (pathToMLAPP);
%   appType     the app's type as App Designer records it.
% The fixture app is this project's (tests/JGraph.Tests/Scripting/Designer/DesignerSpecApp.m, its
% image made here); every other app is one R2025b ships, read in place.
here = fileparts(mfilename('fullpath'));
src.method = fileread(fullfile(here, 'rt', 'jgraph', [app '__code.m']));
if strcmp(app, 'DesignerSpecApp')
    src.classText = fileread(fullfile(here, '..', '..', '..', '..', 'tests', 'JGraph.Tests', 'Scripting', 'Designer', 'DesignerSpecApp.m'));
    src.folder = fullfile(here, 'rt', 'spec');
    if ~isfolder(fullfile(src.folder, 'img')), mkdir(fullfile(src.folder, 'img')); end
    if ~isfile(fullfile(src.folder, 'img', 'logo.png'))
        imwrite(uint8(255 * rand(8, 8, 3)), fullfile(src.folder, 'img', 'logo.png'));
    end
    src.appType = 'Standard';
else
    shipped = dir(fullfile(matlabroot, 'toolbox', '**', [app '.mlapp']));
    fn = fullfile(shipped(1).folder, shipped(1).name);
    src.classText = matlab.internal.getCode(fn);
    src.folder = shipped(1).folder;
    meta = appdesigner.internal.serialization.DeserializerFactory.createDeserializer(fn).getAppMetadata();
    src.appType = 'Standard'; if isfield(meta, 'AppType'), src.appType = char(meta.AppType); end
end
end

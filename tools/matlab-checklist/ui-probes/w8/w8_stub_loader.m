classdef w8_stub_loader < appdesigner.internal.serialization.loader.interface.Loader
    %W8_STUB_LOADER Hands HelpCommentsParser a class name, for w8_details.

    methods
        function appData = load(~)
            appData = struct('code', struct('ClassName', 'W8Probe'));
        end
    end
end
